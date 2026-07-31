using System.Diagnostics;
using System.IO.Ports;
using NModbus;
using NModbus.Serial;

namespace ModbusLogger.Core;

/// <summary>
/// Ciklično bere vse omogočene naprave in meritve pošilja v log sinke.
/// Preživi izpad USB-RS485 pretvornika: port zapre in ga poskuša znova odpreti,
/// vmes pa v sinke zapisuje vrstice z napako, da izpad ostane viden v podatkih.
/// </summary>
public sealed class PollService : IDisposable
{
    private readonly LoadedConfig _loaded;
    private readonly IReadOnlyList<ILogSink> _sinks;
    private readonly List<DeviceEntry> _devices;
    private readonly ManualResetEventSlim _intervalChanged = new(false);

    private SerialPort? _port;
    private IModbusSerialMaster? _master;
    private volatile int _intervalSeconds;

    /// <summary>
    /// Interval vzorčenja v sekundah. Sprememba med tekom velja takoj: če aplikacija
    /// trenutno čaka na naslednji cikel, se čakanje glede na novo vrednost prilagodi
    /// (podaljša ali prekine), ne šele po naslednjem ciklu.
    /// </summary>
    public int IntervalSeconds
    {
        get => _intervalSeconds;
        set
        {
            _intervalSeconds = Math.Max(1, value);
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
        _intervalSeconds = Math.Max(1, loaded.Config.PollIntervalSeconds);
    }

    /// <summary>Blokirajoča zanka; kliči s CancellationTokenom za ustavitev. once=true naredi en cikel.</summary>
    public void Run(CancellationToken ct, bool once = false)
    {
        while (!ct.IsCancellationRequested)
        {
            var cycle = Stopwatch.StartNew();

            if (EnsurePortOpen())
            {
                foreach (var dev in _devices)
                {
                    if (ct.IsCancellationRequested)
                        return;
                    ReadDevice(dev);
                }
            }
            else
            {
                // Port ni na voljo: zabeleži izpad pri vseh napravah, da v CSV ne nastane luknja brez razlage.
                var result = DeviceReadResult.Failed(DateTime.Now, $"port {_loaded.Config.Serial.Port} ni na voljo", 0);
                foreach (var dev in _devices)
                    Dispatch(dev, result);
            }

            if (once)
                return;

            if (WaitForNextCycle(ct, cycle))
                return;
        }
    }

    /// <summary>
    /// Čaka do naslednjega cikla glede na IntervalSeconds, pri čemer upošteva tudi
    /// spremembe intervala med čakanjem. Vrne true, če je bil zahtevan preklic.
    /// </summary>
    private bool WaitForNextCycle(CancellationToken ct, Stopwatch cycle)
    {
        while (true)
        {
            int wait = Math.Max(0, _intervalSeconds * 1000 - (int)cycle.ElapsedMilliseconds);
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

    private void ReadDevice(DeviceEntry dev)
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
            result = DeviceReadResult.Failed(DateTime.Now, $"nepričakovana napaka: {ex.GetType().Name}: {ex.Message}", 0);
        }

        if (!result.Success && !PortStillPresent())
        {
            ClosePort();
            Message?.Invoke($"Port {_loaded.Config.Serial.Port} je izginil (izvlečen USB pretvornik?) — poskušal ga bom znova odpreti.");
        }

        Dispatch(dev, result);
    }

    private void Dispatch(DeviceEntry dev, DeviceReadResult result)
    {
        DeviceRead?.Invoke(dev, result);
        foreach (var sink in _sinks)
        {
            try
            {
                sink.Write(dev, _loaded.Profiles[dev.Profile], result);
            }
            catch (Exception ex)
            {
                // Npr. CSV odprt v Excelu (zaklenjen) — meritev v tem sinku izgubimo, zanka pa teče naprej.
                Message?.Invoke($"Napaka pri zapisu ({sink.GetType().Name}): {ex.Message}");
            }
        }
    }

    private bool EnsurePortOpen()
    {
        if (_port is { IsOpen: true })
            return true;

        ClosePort();
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
            Message?.Invoke($"Port {s.Port} odprt ({s.Baud} baud, 8{s.Parity.ToUpperInvariant()[0]}{s.StopBits}).");
            return true;
        }
        catch (Exception ex)
        {
            Message?.Invoke($"Porta {s.Port} ni mogoče odpreti: {ex.Message}");
            ClosePort();
            return false;
        }
    }

    private bool PortStillPresent() =>
        SerialPort.GetPortNames().Contains(_loaded.Config.Serial.Port, StringComparer.OrdinalIgnoreCase)
        && _port is { IsOpen: true };

    private void ClosePort()
    {
        try { _master?.Dispose(); } catch { /* port je morda že mrtev */ }
        try { _port?.Dispose(); } catch { /* port je morda že mrtev */ }
        _master = null;
        _port = null;
    }

    public void Dispose()
    {
        ClosePort();
        _intervalChanged.Dispose();
    }
}
