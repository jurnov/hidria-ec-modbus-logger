using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using ModbusLogger.Core;

namespace ModbusLogger.App.ViewModels;

/// <summary>
/// En dialog za obe smeri: shrani trenutno konfiguracijo pod izbranim imenom,
/// ali izberi eno od že shranjenih za nalaganje. Vse konfiguracije so *.json
/// datoteke v isti mapi kot devices.json (da si delijo isto mapo profiles/).
/// </summary>
public sealed class ConfigPickerViewModel : ViewModelBase
{
    private readonly string _configDir;

    private string _name = "";
    private string? _selectedConfig;
    private string _validationMessage = "";

    public ConfigPickerViewModel(string configDir, bool isSaveMode, string? currentName)
    {
        _configDir = configDir;
        IsSaveMode = isSaveMode;

        foreach (string n in ConfigLoader.ListAvailableConfigs(configDir))
            AvailableConfigs.Add(n);

        _name = currentName ?? "";
        _selectedConfig = AvailableConfigs.FirstOrDefault(n =>
            string.Equals(n, currentName, StringComparison.OrdinalIgnoreCase));

        ConfirmCommand = new RelayCommand(Confirm);
        CancelCommand = new RelayCommand(() => RequestClose?.Invoke(false));
    }

    public bool IsSaveMode { get; }
    public string DialogTitle => IsSaveMode ? Strings.ConfigPicker_Title_Save : Strings.ConfigPicker_Title_Load;
    public string ConfirmButtonText => IsSaveMode ? Strings.ConfigPicker_ConfirmBtn_Save : Strings.ConfigPicker_ConfirmBtn_Load;

    public ObservableCollection<string> AvailableConfigs { get; } = new();

    /// <summary>Ime za shranjevanje; urejevalno samo v načinu shranjevanja.</summary>
    public string Name { get => _name; set => Set(ref _name, value); }

    public string? SelectedConfig
    {
        get => _selectedConfig;
        set
        {
            if (Set(ref _selectedConfig, value) && IsSaveMode && value is not null)
                Name = value;   // klik na obstoječo predlaga prepis pod istim imenom
        }
    }

    public string ValidationMessage { get => _validationMessage; private set => Set(ref _validationMessage, value); }

    /// <summary>Polna pot izbrane/vpisane konfiguracije po potrditvi.</summary>
    public string? ResultPath { get; private set; }

    public RelayCommand ConfirmCommand { get; }
    public RelayCommand CancelCommand { get; }

    /// <summary>true = potrjeno (glej ResultPath), false = preklicano.</summary>
    public event Action<bool>? RequestClose;

    private void Confirm()
    {
        string name;
        if (IsSaveMode)
        {
            name = SanitizeFileName(Name.Trim());
            if (name.Length == 0)
            {
                ValidationMessage = Strings.ConfigPicker_Err_VpisiIme;
                return;
            }
        }
        else
        {
            if (SelectedConfig is null)
            {
                ValidationMessage = Strings.ConfigPicker_Err_IzberiProfil;
                return;
            }
            name = SelectedConfig;
        }

        ResultPath = Path.Combine(_configDir, name + ".json");
        RequestClose?.Invoke(true);
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (char c in name)
            sb.Append(invalid.Contains(c) || c == ' ' ? '-' : c);
        return sb.ToString();
    }
}
