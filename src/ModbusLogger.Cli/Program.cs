using System.Globalization;
using System.IO.Ports;
using ModbusLogger.Core;

namespace ModbusLogger.Cli;

/// <summary>
/// Faza 3: zapisovalnik — PollService ciklično bere naprave iz konfiguracije
/// in meritve zapisuje v CSV (ena datoteka na napravo na dan).
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        var opts = Options.Parse(args);
        if (opts is null)
        {
            Options.PrintUsage();
            return 1;
        }

        var available = SerialPort.GetPortNames();
        Console.WriteLine($"Razpoložljivi COM porti: {(available.Length == 0 ? "(nobenega)" : string.Join(", ", available))}");
        if (opts.ListPortsOnly)
            return 0;

        string? configPath = ConfigLoader.FindDefaultConfig(opts.ConfigPath);
        if (configPath is null)
        {
            Console.Error.WriteLine("NAPAKA: ne najdem config\\devices.json — podaj pot z --config <pot>.");
            return 1;
        }

        var loaded = ConfigLoader.Load(configPath);
        Console.WriteLine($"Konfiguracija: {loaded.DevicesPath}");
        foreach (string w in loaded.Warnings)
            Console.WriteLine($"  OPOZORILO: {w}");
        if (!loaded.IsValid)
        {
            foreach (string e in loaded.Errors)
                Console.Error.WriteLine($"  NAPAKA: {e}");
            return 1;
        }

        var cfg = loaded.Config;
        if (opts.PortOverride is not null)
            cfg.Serial.Port = opts.PortOverride;
        if (opts.IntervalOverride is not null)
            cfg.PollIntervalSeconds = opts.IntervalOverride.Value;

        var devices = cfg.Devices.Where(d => d.Enabled).ToList();
        Console.WriteLine($"Naprave: {string.Join(", ", devices.Select(d => $"{d.Label} (slave {d.SlaveId}, {d.Profile})"))}");

        var sinks = new List<ILogSink>();
        if (!opts.NoLog)
        {
            var csv = new CsvLogSink(cfg.Logging, Path.GetDirectoryName(loaded.DevicesPath)!);
            sinks.Add(csv);
            Console.WriteLine($"CSV beleženje v: {csv.Folder}");
        }
        else
        {
            Console.WriteLine("CSV beleženje je izklopljeno (--no-log).");
        }
        Console.WriteLine($"Interval: {cfg.PollIntervalSeconds} s. Ustavi s Ctrl+C.");
        Console.WriteLine();

        using var service = new PollService(loaded, sinks);
        service.Message += msg => Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] * {msg}");
        service.DeviceRead += (dev, result) =>
        {
            if (opts.Once)
                PrintTable(dev, result);
            else
                PrintCompact(dev, result);
        };

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        service.Run(cts.Token, opts.Once);

        Console.WriteLine("Ustavljeno.");
        return 0;
    }

    private static void PrintCompact(DeviceEntry dev, DeviceReadResult result)
    {
        string status = result.Success ? $"OK ({result.ElapsedMs} ms)" : $"NAPAKA: {result.Error}";
        Console.WriteLine($"[{result.Timestamp:HH:mm:ss}] {dev.Label} (slave {dev.SlaveId}): {status}");
    }

    private static void PrintTable(DeviceEntry dev, DeviceReadResult result)
    {
        Console.WriteLine($"[{result.Timestamp:yyyy-MM-dd HH:mm:ss}] {dev.Label} (slave {dev.SlaveId})");
        if (!result.Success)
        {
            Console.Error.WriteLine($"  NAPAKA: {result.Error} ({result.ElapsedMs} ms)");
            Console.WriteLine();
            return;
        }

        Console.WriteLine($"  OK, odgovor v {result.ElapsedMs} ms");
        Console.WriteLine($"  {"Naslov",-8} {"Ime",-22} {"Raw",10}  Vrednost");
        foreach (var r in result.Readings)
        {
            string raw = string.Join(" ", r.RawWords.Select(w => w.ToString()));
            string value = r.Value.ToString(r.Value == Math.Floor(r.Value) ? "0" : "0.###", CultureInfo.InvariantCulture);
            Console.WriteLine($"  0x{r.Def.AddressValue:X4}   {r.Def.Name,-22} {raw,10}  {value} {r.Def.Unit}");
        }
        Console.WriteLine();
    }

    private sealed class Options
    {
        public string? ConfigPath;
        public string? PortOverride;
        public int? IntervalOverride;
        public bool Once;
        public bool NoLog;
        public bool ListPortsOnly;

        public static Options? Parse(string[] args)
        {
            var o = new Options();
            for (int i = 0; i < args.Length; i++)
            {
                string Next() => ++i < args.Length ? args[i] : throw new ArgumentException($"manjka vrednost za {args[i - 1]}");
                try
                {
                    switch (args[i].ToLowerInvariant())
                    {
                        case "--config": o.ConfigPath = Next(); break;
                        case "--port": o.PortOverride = Next(); break;
                        case "--interval": o.IntervalOverride = int.Parse(Next()); break;
                        case "--once": o.Once = true; break;
                        case "--no-log": o.NoLog = true; break;
                        case "--list-ports": o.ListPortsOnly = true; break;
                        case "--help" or "-h" or "/?": return null;
                        default:
                            Console.Error.WriteLine($"Neznan argument: {args[i]}");
                            return null;
                    }
                }
                catch (Exception ex) when (ex is FormatException or ArgumentException or OverflowException)
                {
                    Console.Error.WriteLine($"Napačen argument: {ex.Message}");
                    return null;
                }
            }
            return o;
        }

        public static void PrintUsage()
        {
            Console.WriteLine("""
                Modbus RTU zapisovalnik - faza 3 (CSV beleženje)

                Uporaba: ModbusLogger.Cli [možnosti]

                  --config POT       pot do devices.json (privzeto: samodejno išče config\devices.json)
                  --port COMx        povozi port iz konfiguracije
                  --interval S       povozi interval iz konfiguracije
                  --once             en cikel s podrobnim izpisom, potem končaj
                  --no-log           samo izpisuj, brez CSV zapisovanja
                  --list-ports       samo izpiši COM porte in končaj

                Naprave in register mape: devices.json in profiles\*.json.
                CSV: ena datoteka na napravo na dan, mapa iz logging.folder.
                """);
        }
    }
}
