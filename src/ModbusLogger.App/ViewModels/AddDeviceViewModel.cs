using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;
using ModbusLogger.Core;

namespace ModbusLogger.App.ViewModels;

/// <summary>Ena vrstica v urejevalniku registrov profila.</summary>
public sealed class RegisterRowVm : ViewModelBase
{
    private string _address = "0x0000";
    private string _name = "";
    private string _unit = "";
    private string _type = "uint16";
    private string _scale = "1";
    private string _wordOrder = "big";
    private string _function = "input";

    public string Address { get => _address; set => Set(ref _address, value); }
    public string Name { get => _name; set => Set(ref _name, value); }
    public string Unit { get => _unit; set => Set(ref _unit, value); }
    public string Type { get => _type; set => Set(ref _type, value); }
    public string Scale { get => _scale; set => Set(ref _scale, value); }
    public string WordOrder { get => _wordOrder; set => Set(ref _wordOrder, value); }

    /// <summary>input (fc 04) | holding (fc 03) — vsak register lahko izbere svojo.</summary>
    public string Function { get => _function; set => Set(ref _function, value); }
}

/// <summary>
/// Doda ali uredi napravo v devices.json — z izbiro obstoječega profila ali z
/// ustvarjanjem/urejanjem profila neposredno (register mapa).
/// </summary>
public sealed class AddDeviceViewModel : ViewModelBase
{
    private readonly string _devicesPath;
    private readonly DeviceEntry? _editingOriginal;

    private string _label = "";
    private byte _slaveId = 1;
    private bool _enabled = true;
    private string? _selectedProfile;
    private bool _isNewProfile;
    private string _newProfileName = "";
    private string _validationMessage = "";
    private RegisterRowVm? _selectedRegister;

    /// <summary>Način dodajanja nove naprave.</summary>
    public AddDeviceViewModel(string devicesPath) : this(devicesPath, null, null) { }

    /// <summary>Način urejanja obstoječe naprave — editingProfile je njen trenutno naloženi profil.</summary>
    public AddDeviceViewModel(string devicesPath, DeviceEntry? editing, DeviceProfile? editingProfile)
    {
        _devicesPath = devicesPath;
        _editingOriginal = editing;

        foreach (string name in ConfigLoader.ListAvailableProfiles(devicesPath))
            AvailableProfiles.Add(name);

        if (editing is not null)
        {
            _label = editing.Label;
            _slaveId = editing.SlaveId;
            _enabled = editing.Enabled;
            SelectedProfile = AvailableProfiles.FirstOrDefault(p =>
                string.Equals(p, editing.Profile, StringComparison.OrdinalIgnoreCase)) ?? editing.Profile;
            _newProfileName = editing.Profile;
            IsNewProfile = true;   // privzeto pokaži urejevalnik registrov trenutnega profila

            if (editingProfile is not null)
            {
                foreach (var r in editingProfile.Registers)
                {
                    Registers.Add(new RegisterRowVm
                    {
                        Address = r.Address,
                        Name = r.Name,
                        Unit = r.Unit,
                        Type = r.Type,
                        Scale = r.Scale.ToString(CultureInfo.InvariantCulture),
                        WordOrder = r.WordOrder,
                        Function = r.Function,
                    });
                }
            }
            if (Registers.Count == 0)
                Registers.Add(new RegisterRowVm());
        }
        else
        {
            SelectedProfile = AvailableProfiles.FirstOrDefault();
            IsNewProfile = SelectedProfile is null;
            Registers.Add(new RegisterRowVm());
        }

        AddRegisterCommand = new RelayCommand(() => Registers.Add(new RegisterRowVm()));
        RemoveRegisterCommand = new RelayCommand(
            () => { if (SelectedRegister is not null) Registers.Remove(SelectedRegister); },
            () => SelectedRegister is not null);
        SaveCommand = new RelayCommand(Save);
        CancelCommand = new RelayCommand(() => RequestClose?.Invoke(false));
    }

    public bool IsEditMode => _editingOriginal is not null;
    public string DialogTitle => IsEditMode ? "Uredi napravo" : "Dodaj napravo";

    public ObservableCollection<string> AvailableProfiles { get; } = new();
    public ObservableCollection<RegisterRowVm> Registers { get; } = new();

    public RelayCommand AddRegisterCommand { get; }
    public RelayCommand RemoveRegisterCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand CancelCommand { get; }

    /// <summary>true = naprava je bila shranjena, false = uporabnik je preklical.</summary>
    public event Action<bool>? RequestClose;

    public string Label { get => _label; set => Set(ref _label, value); }
    public byte SlaveId { get => _slaveId; set => Set(ref _slaveId, value); }
    public bool Enabled { get => _enabled; set => Set(ref _enabled, value); }
    public string? SelectedProfile { get => _selectedProfile; set => Set(ref _selectedProfile, value); }

    public bool IsNewProfile
    {
        get => _isNewProfile;
        set { if (Set(ref _isNewProfile, value)) Raise(nameof(UseExistingProfile)); }
    }

    /// <summary>Nasprotje IsNewProfile — za IsEnabled vezavo izbirnika obstoječih profilov.</summary>
    public bool UseExistingProfile => !_isNewProfile;

    public string NewProfileName { get => _newProfileName; set => Set(ref _newProfileName, value); }
    public string ValidationMessage { get => _validationMessage; private set => Set(ref _validationMessage, value); }

    public RegisterRowVm? SelectedRegister
    {
        get => _selectedRegister;
        set { if (Set(ref _selectedRegister, value)) RemoveRegisterCommand.RaiseCanExecuteChanged(); }
    }

