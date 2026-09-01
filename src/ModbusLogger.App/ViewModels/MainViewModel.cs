using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using ModbusLogger.App;
using ModbusLogger.Core;

namespace ModbusLogger.App.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private readonly Dispatcher _dispatcher;

    private LoadedConfig? _loaded;
    private PollService? _service;
    private CancellationTokenSource? _cts;
    private Task? _runTask;

    private string _configPath = "";
    private bool _isTcpConnection;
    private string? _selectedPort;
    private int _baud = 19200;
    private string _parity = "even";
    private int _stopBits = 1;
    private string _tcpHost = "192.168.1.100";
    private int _tcpPort = 502;
    private int _timeoutMs = 1000;
    private int _retries = 2;
    private int _sampleIntervalSeconds = 5;
    private int _writeIntervalSeconds = 60;
    private bool _logToCsv = true;
    private string _csvFolder = "logs";
    private string _csvDelimiter = ";";
    private string _csvDecimalSeparator = ",";
    private bool _logToMySql;
    private string _mySqlHost = "localhost";
    private int _mySqlPort = 3306;
    private string _mySqlDatabase = "";
    private string _mySqlUser = "";
    private string _mySqlPassword = "";
    private string _mySqlTable = "";
    private bool _isRunning;
    private bool _isBusy;
    private string _statusText = "";
    private DeviceVm? _selectedDevice;
    private TrafficWindow? _trafficWindow;
    private LoggingSettingsWindow? _loggingSettingsWindow;

    public MainViewModel(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        StartStopCommand = new RelayCommand(StartStop, () => !_isBusy && (_isRunning || _loaded is { IsValid: true }));
        ReloadCommand = new RelayCommand(OpenLoadConfigDialog, () => !_isRunning && !_isBusy);
        RefreshPortsCommand = new RelayCommand(RefreshPorts, () => !_isRunning);
        SaveConfigCommand = new RelayCommand(OpenSaveConfigDialog, () => !_isRunning && !_isBusy && _loaded is not null);
        OpenLogsCommand = new RelayCommand(() => OpenInExplorer(ResolveLogsFolder()));
        AddDeviceCommand = new RelayCommand(AddDevice, () => !_isRunning && !_isBusy && _loaded is { IsValid: true });
        EditDeviceCommand = new RelayCommand(EditDevice, () => !_isRunning && !_isBusy && SelectedDevice is not null);
        RemoveDeviceCommand = new RelayCommand(RemoveDevice, () => !_isRunning && !_isBusy && SelectedDevice is not null);
        ShowTrafficCommand = new RelayCommand(ShowTraffic);
        ShowLoggingSettingsCommand = new RelayCommand(ShowLoggingSettings);
        BrowseCsvFolderCommand = new RelayCommand(BrowseCsvFolder);
        FetchMySqlColumnsCommand = new RelayCommand(FetchMySqlColumns);

        RefreshPorts();
        LoadConfig();
    }

    public ObservableCollection<DeviceVm> Devices { get; } = new();
    public ObservableCollection<string> Ports { get; } = new();
    public ObservableCollection<string> LogLines { get; } = new();
    public ObservableCollection<TrafficExchangeVm> TrafficLog { get; } = new();
    public string[] FramingOptions { get; } = { "8E1", "8O1", "8N1", "8N2" };
    public int[] BaudRateOptions { get; } = { 1200, 2400, 4800, 9600, 19200, 38400, 57600, 115200 };

    public RelayCommand StartStopCommand { get; }
    public RelayCommand ReloadCommand { get; }
    public RelayCommand RefreshPortsCommand { get; }
    public RelayCommand SaveConfigCommand { get; }
    public RelayCommand OpenLogsCommand { get; }
    public RelayCommand AddDeviceCommand { get; }
    public RelayCommand EditDeviceCommand { get; }
    public RelayCommand RemoveDeviceCommand { get; }
    public RelayCommand ShowTrafficCommand { get; }
    public RelayCommand ShowLoggingSettingsCommand { get; }
    public RelayCommand BrowseCsvFolderCommand { get; }
    public RelayCommand FetchMySqlColumnsCommand { get; }

    /// <summary>Ločila stolpcev, ki jih ponuja izbirnik v nastavitvah beleženja.</summary>
    public DelimiterOption[] DelimiterOptions { get; } = new[]
    {
        new DelimiterOption("; (podpičje)", ";"),
        new DelimiterOption(", (vejica)", ","),
        new DelimiterOption("Tabulator", "\t"),
    };

    public string[] DecimalSeparatorOptions { get; } = { ",", "." };

    /// <summary>Možnosti v izbirniku načina povezave nad nastavitvami "Povezava".</summary>
    public ConnectionModeOption[] ConnectionModeOptions { get; } = new[]
    {
        new ConnectionModeOption("Serijska (RS-485)", false),
        new ConnectionModeOption("Modbus TCP", true),
    };

    public string ConfigPath { get => _configPath; private set => Set(ref _configPath, value); }

    /// <summary>true = Modbus TCP (oddaljen IP naslov), false = serijska povezava (RS-485/RS-232 prek COM porta).</summary>
    public bool IsTcpConnection { get => _isTcpConnection; set => Set(ref _isTcpConnection, value); }

    public string TcpHost { get => _tcpHost; set => Set(ref _tcpHost, value); }
    public int TcpPort { get => _tcpPort; set => Set(ref _tcpPort, value); }

    public string? SelectedPort { get => _selectedPort; set => Set(ref _selectedPort, value); }
    public int Baud { get => _baud; set => Set(ref _baud, value); }
    /// <summary>Format okvirja: 8E1/8O1/8N1/8N2 (Modbus RTU je vedno 8 podatkovnih bitov).</summary>
    public string Framing
    {
        get => FormatFraming(_parity, _stopBits);
        set
        {
            (_parity, _stopBits) = ParseFraming(value);
            Raise();
        }
    }

    private static string FormatFraming(string parity, int stopBits) =>
        $"8{parity.ToLowerInvariant() switch { "even" => 'E', "odd" => 'O', _ => 'N' }}{stopBits}";

    private static (string Parity, int StopBits) ParseFraming(string code) => code switch
    {
        "8O1" => ("odd", 1),
        "8N1" => ("none", 1),
        "8N2" => ("none", 2),
        _ => ("even", 1),   // 8E1 in privzeto
    };
    public int TimeoutMs { get => _timeoutMs; set => Set(ref _timeoutMs, value); }
    public int Retries { get => _retries; set => Set(ref _retries, value); }

    /// <summary>Čas vzorčenja (komunikacije z napravami); ostane urejljiv tudi med tekom.</summary>
    public int SampleIntervalSeconds
    {
        get => _sampleIntervalSeconds;
        set
        {
            int clamped = Math.Max(1, value);
            if (!Set(ref _sampleIntervalSeconds, clamped))
                return;
            if (_service is not null)
            {
                _service.SampleIntervalSeconds = clamped;
                Log($"Čas vzorčenja spremenjen na {clamped} s (velja takoj).");
            }
        }
    }

    /// <summary>Interval zapisa v CSV/MySQL; ni urejljiv med tekom (zaklenjen v "Nastavitve beleženja").</summary>
    public int WriteIntervalSeconds { get => _writeIntervalSeconds; set => Set(ref _writeIntervalSeconds, value); }

    public bool LogToCsv { get => _logToCsv; set => Set(ref _logToCsv, value); }

    public string CsvFolder
    {
        get => _csvFolder;
        set { if (Set(ref _csvFolder, value)) Raise(nameof(ResolvedCsvFolderPath)); }
    }

    public string CsvDelimiter { get => _csvDelimiter; set => Set(ref _csvDelimiter, value); }
    public string CsvDecimalSeparator { get => _csvDecimalSeparator; set => Set(ref _csvDecimalSeparator, value); }

    /// <summary>Polna, razrešena pot do mape za CSV — za prikaz v nastavitvah beleženja.</summary>
    public string ResolvedCsvFolderPath => ResolveLogsFolder() ?? "";

    public bool LogToMySql { get => _logToMySql; set => Set(ref _logToMySql, value); }
    public string MySqlHost { get => _mySqlHost; set => Set(ref _mySqlHost, value); }
    public int MySqlPort { get => _mySqlPort; set => Set(ref _mySqlPort, value); }
    public string MySqlDatabase { get => _mySqlDatabase; set => Set(ref _mySqlDatabase, value); }
    public string MySqlUser { get => _mySqlUser; set => Set(ref _mySqlUser, value); }
    public string MySqlPassword { get => _mySqlPassword; set => Set(ref _mySqlPassword, value); }
    public string MySqlTable { get => _mySqlTable; set => Set(ref _mySqlTable, value); }

    /// <summary>Ena vrstica na stolpec tabele, prebran s "Preberi stolpce" — uporabnik izbere vir podatka zanj.</summary>
    public ObservableCollection<MySqlColumnMappingVm> MySqlColumnMappings { get; } = new();

    public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }
    public DeviceVm? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            if (Set(ref _selectedDevice, value))
            {
                EditDeviceCommand.RaiseCanExecuteChanged();
                RemoveDeviceCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (Set(ref _isRunning, value))
            {
                Raise(nameof(IsStopped));
                Raise(nameof(StartStopText));
                RefreshCommands();
            }
        }
    }

    public bool IsStopped => !_isRunning;
    public string StartStopText => _isRunning ? "USTAVI" : "ZAŽENI";

    /// <summary>Znova naloži trenutno aktivno konfiguracijsko datoteko (ali privzeto, če še ni bila izbrana).</summary>
    private void LoadConfig()
    {
        string? path = AppState.LoadLastConfigPath();
        if (path is null)
        {
            StatusText = "Ni naložene konfiguracije — uporabi 'Naloži profil'.";
            Log(StatusText);
            RefreshCommands();
            return;
        }
        LoadConfigFrom(path);
    }

    private void LoadConfigFrom(string? path)
    {
        if (path is null)
        {
            StatusText = "config\\devices.json ni najden — postavi ga poleg aplikacije.";
            Log(StatusText);
            return;
        }

        _loaded = ConfigLoader.Load(path);
        ConfigPath = _loaded.DevicesPath;
        AppState.SaveLastConfigPath(path);

        foreach (string w in _loaded.Warnings)
            Log($"OPOZORILO: {w}");
        if (!_loaded.IsValid)
        {
            foreach (string e in _loaded.Errors)
                Log($"NAPAKA: {e}");
            StatusText = $"Konfiguracija ima {_loaded.Errors.Count} napak — glej dnevnik.";
            Devices.Clear();
            RefreshCommands();
            return;
        }

        var cfg = _loaded.Config;
        IsTcpConnection = string.Equals(cfg.ConnectionType, "tcp", StringComparison.OrdinalIgnoreCase);
        Baud = cfg.Serial.Baud;
        Framing = FormatFraming(cfg.Serial.Parity, cfg.Serial.StopBits);
        TcpHost = cfg.Tcp.Host;
        TcpPort = cfg.Tcp.Port;
        TimeoutMs = IsTcpConnection ? cfg.Tcp.TimeoutMs : cfg.Serial.TimeoutMs;
        Retries = IsTcpConnection ? cfg.Tcp.Retries : cfg.Serial.Retries;
        SampleIntervalSeconds = cfg.SampleIntervalSeconds;
        WriteIntervalSeconds = cfg.WriteIntervalSeconds;
        LogToCsv = cfg.Logging.Enabled;
        CsvFolder = cfg.Logging.Folder;
        CsvDelimiter = cfg.Logging.Delimiter;
        CsvDecimalSeparator = cfg.Logging.DecimalSeparator;
        Raise(nameof(ResolvedCsvFolderPath));
        SelectedPort = Ports.FirstOrDefault(p => p.Equals(cfg.Serial.Port, StringComparison.OrdinalIgnoreCase))
                       ?? Ports.FirstOrDefault();

        LogToMySql = cfg.MySql.Enabled;
        MySqlHost = cfg.MySql.Host;
        MySqlPort = cfg.MySql.Port;
        MySqlDatabase = cfg.MySql.Database;
        MySqlUser = cfg.MySql.User;
        MySqlPassword = cfg.MySql.Password;
        MySqlTable = cfg.MySql.Table;

        Devices.Clear();
        foreach (var dev in cfg.Devices.Where(d => d.Enabled))
            Devices.Add(new DeviceVm(dev, _loaded.Profiles[dev.Profile]));
        SelectedDevice = Devices.FirstOrDefault();

        MySqlColumnMappings.Clear();
        if (cfg.MySql.ColumnMapping.Count > 0)
        {
            var options = BuildMySqlFieldOptions();
            foreach (var (column, key) in cfg.MySql.ColumnMapping)
                MySqlColumnMappings.Add(new MySqlColumnMappingVm(column, options, key));
        }

        StatusText = $"Naloženo: {Devices.Count} naprav, {_loaded.Profiles.Count} profilov.";
        Log(StatusText);
        RefreshCommands();
    }

    private void StartStop()
    {
        if (IsRunning)
            Stop();
        else
            Start();
    }

    private void Start()
    {
        if (_loaded is not { IsValid: true })
            return;

        if (IsTcpConnection)
        {
            if (string.IsNullOrWhiteSpace(TcpHost))
            {
                StatusText = "Vpiši IP naslov ali ime gostitelja.";
                return;
            }
        }
        else if (SelectedPort is null)
        {
            StatusText = "Izberi COM port.";
            return;
        }

        // Nastavitve iz vmesnika veljajo za ta zagon; trajne spremembe sodijo v devices.json.
        var cfg = _loaded.Config;
        cfg.ConnectionType = IsTcpConnection ? "tcp" : "serial";
        if (IsTcpConnection)
        {
            cfg.Tcp.Host = TcpHost.Trim();
            cfg.Tcp.Port = TcpPort;
            cfg.Tcp.TimeoutMs = TimeoutMs;
            cfg.Tcp.Retries = Retries;
        }
        else
        {
            cfg.Serial.Port = SelectedPort!;
            cfg.Serial.Baud = Baud;
            cfg.Serial.Parity = _parity;
            cfg.Serial.StopBits = _stopBits;
            cfg.Serial.TimeoutMs = TimeoutMs;
            cfg.Serial.Retries = Retries;
        }
        cfg.SampleIntervalSeconds = Math.Max(1, SampleIntervalSeconds);
        cfg.WriteIntervalSeconds = Math.Max(1, WriteIntervalSeconds);
        cfg.Logging.Folder = CsvFolder;
        cfg.Logging.Delimiter = CsvDelimiter;
        cfg.Logging.DecimalSeparator = CsvDecimalSeparator;
        cfg.MySql = BuildMySqlSettings();

        var sinks = new List<ILogSink>();
        if (LogToCsv)
        {
            try
            {
                var csv = new CsvLogSink(cfg.Logging, Path.GetDirectoryName(_loaded.DevicesPath)!);
                sinks.Add(csv);
                Log($"CSV beleženje v: {csv.Folder}");
            }
            catch (Exception ex)
            {
                Log($"NAPAKA: mape za CSV ni mogoče ustvariti: {ex.Message}");
                return;
            }
        }
        else
        {
            Log("CSV beleženje je izklopljeno — samo prikaz.");
        }

        if (LogToMySql)
        {
            try
            {
                var mysql = new MySqlLogSink(cfg.MySql);
                mysql.Diagnostic += msg => _dispatcher.BeginInvoke(() => Log(msg));
                sinks.Add(mysql);
                Log($"MySQL beleženje v tabelo '{MySqlTable}' na {MySqlHost}:{MySqlPort}/{MySqlDatabase} " +
                    $"({cfg.MySql.ColumnMapping.Count} povezanih stolpcev).");
            }
            catch (Exception ex)
            {
                Log($"NAPAKA: MySQL povezave ni mogoče vzpostaviti: {ex.Message}");
                return;
            }
        }

        foreach (var dev in Devices)
            dev.ResetStatus();

        TrafficLog.Clear();
        _service = new PollService(_loaded, sinks);
        _service.Message += msg => _dispatcher.BeginInvoke(() => Log(msg));
        _service.DeviceRead += (entry, result) => _dispatcher.BeginInvoke(() =>
        {
            var dev = Devices.FirstOrDefault(d => ReferenceEquals(d.Entry, entry));
            dev?.Update(result);
        });
        _service.Traffic.ExchangeCompleted += exchange => _dispatcher.BeginInvoke(() =>
        {
            TrafficLog.Add(new TrafficExchangeVm(exchange));
            while (TrafficLog.Count > 500)
                TrafficLog.RemoveAt(0);
        });

        _cts = new CancellationTokenSource();
        var service = _service;
        var cts = _cts;
        _runTask = Task.Run(() => service.Run(cts.Token));

        IsRunning = true;
        StatusText = $"Beleženje teče (vzorčenje {cfg.SampleIntervalSeconds} s, zapis {cfg.WriteIntervalSeconds} s).";
        Log("Zagnano.");
    }

    private async void Stop()
    {
        if (_cts is null || _runTask is null)
            return;

        _isBusy = true;
        RefreshCommands();
        StatusText = "Ustavljam ...";
        _cts.Cancel();
        try { await _runTask; }
        catch (Exception ex) { Log($"Napaka ob ustavljanju: {ex.Message}"); }

        _service?.Dispose();
        _service = null;
        _cts.Dispose();
        _cts = null;
        _runTask = null;

        foreach (var dev in Devices)
            dev.SetStopped();

        _isBusy = false;
        IsRunning = false;
        StatusText = "Ustavljeno.";
        Log("Ustavljeno.");
    }

    /// <summary>Ob zapiranju okna: sinhrono ustavi zanko, da se port lepo zapre.</summary>
    public void Shutdown()
    {
        _cts?.Cancel();
        try { _runTask?.Wait(TimeSpan.FromSeconds(5)); } catch { /* zapiramo se */ }
        _service?.Dispose();
    }

    private void RefreshPorts()
    {
        string? current = SelectedPort;
        Ports.Clear();
        foreach (string p in SerialPort.GetPortNames().OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            Ports.Add(p);
        SelectedPort = Ports.FirstOrDefault(p => p == current) ?? Ports.FirstOrDefault();
    }

    /// <summary>Razreši trenutno (živo urejano) mapo za CSV v polno pot — ne čaka na "Shrani profil".</summary>
    private string? ResolveLogsFolder()
    {
        if (_loaded is null)
            return null;
        return Path.IsPathRooted(CsvFolder)
            ? CsvFolder
            : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(_loaded.DevicesPath)!, CsvFolder));
    }

    private static void OpenInExplorer(string? folder)
    {
        if (folder is not null && Directory.Exists(folder))
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
    }

    private void Log(string message)
    {
        LogLines.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
        while (LogLines.Count > 500)
            LogLines.RemoveAt(0);
    }

    /// <summary>Odpre dialog za izbiro ene od shranjenih konfiguracij in jo naloži kot aktivno.</summary>
    private void OpenLoadConfigDialog()
    {
        string dir = _loaded is not null ? Path.GetDirectoryName(_loaded.DevicesPath)! : ConfigLoader.ResolveConfigDir();
        string currentName = _loaded is not null ? Path.GetFileNameWithoutExtension(_loaded.DevicesPath) : "";
        var dialogVm = new ConfigPickerViewModel(dir, isSaveMode: false, currentName);
        var dialog = new ConfigPickerWindow(dialogVm) { Owner = Application.Current.MainWindow };
        bool? result = dialog.ShowDialog();

        if (result == true && dialogVm.ResultPath is not null)
        {
            Log($"Nalagam profil: {Path.GetFileNameWithoutExtension(dialogVm.ResultPath)}");
            LoadConfigFrom(dialogVm.ResultPath);
        }
    }

    /// <summary>
    /// Odpre dialog za vpis imena in trenutno konfiguracijo (naprave iz aktivne datoteke +
    /// trenutne nastavitve vmesnika) shrani pod tem imenom kot novo aktivno konfiguracijo.
    /// </summary>
    private void OpenSaveConfigDialog()
    {
        if (_loaded is null)
            return;

        string dir = Path.GetDirectoryName(_loaded.DevicesPath)!;
        string currentName = Path.GetFileNameWithoutExtension(_loaded.DevicesPath);
        var dialogVm = new ConfigPickerViewModel(dir, isSaveMode: true, currentName);
        var dialog = new ConfigPickerWindow(dialogVm) { Owner = Application.Current.MainWindow };
        bool? result = dialog.ShowDialog();

        if (result != true || dialogVm.ResultPath is null)
            return;

        try
        {
            // Naloži naprave iz trenutno aktivne datoteke, čez njih pa položi trenutne nastavitve vmesnika.
            var loaded = ConfigLoader.Load(_loaded.DevicesPath);
            var cfg = loaded.Config;
            cfg.ConnectionType = IsTcpConnection ? "tcp" : "serial";
            if (IsTcpConnection)
            {
                cfg.Tcp.Host = TcpHost.Trim();
                cfg.Tcp.Port = TcpPort;
                cfg.Tcp.TimeoutMs = TimeoutMs;
                cfg.Tcp.Retries = Retries;
            }
            else
            {
                if (SelectedPort is not null)
                    cfg.Serial.Port = SelectedPort;
                cfg.Serial.Baud = Baud;
                cfg.Serial.Parity = _parity;
                cfg.Serial.StopBits = _stopBits;
                cfg.Serial.TimeoutMs = TimeoutMs;
                cfg.Serial.Retries = Retries;
            }
            cfg.SampleIntervalSeconds = Math.Max(1, SampleIntervalSeconds);
            cfg.WriteIntervalSeconds = Math.Max(1, WriteIntervalSeconds);
            cfg.Logging.Enabled = LogToCsv;
            cfg.Logging.Folder = CsvFolder;
            cfg.Logging.Delimiter = CsvDelimiter;
            cfg.Logging.DecimalSeparator = CsvDecimalSeparator;
            cfg.MySql = BuildMySqlSettings();

            ConfigLoader.Save(cfg, dialogVm.ResultPath);
            Log($"Profil shranjen kot: {Path.GetFileNameWithoutExtension(dialogVm.ResultPath)}");
        }
        catch (Exception ex)
        {
            Log($"NAPAKA pri shranjevanju profila: {ex.Message}");
            return;
        }

        LoadConfigFrom(dialogVm.ResultPath);
    }

    /// <summary>Odpre (ali aktivira že odprto) nemodalno okno s HEX pregledom Modbus prometka.</summary>
    private void ShowTraffic()
    {
        if (_trafficWindow is null)
        {
            _trafficWindow = new TrafficWindow(this) { Owner = Application.Current.MainWindow };
            _trafficWindow.Closed += (_, _) => _trafficWindow = null;
            _trafficWindow.Show();
        }
        else
        {
            _trafficWindow.Activate();
        }
    }

    /// <summary>Odpre (ali aktivira že odprto) okno z nastavitvami beleženja (interval, CSV format, mapa).</summary>
    private void ShowLoggingSettings()
    {
        if (_loggingSettingsWindow is null)
        {
            _loggingSettingsWindow = new LoggingSettingsWindow(this) { Owner = Application.Current.MainWindow };
            _loggingSettingsWindow.Closed += (_, _) => _loggingSettingsWindow = null;
            _loggingSettingsWindow.Show();
        }
        else
        {
            _loggingSettingsWindow.Activate();
        }
    }

    private void BrowseCsvFolder()
    {
        var dialog = new OpenFolderDialog { Title = "Izberi mapo za CSV datoteke" };
        string current = CsvFolder;
        if (_loaded is not null)
        {
            string full = Path.IsPathRooted(current)
                ? current
                : Path.Combine(Path.GetDirectoryName(_loaded.DevicesPath)!, current);
            if (Directory.Exists(full))
                dialog.InitialDirectory = full;
        }
        if (dialog.ShowDialog() == true)
            CsvFolder = dialog.FolderName;
    }

    /// <summary>Sestavi MySqlSettings iz trenutnih vrednosti v vmesniku (povezava + mapiranje stolpcev).</summary>
    private MySqlSettings BuildMySqlSettings() => new()
    {
        Enabled = LogToMySql,
        Host = MySqlHost,
        Port = MySqlPort,
        Database = MySqlDatabase,
        User = MySqlUser,
        Password = MySqlPassword,
        Table = MySqlTable,
        ColumnMapping = MySqlColumnMappings
            .Where(m => m.EffectiveKey is not null)
            .ToDictionary(m => m.Column, m => m.EffectiveKey!),
    };

    /// <summary>Fiksni viri podatka (čas, naprava ...) + en vnos na vsako distinktno ime registra iz naloženih profilov.</summary>
    private MySqlFieldOption[] BuildMySqlFieldOptions()
    {
        var options = new List<MySqlFieldOption>
        {
            new("(ne uporabi)", null),
            new("Čas meritve", MySqlLogSink.FieldTimestamp),
            new("Naprava (ime)", MySqlLogSink.FieldDevice),
            new("Slave ID", MySqlLogSink.FieldSlaveId),
            new("Status / napaka", MySqlLogSink.FieldStatus),
            new("Čas odziva [ms]", MySqlLogSink.FieldResponseMs),
            new("Napaka komunikacije (1 = napaka, 0 = OK)", MySqlLogSink.FieldErrorFlag),
            new("Konstantna vrednost ...", MySqlLogSink.ConstantFieldPrefix),
        };

        if (_loaded is not null)
        {
            var registerNames = _loaded.Profiles.Values
                .SelectMany(p => p.Registers)
                .Select(r => r.Name)
                .Distinct()
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase);
            foreach (string name in registerNames)
                options.Add(new MySqlFieldOption($"Register: {name}", MySqlLogSink.RegisterFieldPrefix + name));
        }

        return options.ToArray();
    }

    private void FetchMySqlColumns()
    {
        var settings = new MySqlSettings
        {
            Host = MySqlHost, Port = MySqlPort, Database = MySqlDatabase,
            User = MySqlUser, Password = MySqlPassword, Table = MySqlTable,
        };
        try
        {
            var columns = MySqlLogSink.FetchColumns(settings);
            if (columns.Count == 0)
            {
                Log($"Tabela '{MySqlTable}' v bazi '{MySqlDatabase}' ne obstaja ali nima stolpcev.");
                return;
            }

            var options = BuildMySqlFieldOptions();
            var existing = MySqlColumnMappings.ToDictionary(m => m.Column, m => m.EffectiveKey);
            MySqlColumnMappings.Clear();
            foreach (string col in columns)
                MySqlColumnMappings.Add(new MySqlColumnMappingVm(col, options, existing.GetValueOrDefault(col)));
            Log($"Prebranih {columns.Count} stolpcev iz tabele '{MySqlTable}'. Poveži jih s podatki spodaj.");
        }
        catch (Exception ex)
        {
            Log($"NAPAKA pri branju stolpcev MySQL tabele: {ex.Message}");
        }
    }

    private void AddDevice()
    {
        if (_loaded is null)
            return;

        var dialogVm = new AddDeviceViewModel(_loaded.DevicesPath);
        var dialog = new AddDeviceWindow(dialogVm) { Owner = Application.Current.MainWindow };
        bool? result = dialog.ShowDialog();

        if (result == true)
        {
            Log($"Naprava '{dialogVm.Label}' (slave {dialogVm.SlaveId}) je bila dodana v devices.json.");
            LoadConfig();
        }
    }

    private void EditDevice()
    {
        if (_loaded is null || SelectedDevice is null)
            return;

        var entry = SelectedDevice.Entry;
        if (!_loaded.Profiles.TryGetValue(entry.Profile, out var profile))
        {
            Log($"NAPAKA: profil '{entry.Profile}' za napravo '{entry.Label}' ni naložen — ni ga mogoče urediti.");
            return;
        }

        var dialogVm = new AddDeviceViewModel(_loaded.DevicesPath, entry, profile);
        var dialog = new AddDeviceWindow(dialogVm) { Owner = Application.Current.MainWindow };
        bool? result = dialog.ShowDialog();

        if (result == true)
        {
            Log($"Naprava '{dialogVm.Label}' (slave {dialogVm.SlaveId}) je bila posodobljena.");
            LoadConfig();
        }
    }

    private void RemoveDevice()
    {
        if (_loaded is null || SelectedDevice is null)
            return;

        var entry = SelectedDevice.Entry;
        var choice = MessageBox.Show(
            $"Odstranim napravo '{entry.Label}' (slave {entry.SlaveId}) iz devices.json?\n\nProfil '{entry.Profile}' ostane na disku (lahko ga uporablja druga naprava).",
            "Odstrani napravo", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (choice != MessageBoxResult.Yes)
            return;

        try
        {
            var loaded = ConfigLoader.Load(_loaded.DevicesPath);
            int removed = loaded.Config.Devices.RemoveAll(d =>
                d.SlaveId == entry.SlaveId &&
                string.Equals(d.Profile, entry.Profile, StringComparison.OrdinalIgnoreCase) &&
                d.Label == entry.Label);
            ConfigLoader.Save(loaded.Config, _loaded.DevicesPath);

            Log(removed > 0
                ? $"Naprava '{entry.Label}' (slave {entry.SlaveId}) je bila odstranjena iz devices.json."
                : $"Naprave '{entry.Label}' ni bilo mogoče najti v devices.json (že odstranjena?).");
        }
        catch (Exception ex)
        {
            Log($"NAPAKA pri odstranjevanju naprave: {ex.Message}");
            return;
        }

        LoadConfig();
    }

    private void RefreshCommands()
    {
        StartStopCommand.RaiseCanExecuteChanged();
        ReloadCommand.RaiseCanExecuteChanged();
        RefreshPortsCommand.RaiseCanExecuteChanged();
        AddDeviceCommand.RaiseCanExecuteChanged();
        EditDeviceCommand.RaiseCanExecuteChanged();
        RemoveDeviceCommand.RaiseCanExecuteChanged();
        SaveConfigCommand.RaiseCanExecuteChanged();
    }
}

