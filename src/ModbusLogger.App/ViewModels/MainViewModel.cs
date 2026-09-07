using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Ports;
using System.Reflection;
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
    private LanguageOption _selectedLanguage;

    public MainViewModel(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _selectedLanguage = LanguageOptions.FirstOrDefault(o =>
            string.Equals(o.Code, CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, StringComparison.OrdinalIgnoreCase))
            ?? LanguageOptions[0];
        StartStopCommand = new RelayCommand(StartStop, () => !_isBusy && (_isRunning || _loaded is { IsValid: true }));
        ReloadCommand = new RelayCommand(OpenLoadConfigDialog, () => !_isRunning && !_isBusy);
        RefreshPortsCommand = new RelayCommand(RefreshPorts, () => !_isRunning);
        SaveConfigCommand = new RelayCommand(OpenSaveConfigDialog, () => !_isRunning && !_isBusy && _loaded is not null);
        OpenLogsCommand = new RelayCommand(() => OpenInExplorer(ResolveLogsFolder()));
        AddDeviceCommand = new RelayCommand(AddDevice, () => !_isRunning && !_isBusy && _loaded is { IsValid: true });
        EditDeviceCommand = new RelayCommand(EditDevice, () => !_isRunning && !_isBusy && SelectedDevice is not null);
        RemoveDeviceCommand = new RelayCommand(RemoveDevice, () => !_isRunning && !_isBusy && SelectedDevice is not null);
        MoveDeviceUpCommand = new RelayCommand(() => MoveSelectedDevice(-1),
            () => !_isRunning && !_isBusy && SelectedDevice is not null && Devices.IndexOf(SelectedDevice) > 0);
        MoveDeviceDownCommand = new RelayCommand(() => MoveSelectedDevice(1),
            () => !_isRunning && !_isBusy && SelectedDevice is not null && Devices.IndexOf(SelectedDevice) < Devices.Count - 1);
        ShowTrafficCommand = new RelayCommand(ShowTraffic);
        ShowLoggingSettingsCommand = new RelayCommand(ShowLoggingSettings);
        BrowseCsvFolderCommand = new RelayCommand(BrowseCsvFolder);
        FetchMySqlColumnsCommand = new RelayCommand(FetchMySqlColumns);
        ShowHelpCommand = new RelayCommand(ShowHelp);

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
    public RelayCommand MoveDeviceUpCommand { get; }
    public RelayCommand MoveDeviceDownCommand { get; }
    public RelayCommand ShowTrafficCommand { get; }
    public RelayCommand ShowLoggingSettingsCommand { get; }
    public RelayCommand BrowseCsvFolderCommand { get; }
    public RelayCommand FetchMySqlColumnsCommand { get; }
    public RelayCommand ShowHelpCommand { get; }

    /// <summary>Ločila stolpcev, ki jih ponuja izbirnik v nastavitvah beleženja.</summary>
    public DelimiterOption[] DelimiterOptions { get; } = new[]
    {
        new DelimiterOption(Strings.LoggingSettings_Delim_Podpicje, ";"),
        new DelimiterOption(Strings.LoggingSettings_Delim_Vejica, ","),
        new DelimiterOption(Strings.LoggingSettings_Delim_Tab, "\t"),
    };

    public string[] DecimalSeparatorOptions { get; } = { ",", "." };

    /// <summary>
    /// Jeziki vmesnika, na voljo v izbirniku v orodni vrstici. Prikazna oznaka je namerno vedno
    /// dvočrkovna koda (ne prevedeno ime jezika) — ostane enaka ne glede na trenutno izbrano kulturo.
    /// </summary>
    public LanguageOption[] LanguageOptions { get; } = new[]
    {
        new LanguageOption("SL", "sl"),
        new LanguageOption("EN", "en"),
        new LanguageOption("DE", "de"),
        new LanguageOption("IT", "it"),
        new LanguageOption("ES", "es"),
    };

    /// <summary>
    /// Izbran jezik vmesnika. Sprememba se shrani in aplikacija se takoj znova zažene, da se
    /// nova kultura uveljavi povsod (x:Static v XAML se razreši samo enkrat, ob nalaganju okna).
    /// </summary>
    public LanguageOption SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            if (!Set(ref _selectedLanguage, value))
                return;
            AppState.SaveLanguage(value.Code);
            RestartApplication();
        }
    }

    private static void RestartApplication()
    {
        string? exePath = Environment.ProcessPath;
        if (exePath is not null)
            Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true });
        Application.Current.Shutdown();
    }

    /// <summary>Odpre navodila za uporabo (HTML) v privzetem brskalniku, v trenutno izbranem jeziku vmesnika.</summary>
    private static void ShowHelp()
    {
        string lang = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant();
        string docsDir = Path.Combine(AppContext.BaseDirectory, "docs");
        string path = Path.Combine(docsDir, $"help-{lang}.html");
        if (!File.Exists(path))
            path = Path.Combine(docsDir, "help-sl.html");
        if (File.Exists(path))
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    /// <summary>Možnosti v izbirniku načina povezave nad nastavitvami "Povezava".</summary>
    public ConnectionModeOption[] ConnectionModeOptions { get; } = new[]
    {
        new ConnectionModeOption(Strings.Main_ConnMode_Serial, false),
        new ConnectionModeOption(Strings.Main_ConnMode_Tcp, true),
    };

    public string ConfigPath
    {
        get => _configPath;
        private set { if (Set(ref _configPath, value)) Raise(nameof(ConfigPathDisplay)); }
    }

    /// <summary>Kratek prikaz naloženega profila v orodni vrstici, brez polne poti do datoteke.</summary>
    public string ConfigPathDisplay =>
        string.IsNullOrEmpty(_configPath) ? "" : string.Format(Strings.Main_ConfigPathDisplay, Path.GetFileNameWithoutExtension(_configPath));

    /// <summary>Verzija aplikacije (iz Version v .csproj), za prikaz v naslovni vrstici.</summary>
    public string AppVersion { get; } = "v" + (Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "?");

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
                Log(string.Format(Strings.Log_CasVzorcenjaSpremenjen, clamped));
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
                MoveDeviceUpCommand.RaiseCanExecuteChanged();
                MoveDeviceDownCommand.RaiseCanExecuteChanged();
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
    public string StartStopText => _isRunning ? Strings.Main_StartStop_Stop : Strings.Main_StartStop_Start;

    /// <summary>Znova naloži trenutno aktivno konfiguracijsko datoteko (ali privzeto, če še ni bila izbrana).</summary>
    private void LoadConfig()
    {
        string? path = AppState.LoadLastConfigPath();
        if (path is null)
        {
            StatusText = Strings.St_NiNalozeneKonf;
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
            StatusText = Strings.St_DevicesJsonNiNajden;
            Log(StatusText);
            return;
        }

        _loaded = ConfigLoader.Load(path);
        ConfigPath = _loaded.DevicesPath;
        AppState.SaveLastConfigPath(path);

        foreach (string w in _loaded.Warnings)
            Log(string.Format(Strings.Log_Opozorilo, w));
        if (!_loaded.IsValid)
        {
            foreach (string e in _loaded.Errors)
                Log(string.Format(Strings.Log_Napaka, e));
            StatusText = string.Format(Strings.St_KonfImaNapak, _loaded.Errors.Count);
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

        StatusText = string.Format(Strings.St_Nalozeno, Devices.Count, _loaded.Profiles.Count);
        Log(StatusText);
        RefreshCommands();
    }

    /// <summary>
    /// Po Dodaj/Uredi/Odstrani napravo: znova prebere samo seznam naprav in profile z diska,
    /// brez da bi (kot LoadConfig/LoadConfigFrom) prepisal polja povezave/beleženja v vmesniku
    /// — ta dialogi teh nastavitev ne spreminjajo, zato bi sicer izgubili morebitne še
    /// neshranjene spremembe (COM port, CSV mapa ...), ki jih je uporabnik vpisal v vmesniku.
    /// </summary>
    private void RefreshDevicesFromDisk()
    {
        if (_loaded is null)
            return;

        var reloaded = ConfigLoader.Load(_loaded.DevicesPath);
        foreach (string w in reloaded.Warnings)
            Log(string.Format(Strings.Log_Opozorilo, w));
        if (!reloaded.IsValid)
        {
            foreach (string e in reloaded.Errors)
                Log(string.Format(Strings.Log_Napaka, e));
            StatusText = string.Format(Strings.St_KonfImaNapak, reloaded.Errors.Count);
            Devices.Clear();
            RefreshCommands();
            return;
        }

        _loaded = reloaded;
        Devices.Clear();
        foreach (var dev in reloaded.Config.Devices.Where(d => d.Enabled))
            Devices.Add(new DeviceVm(dev, reloaded.Profiles[dev.Profile]));
        SelectedDevice = Devices.FirstOrDefault();

        StatusText = string.Format(Strings.St_Nalozeno, Devices.Count, reloaded.Profiles.Count);
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
                StatusText = Strings.St_VpisiIpNaslov;
                return;
            }
        }
        else if (SelectedPort is null)
        {
            StatusText = Strings.St_IzberiComPort;
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
                Log(string.Format(Strings.Log_CsvBelezenjeV, csv.Folder));
            }
            catch (Exception ex)
            {
                Log(string.Format(Strings.Log_NapakaMapeCsv, ex.Message));
                return;
            }
        }
        else
        {
            Log(Strings.Log_CsvIzklopljeno);
        }

        if (LogToMySql)
        {
            try
            {
                var mysql = new MySqlLogSink(cfg.MySql);
                mysql.Diagnostic += msg => _dispatcher.BeginInvoke(() => Log(msg));
                sinks.Add(mysql);
                Log(string.Format(Strings.Log_MySqlBelezenjeV, MySqlTable, MySqlHost, MySqlPort, MySqlDatabase, cfg.MySql.ColumnMapping.Count));
            }
            catch (Exception ex)
            {
                Log(string.Format(Strings.Log_NapakaMySqlPovezave, ex.Message));
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
        StatusText = string.Format(Strings.St_BelezenjeTece, cfg.SampleIntervalSeconds, cfg.WriteIntervalSeconds);
        Log(Strings.Log_Zagnano);
    }

    private async void Stop()
    {
        if (_cts is null || _runTask is null)
            return;

        _isBusy = true;
        RefreshCommands();
        StatusText = Strings.St_Ustavljam;
        _cts.Cancel();
        try { await _runTask; }
        catch (Exception ex) { Log(string.Format(Strings.Log_NapakaObUstavljanju, ex.Message)); }

        _service?.Dispose();
        _service = null;
        _cts.Dispose();
        _cts = null;
        _runTask = null;

        foreach (var dev in Devices)
            dev.SetStopped();

        _isBusy = false;
        IsRunning = false;
        StatusText = Strings.St_Ustavljeno;
        Log(Strings.St_Ustavljeno);
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
            Log(string.Format(Strings.Log_NalagamProfil, Path.GetFileNameWithoutExtension(dialogVm.ResultPath)));
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
            Log(string.Format(Strings.Log_ProfilShranjenKot, Path.GetFileNameWithoutExtension(dialogVm.ResultPath)));
        }
        catch (Exception ex)
        {
            Log(string.Format(Strings.Log_NapakaPriShranjevanjuProfila, ex.Message));
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
        var dialog = new OpenFolderDialog { Title = Strings.Dlg_IzberiMapoCsv };
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
            new(Strings.MySqlField_NeUporabi, null),
            new(Strings.MySqlField_CasMeritve, MySqlLogSink.FieldTimestamp),
            new(Strings.MySqlField_NapravaIme, MySqlLogSink.FieldDevice),
            new(Strings.MySqlField_SlaveId, MySqlLogSink.FieldSlaveId),
            new(Strings.MySqlField_StatusNapaka, MySqlLogSink.FieldStatus),
            new(Strings.MySqlField_CasOdziva, MySqlLogSink.FieldResponseMs),
            new(Strings.MySqlField_NapakaKomunikacije, MySqlLogSink.FieldErrorFlag),
            new(Strings.MySqlField_Konstanta, MySqlLogSink.ConstantFieldPrefix),
        };

        if (_loaded is not null)
        {
            var registerNames = _loaded.Profiles.Values
                .SelectMany(p => p.Registers)
                .Select(r => r.Name)
                .Distinct()
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase);
            foreach (string name in registerNames)
                options.Add(new MySqlFieldOption(string.Format(Strings.MySqlField_Register, name), MySqlLogSink.RegisterFieldPrefix + name));
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
                Log(string.Format(Strings.Log_TabelaNeObstaja, MySqlTable, MySqlDatabase));
                return;
            }

            var options = BuildMySqlFieldOptions();
            var existing = MySqlColumnMappings.ToDictionary(m => m.Column, m => m.EffectiveKey);
            MySqlColumnMappings.Clear();
            foreach (string col in columns)
                MySqlColumnMappings.Add(new MySqlColumnMappingVm(col, options, existing.GetValueOrDefault(col)));
            Log(string.Format(Strings.Log_PrebranihStolpcev, columns.Count, MySqlTable));
        }
        catch (Exception ex)
        {
            Log(string.Format(Strings.Log_NapakaBranjaStolpcev, ex.Message));
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
            Log(string.Format(Strings.Log_NapravaDodana, dialogVm.Label, dialogVm.SlaveId));
            RefreshDevicesFromDisk();
        }
    }

    private void EditDevice()
    {
        if (_loaded is null || SelectedDevice is null)
            return;

        var entry = SelectedDevice.Entry;
        if (!_loaded.Profiles.TryGetValue(entry.Profile, out var profile))
        {
            Log(string.Format(Strings.Log_NapakaProfilNiNalozen, entry.Profile, entry.Label));
            return;
        }

        var dialogVm = new AddDeviceViewModel(_loaded.DevicesPath, entry, profile);
        var dialog = new AddDeviceWindow(dialogVm) { Owner = Application.Current.MainWindow };
        bool? result = dialog.ShowDialog();

        if (result == true)
        {
            Log(string.Format(Strings.Log_NapravaPosodobljena, dialogVm.Label, dialogVm.SlaveId));
            RefreshDevicesFromDisk();
        }
    }

    private void RemoveDevice()
    {
        if (_loaded is null || SelectedDevice is null)
            return;

        var entry = SelectedDevice.Entry;
        var choice = MessageBox.Show(
            string.Format(Strings.Msg_OdstraniNapravo, entry.Label, entry.SlaveId, entry.Profile),
            Strings.Msg_OdstraniNapravoTitle, MessageBoxButton.YesNo, MessageBoxImage.Question);
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
                ? string.Format(Strings.Log_NapravaOdstranjena, entry.Label, entry.SlaveId)
                : string.Format(Strings.Log_NapraveNiBiloMogoceNajti, entry.Label));
        }
        catch (Exception ex)
        {
            Log(string.Format(Strings.Log_NapakaOdstranjevanjaNaprave, ex.Message));
            return;
        }

        RefreshDevicesFromDisk();
    }

    /// <summary>
    /// Premakne izbrano napravo za eno mesto navzgor (offset -1) ali navzdol (offset +1) v seznamu
    /// in vrstni red trajno shrani v devices.json.
    /// </summary>
    private void MoveSelectedDevice(int offset)
    {
        if (_loaded is null || SelectedDevice is null)
            return;

        int oldIndex = Devices.IndexOf(SelectedDevice);
        int newIndex = oldIndex + offset;
        if (oldIndex < 0 || newIndex < 0 || newIndex >= Devices.Count)
            return;

        Devices.Move(oldIndex, newIndex);
        MoveDeviceUpCommand.RaiseCanExecuteChanged();
        MoveDeviceDownCommand.RaiseCanExecuteChanged();

        // Prepletenost z morebitnimi onemogočenimi napravami (ki jih Devices ne vsebuje) ohranimo
        // tako, da izpolnimo njihova mesta z novo urejenim zaporedjem omogočenih naprav.
        var enabledInNewOrder = new Queue<DeviceEntry>(Devices.Select(d => d.Entry));
        var newOrder = new List<DeviceEntry>(_loaded.Config.Devices.Count);
        foreach (var entry in _loaded.Config.Devices)
            newOrder.Add(entry.Enabled ? enabledInNewOrder.Dequeue() : entry);

        _loaded.Config.Devices.Clear();
        _loaded.Config.Devices.AddRange(newOrder);
        ConfigLoader.Save(_loaded.Config, _loaded.DevicesPath);
    }

    private void RefreshCommands()
    {
        StartStopCommand.RaiseCanExecuteChanged();
        ReloadCommand.RaiseCanExecuteChanged();
        RefreshPortsCommand.RaiseCanExecuteChanged();
        AddDeviceCommand.RaiseCanExecuteChanged();
        EditDeviceCommand.RaiseCanExecuteChanged();
        RemoveDeviceCommand.RaiseCanExecuteChanged();
        MoveDeviceUpCommand.RaiseCanExecuteChanged();
        MoveDeviceDownCommand.RaiseCanExecuteChanged();
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

/// <summary>En jezik vmesnika v izbirniku (prikazno ime v tem jeziku + dvočrkovna koda kulture).</summary>
public sealed record LanguageOption(string Label, string Code)
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
