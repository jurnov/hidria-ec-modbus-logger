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

    private string _status = Strings.Device_CakamNaPrvoBranje;
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
            Status = string.Format(Strings.Device_OkMs, result.ElapsedMs);
            StatusBrush = OkBrush;
            for (int i = 0; i < result.Readings.Count && i < Registers.Count; i++)
                Registers[i].SetValue(result.Readings[i].Value);
        }
        else
        {
            Status = result.Error ?? Strings.Device_Napaka;
            StatusBrush = ErrorBrush;
        }
    }

    public void ResetStatus()
    {
        Status = Strings.Device_CakamNaPrvoBranje;
        StatusBrush = IdleBrush;
        LastReadText = "";
    }

    /// <summary>Ob ustavitvi zapisovalnika: siva lučka, brez zadnjega odzivnega časa/statusa "OK".</summary>
    public void SetStopped()
    {
        Status = Strings.Device_BrezPovezaveUstavljeno;
        StatusBrush = IdleBrush;
        LastReadText = "";
    }
}

public sealed class RegisterVm : ViewModelBase
{
    private string _valueText = "—";

    public RegisterVm(RegisterDef def)
    {
        IsConstant = def.FunctionValue == ModbusFunction.Constant;
        Address = IsConstant ? "" : $"0x{def.AddressValue:X4}";
        Name = def.Name;
        Unit = def.Unit;
    }

    /// <summary>true, kadar ta "register" nima pravega Modbus naslova (fiksna vrednost iz profila).</summary>
    public bool IsConstant { get; }

    public string Address { get; }
    public string Name { get; }
    public string Unit { get; }
    public string ValueText { get => _valueText; private set => Set(ref _valueText, value); }

    public void SetValue(double value) =>
        ValueText = value.ToString(value == Math.Floor(value) ? "0" : "0.###", CultureInfo.CurrentCulture);
}