/// <summary>Ena možnost v izbirniku ločila stolpcev CSV (prikazno ime + dejanski znak).</summary>
public sealed record DelimiterOption(string Label, string Value)
{
    public override string ToString() => Label;
}

/// <summary>Ena možnost v izbirniku načina povezave (Modbus RTU prek COM porta ali Modbus TCP).</summary>
public sealed record ConnectionModeOption(string Label, bool IsTcp)
{
    public override string ToString() => Label;
}

/// <summary>Ena možnost v izbirniku vira podatka za stolpec MySQL tabele. Key=null pomeni "ne uporabi".</summary>
public sealed record MySqlFieldOption(string Label, string? Key)
{
    public override string ToString() => Label;
}

/// <summary>Eno mapiranje: stolpec obstoječe MySQL tabele -> izbran vir podatka.</summary>
public sealed class MySqlColumnMappingVm : ViewModelBase
{
    private MySqlFieldOption _selectedField;
    private string _constantValue = "";

    public MySqlColumnMappingVm(string column, MySqlFieldOption[] fieldOptions, string? currentKey)
    {
        Column = column;
        FieldOptions = fieldOptions;

        if (currentKey is not null && currentKey.StartsWith(MySqlLogSink.ConstantFieldPrefix, StringComparison.Ordinal))
        {
            _selectedField = fieldOptions.First(o => o.Key == MySqlLogSink.ConstantFieldPrefix);
            _constantValue = currentKey[MySqlLogSink.ConstantFieldPrefix.Length..];
        }
        else
        {
            _selectedField = fieldOptions.FirstOrDefault(o => o.Key == currentKey) ?? fieldOptions[0];
        }
    }

    public string Column { get; }
    public MySqlFieldOption[] FieldOptions { get; }

    public MySqlFieldOption SelectedField
    {
        get => _selectedField;
        set { if (Set(ref _selectedField, value)) Raise(nameof(IsConstant)); }
    }

    /// <summary>true, ko je izbrana možnost "Konstantna vrednost" — pokaže polje za vpis besedila.</summary>
    public bool IsConstant => SelectedField.Key == MySqlLogSink.ConstantFieldPrefix;

    /// <summary>Besedilo, ki se zapiše v vsako vrstico, kadar je IsConstant true.</summary>
    public string ConstantValue { get => _constantValue; set => Set(ref _constantValue, value); }

    /// <summary>Dejanski ključ za shranjevanje: pri konstanti vsebuje tudi vpisano besedilo.</summary>
    public string? EffectiveKey => IsConstant ? MySqlLogSink.ConstantFieldPrefix + ConstantValue : SelectedField.Key;
}