    private void Save()
    {
        var errors = new List<string>();
        string label = Label.Trim();
        if (label.Length == 0)
            errors.Add("Ime naprave ne sme biti prazno.");
        if (SlaveId is < 1 or > 247)
            errors.Add("Slave ID mora biti med 1 in 247.");

        string profileName;
        DeviceProfile? newProfile = null;
        string? newProfilePath = null;

        if (IsNewProfile)
        {
            profileName = SanitizeFileName(NewProfileName.Trim());
            if (profileName.Length == 0)
                errors.Add("Ime novega profila ne sme biti prazno.");

            string profilesDir = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(_devicesPath))!, "profiles");
            newProfilePath = Path.Combine(profilesDir, profileName + ".json");
            bool overwritingSameProfile = _editingOriginal is not null &&
                string.Equals(profileName, _editingOriginal.Profile, StringComparison.OrdinalIgnoreCase);
            if (profileName.Length > 0 && File.Exists(newProfilePath) && !overwritingSameProfile)
                errors.Add($"Profil '{profileName}' že obstaja — izberi drugo ime.");

            var parsed = new List<(RegisterDef Def, ushort Addr, int WordCount, string Function)>();
            foreach (var row in Registers)
            {
                if (string.IsNullOrWhiteSpace(row.Name))
                {
                    errors.Add("Vsak register potrebuje ime.");
                    continue;
                }
                if (!ConfigLoader.TryParseAddress(row.Address, out ushort addr))
                {
                    errors.Add($"Neveljaven naslov registra '{row.Name}': '{row.Address}'.");
                    continue;
                }
                if (!double.TryParse(row.Scale, NumberStyles.Float, CultureInfo.InvariantCulture, out double scale))
                {
                    errors.Add($"Neveljavna skala za register '{row.Name}': '{row.Scale}'.");
                    continue;
                }
                int wordCount = row.Type is "uint32" or "int32" or "float32" ? 2 : 1;
                var def = new RegisterDef
                {
                    Address = row.Address.Trim(),
                    Name = row.Name.Trim(),
                    Unit = row.Unit.Trim(),
                    Type = row.Type,
                    Scale = scale,
                    WordOrder = row.WordOrder,
                    Function = row.Function,
                };
                parsed.Add((def, addr, wordCount, row.Function));
            }

            if (parsed.Count == 0)
            {
                errors.Add("Nov profil potrebuje vsaj en register.");
            }
            else if (errors.Count == 0)
            {
                newProfile = new DeviceProfile { Name = profileName, Registers = parsed.Select(p => p.Def).ToList() };
                newProfile.PollGroups.AddRange(BuildPollGroups(parsed));
            }
        }
        else
        {
            if (SelectedProfile is null)
                errors.Add("Izberi obstoječi profil naprave.");
            profileName = SelectedProfile ?? "";
        }

        if (errors.Count > 0)
        {
            ValidationMessage = string.Join("\n", errors);
            return;
        }

        try
        {
            if (newProfile is not null && newProfilePath is not null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(newProfilePath)!);
                ConfigLoader.SaveProfile(newProfile, newProfilePath);
            }

            var loaded = ConfigLoader.Load(_devicesPath);
            var updatedEntry = new DeviceEntry
            {
                SlaveId = SlaveId,
                Profile = IsNewProfile ? newProfile!.Name : (SelectedProfile ?? ""),
                Label = label,
                Enabled = Enabled,
            };

            int existingIndex = _editingOriginal is null ? -1 : loaded.Config.Devices.FindIndex(d =>
                d.SlaveId == _editingOriginal.SlaveId &&
                string.Equals(d.Profile, _editingOriginal.Profile, StringComparison.OrdinalIgnoreCase) &&
                d.Label == _editingOriginal.Label);

            if (existingIndex >= 0)
                loaded.Config.Devices[existingIndex] = updatedEntry;
            else
                loaded.Config.Devices.Add(updatedEntry);

            ConfigLoader.Save(loaded.Config, _devicesPath);
        }
        catch (Exception ex)
        {
            ValidationMessage = $"Napaka pri shranjevanju: {ex.Message}";
            return;
        }

        RequestClose?.Invoke(true);
    }

    /// <summary>
    /// Registre razdeli po funkciji (input/holding — vsak Modbus paket lahko uporabi le eno),
    /// nato jih znotraj vsake funkcije po naslovu požrešno združi v čim manj blokov,
    /// tako da nihče ne presega Modbus omejitve 125 registrov na branje.
    /// </summary>
    private static List<PollGroup> BuildPollGroups(List<(RegisterDef Def, ushort Addr, int WordCount, string Function)> registers)
    {
        var groups = new List<PollGroup>();

        foreach (var byFunction in registers.GroupBy(r => r.Function, StringComparer.OrdinalIgnoreCase))
        {
            var sorted = byFunction.OrderBy(r => r.Addr).ToList();
            ushort? groupStart = null;
            ushort groupEnd = 0;

            foreach (var r in sorted)
            {
                ushort end = (ushort)(r.Addr + r.WordCount);
                if (groupStart is null)
                {
                    groupStart = r.Addr;
                    groupEnd = end;
                    continue;
                }

                ushort candidateEnd = Math.Max(groupEnd, end);
                if (candidateEnd - groupStart.Value <= 125)
                {
                    groupEnd = candidateEnd;
                }
                else
                {
                    groups.Add(new PollGroup { Function = byFunction.Key, StartAddress = $"0x{groupStart:X4}", Count = (ushort)(groupEnd - groupStart.Value) });
                    groupStart = r.Addr;
                    groupEnd = end;
                }
            }

            if (groupStart is not null)
                groups.Add(new PollGroup { Function = byFunction.Key, StartAddress = $"0x{groupStart:X4}", Count = (ushort)(groupEnd - groupStart.Value) });
        }

        return groups;
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
