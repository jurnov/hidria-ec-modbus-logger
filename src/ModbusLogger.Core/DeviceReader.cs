using System.Diagnostics;
using System.IO.Ports;
using System.Net.Sockets;
using NModbus;

namespace ModbusLogger.Core;

public sealed record RegisterReading(RegisterDef Def, ushort[] RawWords, double Value);

public sealed record DeviceReadResult(
    DateTime Timestamp,
    bool Success,
    string? Error,
    long ElapsedMs,
    IReadOnlyList<RegisterReading> Readings)
{
    public static DeviceReadResult Failed(DateTime ts, string error, long elapsedMs) =>
        new(ts, false, error, elapsedMs, Array.Empty<RegisterReading>());
}

public static class DeviceReader
{
    /// <summary>Ustvari serijski port iz nastavitev v devices.json.</summary>
    public static SerialPort CreatePort(SerialSettings s) => new(s.Port)
    {
        BaudRate = s.Baud,
        DataBits = 8,
        Parity = s.Parity.ToLowerInvariant() switch
        {
            "none" => Parity.None,
            "odd" => Parity.Odd,
            _ => Parity.Even,
        },
        StopBits = s.StopBits == 2 ? StopBits.Two : StopBits.One,
        ReadTimeout = s.TimeoutMs,
        WriteTimeout = s.TimeoutMs,
    };

    /// <summary>
    /// Vzpostavi TCP povezavo za Modbus TCP; connect je omejen na TimeoutMs, da oddaljen/
    /// nedosegljiv naslov ne blokira zanke za (privzeto precej daljši) OS-timeout.
    /// </summary>
    public static TcpClient CreateTcpClient(TcpSettings s)
    {
        var client = new TcpClient();
        if (!client.ConnectAsync(s.Host, s.Port).Wait(s.TimeoutMs))
        {
            client.Dispose();
            throw new TimeoutException($"povezava na {s.Host}:{s.Port} ni uspela v {s.TimeoutMs} ms");
        }
        client.ReceiveTimeout = s.TimeoutMs;
        client.SendTimeout = s.TimeoutMs;
        return client;
    }

    /// <summary>Prebere vse poll groupe profila z ene naprave in dekodira registre.</summary>
    public static DeviceReadResult Read(IModbusMaster master, byte slaveId, DeviceProfile profile)
    {
        var ts = DateTime.Now;
        var sw = Stopwatch.StartNew();
        var blocks = new List<(PollGroup Group, ushort[] Data)>();

        foreach (var group in profile.PollGroups)
        {
            try
            {
                ushort[] data = group.FunctionValue == ModbusFunction.ReadHoldingRegisters
                    ? master.ReadHoldingRegisters(slaveId, group.StartAddressValue, group.Count)
                    : master.ReadInputRegisters(slaveId, group.StartAddressValue, group.Count);
                blocks.Add((group, data));
            }
            catch (TimeoutException)
            {
                return DeviceReadResult.Failed(ts, "timeout — naprava ne odgovarja", sw.ElapsedMilliseconds);
            }
            catch (SlaveException ex)
            {
                return DeviceReadResult.Failed(ts,
                    $"Modbus exception (function {ex.FunctionCode}, code {ex.SlaveExceptionCode}) — naprava zavrača zahtevo, preveri naslove v profilu",
                    sw.ElapsedMilliseconds);
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException or SocketException)
            {
                return DeviceReadResult.Failed(ts, $"napaka povezave: {ex.Message}", sw.ElapsedMilliseconds);
            }
        }

        var readings = new List<RegisterReading>(profile.Registers.Count);
        foreach (var def in profile.Registers)
        {
            var (group, data) = blocks.First(b =>
                b.Group.FunctionValue == def.FunctionValue &&
                def.AddressValue >= b.Group.StartAddressValue &&
                def.AddressValue + def.WordCount <= b.Group.StartAddressValue + b.Group.Count);
            var words = new ushort[def.WordCount];
            Array.Copy(data, def.AddressValue - group.StartAddressValue, words, 0, def.WordCount);
            readings.Add(new RegisterReading(def, words, DecodeValue(def, words)));
        }

        return new DeviceReadResult(ts, true, null, sw.ElapsedMilliseconds, readings);
    }

    /// <summary>Iz surovih besed naredi fizikalno vrednost po tipu, vrstnem redu besed, skali in odmiku.</summary>
    public static double DecodeValue(RegisterDef def, ushort[] words)
    {
        uint u32 = words.Length == 2
            ? def.WordOrder == "little"
                ? ((uint)words[1] << 16) | words[0]
                : ((uint)words[0] << 16) | words[1]
            : words[0];

        double raw = def.Type switch
        {
            "int16" => (short)words[0],
            "uint32" => u32,
            "int32" => (int)u32,
            "float32" => BitConverter.UInt32BitsToSingle(u32),
            _ => words[0],   // uint16
        };

        return raw * def.Scale + def.Offset;
    }
}
