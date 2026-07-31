using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Windows;
using System.Windows.Threading;
using ModbusLogger.App;
using ModbusLogger.Core;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Legends;
using OxyPlot.Series;

namespace ModbusLogger.App.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private readonly Dispatcher _dispatcher;

    private LoadedConfig? _loaded;
    private PollService? _service;
    private CancellationTokenSource? _cts;
    private Task? _runTask;

    private string _configPath = "";
    private string? _selectedPort;
    private int _baud = 19200;
    private string _parity = "even";
    private int _stopBits = 1;
    private int _timeoutMs = 1000;
    private int _retries = 2;
    private int _intervalSeconds = 60;
    private bool _logToCsv = true;
    private bool _isRunning;
    private bool _isBusy;
    private string _statusText = "";
    private DeviceVm? _selectedDevice;
    private bool _isChartView;
    private TimeRangeOption _selectedTimeRange;
    private readonly List<RegisterVm> _watchedRegisters = new();
    private TrafficWindow? _trafficWindow;

    public MainViewModel(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        StartStopCommand = new RelayCommand(StartStop, () => !_isBusy && (_isRunning || _loaded is { IsValid: true }));
        ReloadCommand = new RelayCommand(OpenLoadConfigDialog, () => !_isRunning && !_isBusy && _loaded is not null);
        RefreshPortsCommand = new RelayCommand(RefreshPorts, () => !_isRunning);
        SaveConfigCommand = new RelayCommand(OpenSaveConfigDialog, () => !_isRunning && !_isBusy && _loaded is not null);
        OpenLogsCommand = new RelayCommand(() => OpenInExplorer(ResolveLogsFolder()));
        AddDeviceCommand = new RelayCommand(AddDevice, () => !_isRunning && !_isBusy && _loaded is { IsValid: true });
        EditDeviceCommand = new RelayCommand(EditDevice, () => !_isRunning && !_isBusy && SelectedDevice is not null);
        RemoveDeviceCommand = new RelayCommand(RemoveDevice, () => !_isRunning && !_isBusy && SelectedDevice is not null);
        ToggleChartViewCommand = new RelayCommand(() => IsChartView = !IsChartView);
        ShowTrafficCommand = new RelayCommand(ShowTraffic);
        _selectedTimeRange = TimeRangeOptions[^1];   // "Vse" privzeto

        ChartModel = new PlotModel();
        ChartModel.Legends.Add(new Legend { LegendPosition = LegendPosition.TopRight, LegendPlacement = LegendPlacement.Outside });
        ChartModel.Axes.Add(new DateTimeAxis { Position = AxisPosition.Bottom, StringFormat = "HH:mm:ss", Title = "Čas" });
        ChartModel.Axes.Add(new LinearAxis { Position = AxisPosition.Left, Key = LeftAxisKey, Title = "Vrednost (levo)" });
        ChartModel.Axes.Add(new LinearAxis { Position = AxisPosition.Right, Key = RightAxisKey, Title = "Vrednost (desno)" });

        RefreshPorts();
        LoadConfig();
    }

    private const string LeftAxisKey = "Left";
    private const string RightAxisKey = "Right";

    public ObservableCollection<DeviceVm> Devices { get; } = new();
    public ObservableCollection<string> Ports { get; } = new();
    public ObservableCollection<string> LogLines { get; } = new();
    public ObservableCollection<TrafficExchangeVm> TrafficLog { get; } = new();
    public string[] FramingOptions { get; } = { "8E1", "8O1", "8N1", "8N2" };
    public int[] BaudRateOptions { get; } = { 1200, 2400, 4800, 9600, 19200, 38400, 57600, 115200 };

    /// <summary>Časovno okno grafa; Span=null pomeni "vse", kar je omejeno le z zamejeno zgodovino registra.</summary>
    public TimeRangeOption[] TimeRangeOptions { get; } = new[]
    {
        new TimeRangeOption("1 min", TimeSpan.FromMinutes(1)),
        new TimeRangeOption("5 min", TimeSpan.FromMinutes(5)),
        new TimeRangeOption("10 min", TimeSpan.FromMinutes(10)),
        new TimeRangeOption("30 min", TimeSpan.FromMinutes(30)),
        new TimeRangeOption("1 h", TimeSpan.FromHours(1)),
        new TimeRangeOption("Vse", null),
    };

    public RelayCommand StartStopCommand { get; }
    public RelayCommand ReloadCommand { get; }
    public RelayCommand RefreshPortsCommand { get; }
    public RelayCommand SaveConfigCommand { get; }
    public RelayCommand OpenLogsCommand { get; }
    public RelayCommand AddDeviceCommand { get; }
    public RelayCommand EditDeviceCommand { get; }
    public RelayCommand RemoveDeviceCommand { get; }
    public RelayCommand ToggleChartViewCommand { get; }
    public RelayCommand ShowTrafficCommand { get; }

    public PlotModel ChartModel { get; }

    public string ConfigPath { get => _configPath; private set => Set(ref _configPath, value); }
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
    public int IntervalSeconds
    {
        get => _intervalSeconds;
        set
        {
            int clamped = Math.Max(1, value);
            if (!Set(ref _intervalSeconds, clamped))
                return;
            if (_service is not null)
            {
                _service.IntervalSeconds = clamped;
                Log($"Interval vzorčenja spremenjen na {clamped} s (velja takoj).");
            }
        }
    }
    public bool LogToCsv { get => _logToCsv; set => Set(ref _logToCsv, value); }
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
                AttachRegisterWatchers(value);
                RefreshChart();
            }
        }
    }

    /// <summary>true = prikazan je graf, false = tabela vseh registrov.</summary>
    public bool IsChartView
    {
        get => _isChartView;
        set
        {
            if (Set(ref _isChartView, value))
            {
                Raise(nameof(ChartToggleText));
                if (value)
                    RefreshChart();
            }
        }
    }

    public string ChartToggleText => IsChartView ? "Prikaži tabelo" : "Prikaži graf";

    /// <summary>Časovno okno, ki se prikazuje na grafu (npr. zadnjih 5 min).</summary>
    public TimeRangeOption SelectedTimeRange
    {
        get => _selectedTimeRange;
        set { if (Set(ref _selectedTimeRange, value)) RefreshChart(); }
    }

    /// <summary>Opazuje ShowOnChart/UseRightAxis na registrih trenutno izbrane naprave, da lahko graf sproti osveži.</summary>
    private void AttachRegisterWatchers(DeviceVm? device)
    {
        foreach (var r in _watchedRegisters)
            r.PropertyChanged -= OnRegisterChartPropertyChanged;
        _watchedRegisters.Clear();

        if (device is null)
            return;
        foreach (var r in device.Registers)
        {
            r.PropertyChanged += OnRegisterChartPropertyChanged;
            _watchedRegisters.Add(r);
        }
    }

    private void OnRegisterChartPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(RegisterVm.ShowOnChart) or nameof(RegisterVm.UseRightAxis))
            RefreshChart();
    }

    private void RefreshChart()
    {
        ChartModel.Series.Clear();
        var device = SelectedDevice;
        if (device is not null)
        {
            DateTime? cutoff = SelectedTimeRange.Span is { } span ? DateTime.Now - span : null;
            foreach (var reg in device.Registers.Where(r => r.ShowOnChart))
            {
                var series = new LineSeries
                {
                    Title = reg.DisplayName,
                    YAxisKey = reg.UseRightAxis ? RightAxisKey : LeftAxisKey,
                    MarkerType = MarkerType.None,
                };
                foreach (var sample in reg.History)
                {
                    if (cutoff is not null && sample.Time < cutoff.Value)
                        continue;
                    series.Points.Add(DateTimeAxis.CreateDataPoint(sample.Time, sample.Value));
                }
                ChartModel.Series.Add(series);
            }
        }
        ChartModel.InvalidatePlot(true);
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
    private void LoadConfig() => LoadConfigFrom(_loaded?.DevicesPath ?? ConfigLoader.FindDefaultConfig());

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
        Baud = cfg.Serial.Baud;
        Framing = FormatFraming(cfg.Serial.Parity, cfg.Serial.StopBits);
        TimeoutMs = cfg.Serial.TimeoutMs;
        Retries = cfg.Serial.Retries;
        IntervalSeconds = cfg.PollIntervalSeconds;
        LogToCsv = cfg.Logging.Enabled;
        SelectedPort = Ports.FirstOrDefault(p => p.Equals(cfg.Serial.Port, StringComparison.OrdinalIgnoreCase))
                       ?? Ports.FirstOrDefault();

        Devices.Clear();
        foreach (var dev in cfg.Devices.Where(d => d.Enabled))
            Devices.Add(new DeviceVm(dev, _loaded.Profiles[dev.Profile]));
        SelectedDevice = Devices.FirstOrDefault();

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
        if (SelectedPort is null)
        {
            StatusText = "Izberi COM port.";
            return;
        }

        // Nastavitve iz vmesnika veljajo za ta zagon; trajne spremembe sodijo v devices.json.
        var cfg = _loaded.Config;
        cfg.Serial.Port = SelectedPort;
        cfg.Serial.Baud = Baud;
        cfg.Serial.Parity = _parity;
        cfg.Serial.StopBits = _stopBits;
        cfg.Serial.TimeoutMs = TimeoutMs;
        cfg.Serial.Retries = Retries;
        cfg.PollIntervalSeconds = Math.Max(1, IntervalSeconds);

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

        foreach (var dev in Devices)
            dev.ResetStatus();

        TrafficLog.Clear();
        _service = new PollService(_loaded, sinks);
        _service.Message += msg => _dispatcher.BeginInvoke(() => Log(msg));
        _service.DeviceRead += (entry, result) => _dispatcher.BeginInvoke(() =>
        {
            var dev = Devices.FirstOrDefault(d => ReferenceEquals(d.Entry, entry));
            dev?.Update(result);
            if (IsChartView && dev is not null && ReferenceEquals(dev, SelectedDevice))
                RefreshChart();
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
        StatusText = $"Beleženje teče (interval {cfg.PollIntervalSeconds} s).";
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

    private string? ResolveLogsFolder()
    {
        if (_loaded is null)
            return null;
        string folder = _loaded.Config.Logging.Folder;
        return Path.IsPathRooted(folder)
            ? folder
            : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(_loaded.DevicesPath)!, folder));
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
        if (_loaded is null)
            return;

        string dir = Path.GetDirectoryName(_loaded.DevicesPath)!;
        string currentName = Path.GetFileNameWithoutExtension(_loaded.DevicesPath);
        var dialogVm = new ConfigPickerViewModel(dir, isSaveMode: false, currentName);
        var dialog = new ConfigPickerWindow(dialogVm) { Owner = Application.Current.MainWindow };
        bool? result = dialog.ShowDialog();

        if (result == true && dialogVm.ResultPath is not null)
        {
            Log($"Nalagam konfiguracijo: {Path.GetFileNameWithoutExtension(dialogVm.ResultPath)}");
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
            if (SelectedPort is not null)
                cfg.Serial.Port = SelectedPort;
            cfg.Serial.Baud = Baud;
            cfg.Serial.Parity = _parity;
            cfg.Serial.StopBits = _stopBits;
            cfg.Serial.TimeoutMs = TimeoutMs;
            cfg.Serial.Retries = Retries;
            cfg.PollIntervalSeconds = Math.Max(1, IntervalSeconds);
            cfg.Logging.Enabled = LogToCsv;

            ConfigLoader.Save(cfg, dialogVm.ResultPath);
            Log($"Konfiguracija shranjena kot: {Path.GetFileNameWithoutExtension(dialogVm.ResultPath)}");
        }
        catch (Exception ex)
        {
            Log($"NAPAKA pri shranjevanju konfiguracije: {ex.Message}");
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

/// <summary>Ena možnost v izbirniku časovnega okna grafa. Span=null pomeni brez omejitve (vsa zgodovina).</summary>
public sealed record TimeRangeOption(string Label, TimeSpan? Span)
{
    public override string ToString() => Label;
}
