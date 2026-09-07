using System.Diagnostics;
using System.IO.Ports;
using System.Net.Sockets;
using NModbus;
using NModbus.Device;
using NModbus.IO;
using NModbus.Serial;

namespace ModbusLogger.Core;

/// <summary>
/// Ciklično bere vse omogočene naprave in meritve pošilja v log sinke. Deluje prek serijskega
/// porta (RS-485) ali Modbus TCP, glede na Config.ConnectionType. Preživi izpad povezave
/// (izvlečen USB pretvornik ali prekinjena TCP povezava): jo zapre in poskuša znova vzpostaviti,
/// vmes pa v sinke zapisuje vrstice z napako, da izpad ostane viden v podatkih.
/// </summary>
public sealed class PollService : IDisposable
{
    private readonly LoadedConfig _loaded;
    private readonly IReadOnlyList<ILogSink> _sinks;
    private readonly List<DeviceEntry> _devices;
    private readonly ManualResetEventSlim _intervalChanged = new(false);

    private SerialPort? _port;
    private TcpClient? _tcpClient;
    private IModbusMaster? _master;
    private volatile int _sampleIntervalSeconds;
    private readonly int _writeIntervalSeconds;
    private DateTime _lastWriteTime = DateTime.MinValue;

    private bool IsTcp => string.Equals(_loaded.Config.ConnectionType, "tcp", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Čas vzorčenja (komunikacije z napravami) v sekundah. Sprememba med tekom velja takoj: če
    /// aplikacija trenutno čaka na naslednji cikel, se čakanje glede na novo vrednost prilagodi
    /// (podaljša ali prekine), ne šele po naslednjem ciklu.
    /// </summary>
    public int SampleIntervalSeconds
    {
        get => _sampleIntervalSeconds;
        set
        {
            _sampleIntervalSeconds = Math.Max(1, value);
            _intervalChanged.Set();
        }
    }

    /// <summary>Dogodki za prikaz (konzola zdaj, GUI kasneje): sporočila o portu, napakah sinkov ...</summary>
    public event Action<string>? Message;

    /// <summary>Sproži se po vsakem branju naprave, uspešnem ali ne.</summary>
    public event Action<DeviceEntry, DeviceReadResult>? DeviceRead;

    /// <summary>Surov Modbus RTU promet (zahtevek/odgovor v HEX) — preživi ponovno odpiranje porta.</summary>
    public TrafficLog Traffic { get; } = new();

    public PollService(LoadedConfig loaded, IEnumerable<ILogSink> sinks)
    {
        _loaded = loaded;
        _sinks = sinks.ToList();
        _devices = loaded.Config.Devices.Where(d => d.Enabled).ToList();
        _sampleIntervalSeconds = Math.Max(1, loaded.Config.SampleIntervalSeconds);
        _writeIntervalSeconds = Math.Max(1, loaded.Config.WriteIntervalSeconds);
    }

    /// <summary>Blokirajoča zanka; kliči s CancellationTokenom za ustavitev. once=true naredi en cikel.</summary>
    public void Run(CancellationToken ct, bool once = false)
    {
        while (!ct.IsCancellationRequested)
        {
            var cycle = Stopwatch.StartNew();

            // Vsak cikel bere naprave (za živ prikaz); v sinke (CSV/MySQL) pa se zapiše le, ko od
            // zadnjega zapisa mine WriteIntervalSeconds — s tem je hitrost komunikacije (vzorčenje)
            // ločena od pogostosti dejanskega beleženja.
            bool shouldWrite = once || DateTime.Now - _lastWriteTime >= TimeSpan.FromSeconds(_writeIntervalSeconds);

            if (EnsureConnectionOpen())
            {
                foreach (var dev in _devices)
                {
                    if (ct.IsCancellationRequested)
                        return;
                    ReadDevice(dev, shouldWrite);
                }
            }
            else
            {
                // Povezava ni na voljo: zabeleži izpad pri vseh napravah, da v CSV ne nastane luknja brez razlage.
                var result = DeviceReadResult.Failed(DateTime.Now, string.Format(Strings.Msg_PovezavaNiNaVoljo, ConnectionDescription), 0);
                foreach (var dev in _devices)
                    Dispatch(dev, result, shouldWrite);
            }

            if (shouldWrite)
                _lastWriteTime = DateTime.Now;

            if (once)
                return;

            if (WaitForNextCycle(ct, cycle))
                return;
        }
    }

    /// <summary>
    /// Čaka do naslednjega cikla glede na SampleIntervalSeconds, pri čemer upošteva tudi
    /// spremembe intervala med čakanjem. Vrne true, če je bil zahtevan preklic.
    /// </summary>
    private bool WaitForNextCycle(CancellationToken ct, Stopwatch cycle)
    {
        while (true)
        {
            int wait = Math.Max(0, _sampleIntervalSeconds * 1000 - (int)cycle.ElapsedMilliseconds);
            if (wait == 0)
                return false;

            int signaled = WaitHandle.WaitAny(new[] { ct.WaitHandle, _intervalChanged.WaitHandle }, wait);
            if (signaled == 0)
                return true;               // preklic
            if (signaled == WaitHandle.WaitTimeout)
                return false;              // čakanje se je izteklo, na vrsti je naslednji cikel

            _intervalChanged.Reset();      // interval se je spremenil med čakanjem — preračunaj preostanek
        }
    }

    private void ReadDevice(DeviceEntry dev, bool shouldWrite)
    {
        var profile = _loaded.Profiles[dev.Profile];
        DeviceReadResult result;
        try
        {
            result = DeviceReader.Read(_master!, dev.SlaveId, profile);
        }
        catch (Exception ex)
        {
            // Varovalka: dolgotrajen zapisovalnik ne sme pasti zaradi nepričakovane izjeme knjižnice.
            result = DeviceReadResult.Failed(DateTime.Now, string.Format(Strings.Err_NepricakovanaNapaka, ex.GetType().Name, ex.Message), 0);
        }

        if (!result.Success && !ConnectionStillPresent())
        {
            CloseConnection();
            Message?.Invoke(string.Format(Strings.Msg_PovezavaJeIzginila, ConnectionDescription));
        }

        Dispatch(dev, result, shouldWrite);
    }

    private string ConnectionDescription => IsTcp
        ? $"{_loaded.Config.Tcp.Host}:{_loaded.Config.Tcp.Port}"
        : _loaded.Config.Serial.Port;

    private void Dispatch(DeviceEntry dev, DeviceReadResult result, bool shouldWrite)
    {
        DeviceRead?.Invoke(dev, result);
        if (!shouldWrite)
            return;

        foreach (var sink in _sinks)
        {
            try
            {
                sink.Write(dev, _loaded.Profiles[dev.Profile], result);
            }
            catch (Exception ex)
            {
                // Npr. CSV odprt v Excelu (zaklenjen) — meritev v tem sinku izgubimo, zanka pa teče naprej.
                Message?.Invoke(string.Format(Strings.Msg_NapakaPriZapisu, sink.GetType().Name, ex.Message));
            }
        }
    }

    private bool EnsureConnectionOpen() => IsTcp ? EnsureTcpOpen() : EnsureSerialOpen();

    private bool EnsureSerialOpen()
    {
        if (_port is { IsOpen: true })
            return true;

        CloseConnection();
        var s = _loaded.Config.Serial;
        if (!SerialPort.GetPortNames().Contains(s.Port, StringComparer.OrdinalIgnoreCase))
            return false;

        try
        {
            _port = DeviceReader.CreatePort(s);
            _port.Open();
            var streamResource = new LoggingStreamResource(new SerialPortAdapter(_port));
            streamResource.TrafficCaptured += Traffic.OnTraffic;
            var factory = new ModbusFactory();
            _master = factory.CreateRtuMaster(streamResource);
            _master.Transport.ReadTimeout = s.TimeoutMs;
            _master.Transport.WriteTimeout = s.TimeoutMs;
            _master.Transport.Retries = s.Retries;
            Message?.Invoke(string.Format(Strings.Msg_PortOdprt, s.Port, s.Baud, s.Parity.ToUpperInvariant()[0], s.StopBits));
            return true;
        }
        catch (Exception ex)
        {
            Message?.Invoke(string.Format(Strings.Msg_PortaNiMogoceOdpreti, s.Port, ex.Message));
            CloseConnection();
            return false;
        }
    }

    private bool EnsureTcpOpen()
    {
        if (_tcpClient is { Connected: true })
            return true;

        CloseConnection();
        var s = _loaded.Config.Tcp;

        try
        {
            _tcpClient = DeviceReader.CreateTcpClient(s);
            var streamResource = new LoggingStreamResource(new TcpClientAdapter(_tcpClient));
            streamResource.TrafficCaptured += Traffic.OnTraffic;
            var factory = new ModbusFactory();
            var transport = factory.CreateIpTransport(streamResource);
            _master = new ModbusIpMaster(transport);
            _master.Transport.ReadTimeout = s.TimeoutMs;
            _master.Transport.WriteTimeout = s.TimeoutMs;
            _master.Transport.Retries = s.Retries;
            Message?.Invoke(string.Format(Strings.Msg_PovezavaVzpostavljena, s.Host, s.Port));
            return true;
        }
        catch (Exception ex)
        {
            Message?.Invoke(string.Format(Strings.Msg_PovezaveNiMogoceVzpostaviti, s.Host, s.Port, ex.Message));
            CloseConnection();
            return false;
        }
    }

    private bool ConnectionStillPresent() => IsTcp ? TcpStillConnected() : SerialStillPresent();

    /// <summary>
    /// SerialPort.IsOpen ostane true tudi po fizičnem izklopu USB pretvornika — je le zastavica,
    /// ne preverja strojne opreme. Zato tu dodatno poskusimo dostopati do dejanskega stanja
    /// vodila (BytesToRead), kar na "mrtvem" portu vrže izjemo in zanesljivo razkrije izpad.
    /// </summary>
    private bool SerialStillPresent()
    {
        if (_port is not { IsOpen: true })
            return false;
        if (!SerialPort.GetPortNames().Contains(_loaded.Config.Serial.Port, StringComparer.OrdinalIgnoreCase))
            return false;
        try
        {
            _ = _port.BytesToRead;
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// TcpClient.Connected odraža le izid zadnje operacije, ne dejanskega stanja povezave.
    /// Poll+Available trik zanesljivo zazna, da je druga stran zaprla povezavo (polovično zaprt socket).
    /// </summary>
    private bool TcpStillConnected()
    {
        if (_tcpClient is not { Connected: true })
            return false;
        try
        {
            var socket = _tcpClient.Client;
            bool readable = socket.Poll(0, SelectMode.SelectRead);
            return !(readable && socket.Available == 0);
        }
        catch
        {
            return false;
        }
    }

    private void CloseConnection()
    {
        try { _master?.Dispose(); } catch { /* povezava je morda že mrtva */ }
        try { _port?.Dispose(); } catch { /* povezava je morda že mrtva */ }
        try { _tcpClient?.Dispose(); } catch { /* povezava je morda že mrtva */ }
        _master = null;
        _port = null;
        _tcpClient = null;
    }

    public void Dispose()
    {
        CloseConnection();
        _intervalChanged.Dispose();
        foreach (var sink in _sinks)
            (sink as IDisposable)?.Dispose();
    }
}
