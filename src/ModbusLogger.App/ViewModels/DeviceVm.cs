using System.Globalization;
using System.Windows.Media;
using ModbusLogger.Core;

namespace ModbusLogger.App.ViewModels;

/// <summary>Ena naprava v seznamu: status komunikacije + živi seznam registrov.</summary>
public sealed class DeviceVm : ViewModelBase
{
    private static readonly Brush IdleBrush = Brushes.Gray;
    private static readonly Brush OkBrush = Brushes.LimeGreen;
    private static readonly Brush ErrorBrush = Brushes.OrangeRed;

    private string _status = "čakam na prvo branje";
    private Brush _statusBrush = IdleBrush;
    private string _lastReadText = "";

    public DeviceVm(DeviceEntry entry, DeviceProfile profile)
    {
        Entry = entry;
        foreach (var reg in profile.Registers)
            Registers.Add(new RegisterVm(reg));
    }

    public DeviceEntry Entry { get; }
    public string Label => Entry.Label;
    public byte SlaveId => Entry.SlaveId;
    public string ProfileName => Entry.Profile;

    public System.Collections.ObjectModel.ObservableCollection<RegisterVm> Registers { get; } = new();

    public string Status { get => _status; private set => Set(ref _status, value); }
    public Brush StatusBrush { get => _statusBrush; private set => Set(ref _statusBrush, value); }
    public string LastReadText { get => _lastReadText; private set => Set(ref _lastReadText, value); }

    public void Update(DeviceReadResult result)
    {
        LastReadText = result.Timestamp.ToString("HH:mm:ss");
        if (result.Success)
        {
            Status = $"OK ({result.ElapsedMs} ms)";
            StatusBrush = OkBrush;
            for (int i = 0; i < result.Readings.Count && i < Registers.Count; i++)
                Registers[i].SetValue(result.Readings[i].Value);
        }
        else
        {
            Status = result.Error ?? "napaka";
            StatusBrush = ErrorBrush;
        }
    }

    public void ResetStatus()
    {
        Status = "čakam na prvo branje";
        StatusBrush = IdleBrush;
        LastReadText = "";
        foreach (var reg in Registers)
            reg.History.Clear();
    }
}

/// <summary>Ena vzorčena vrednost registra ob določenem času — za graf.</summary>
public sealed record RegisterSample(DateTime Time, double Value);

public sealed class RegisterVm : ViewModelBase
{
    /// <summary>Omeji zgodovino, da poraba pomnilnika ne raste neomejeno pri dolgotrajnem teku.</summary>
    private const int MaxHistory = 2000;

    private string _valueText = "—";
    private bool _showOnChart;
    private bool _useRightAxis;

    public RegisterVm(RegisterDef def)
    {
        Address = $"0x{def.AddressValue:X4}";
        Name = def.Name;
        Unit = def.Unit;
    }

    public string Address { get; }
    public string Name { get; }
    public string Unit { get; }
    public string DisplayName => string.IsNullOrEmpty(Unit) ? Name : $"{Name} [{Unit}]";
    public string ValueText { get => _valueText; private set => Set(ref _valueText, value); }

    /// <summary>Ali je ta register trenutno prikazan na grafu (izbira v pojavnem meniju "Parametri").</summary>
    public bool ShowOnChart { get => _showOnChart; set => Set(ref _showOnChart, value); }

    /// <summary>Ko je izbran za graf: false = leva os, true = desna os.</summary>
    public bool UseRightAxis { get => _useRightAxis; set => Set(ref _useRightAxis, value); }

    /// <summary>Zgodovina vrednosti od zagona beleženja naprej, za grafični prikaz.</summary>
    public System.Collections.ObjectModel.ObservableCollection<RegisterSample> History { get; } = new();

    public void SetValue(double value)
    {
        ValueText = value.ToString(value == Math.Floor(value) ? "0" : "0.###", CultureInfo.CurrentCulture);
        History.Add(new RegisterSample(DateTime.Now, value));
        while (History.Count > MaxHistory)
            History.RemoveAt(0);
    }
}
