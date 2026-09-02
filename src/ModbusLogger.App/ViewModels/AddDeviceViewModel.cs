using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using Microsoft.Win32;
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
    private string _constantValue = "0";

    public string Address { get => _address; set => Set(ref _address, value); }
    public string Name { get => _name; set => Set(ref _name, value); }
    public string Unit { get => _unit; set => Set(ref _unit, value); }
    public string Type { get => _type; set => Set(ref _type, value); }
    public string Scale { get => _scale; set => Set(ref _scale, value); }
    public string WordOrder { get => _wordOrder; set => Set(ref _wordOrder, value); }

    /// <summary>input (fc 04) | holding (fc 03) | constant — vsak register lahko izbere svojo.</summary>
    public string Function
    {
        get => _function;
        set { if (Set(ref _function, value)) Raise(nameof(IsConstant)); }
    }

    /// <summary>Uporabljeno samo, kadar je Function="constant" — fiksna vrednost, ki se ne bere z vodila.</summary>
    public string ConstantValue { get => _constantValue; set => Set(ref _constantValue, value); }

    /// <summary>true, kadar ta "register" nima pravega Modbus naslova (Function="constant").</summary>
    public bool IsConstant => string.Equals(Function, "constant", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Doda ali uredi napravo v devices.json. Register mapa je vedno urejena neposredno tukaj
/// in se privzeto shrani v profil, ki je izključno "last" te naprave — urejanje registrov
/// za eno napravo torej ne vpliva na druge naprave, tudi če so prej uporabljale isti profil
/// (v tem primeru se ob shranjevanju samodejno "razcepi" v nov, zasebni profil).
/// Za namerno souporabo/predloge sta na voljo ločeni akciji "Naloži profil" in "Shrani kot profil".
/// </summary>
public sealed class AddDeviceViewModel : ViewModelBase
{
    private readonly string _devicesPath;
    private readonly DeviceEntry? _editingOriginal;

    private string _label = "";
    private byte _slaveId = 1;
    private bool _enabled = true;
    private string _validationMessage = "";
    private RegisterRowVm? _selectedRegister;
    private string? _profileToLoad;

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
                        ConstantValue = r.ConstantValue.ToString(CultureInfo.InvariantCulture),
                    });
                }
            }
        }

        if (Registers.Count == 0)
            Registers.Add(new RegisterRowVm());

        _profileToLoad = AvailableProfiles.FirstOrDefault();

        AddRegisterCommand = new RelayCommand(() => Registers.Add(new RegisterRowVm()));
        RemoveRegisterCommand = new RelayCommand(
            () => { if (SelectedRegister is not null) Registers.Remove(SelectedRegister); },
            () => SelectedRegister is not null);
        LoadProfileCommand = new RelayCommand(LoadProfile, () => ProfileToLoad is not null);
        SaveAsProfileCommand = new RelayCommand(SaveAsProfile);
        DeleteProfileCommand = new RelayCommand(DeleteProfile, () => ProfileToLoad is not null);
        SaveCommand = new RelayCommand(Save);
        CancelCommand = new RelayCommand(() => RequestClose?.Invoke(false));
    }

    public bool IsEditMode => _editingOriginal is not null;
    public string DialogTitle => IsEditMode ? "Uredi napravo" : "Dodaj napravo";

    public ObservableCollection<string> AvailableProfiles { get; } = new();
    public ObservableCollection<RegisterRowVm> Registers { get; } = new();

    public RelayCommand AddRegisterCommand { get; }
    public RelayCommand RemoveRegisterCommand { get; }
    public RelayCommand LoadProfileCommand { get; }
    public RelayCommand SaveAsProfileCommand { get; }
    public RelayCommand DeleteProfileCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand CancelCommand { get; }

    /// <summary>true = naprava je bila shranjena, false = uporabnik je preklical.</summary>
    public event Action<bool>? RequestClose;

    public string Label { get => _label; set => Set(ref _label, value); }
    public byte SlaveId { get => _slaveId; set => Set(ref _slaveId, value); }
    public bool Enabled { get => _enabled; set => Set(ref _enabled, value); }
    public string ValidationMessage { get => _validationMessage; private set => Set(ref _validationMessage, value); }

    /// <summary>Izbran profil v spustnem seznamu za "Naloži profil"/"Izbriši profil" (samo predloga, ne trajna povezava).</summary>
    public string? ProfileToLoad
    {
        get => _profileToLoad;
        set
        {
            if (Set(ref _profileToLoad, value))
            {
                LoadProfileCommand.RaiseCanExecuteChanged();
                DeleteProfileCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public RegisterRowVm? SelectedRegister
    {
        get => _selectedRegister;
        set { if (Set(ref _selectedRegister, value)) RemoveRegisterCommand.RaiseCanExecuteChanged(); }
    }

    /// <summary>Naloži izbran obstoječi profil v urejevalnik registrov (nadomesti trenutno vsebino).</summary>
    private void LoadProfile()
    {
        if (ProfileToLoad is null)
            return;

        string profilesDir = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(_devicesPath))!, "profiles");
        string path = Path.Combine(profilesDir, ProfileToLoad + ".json");
        var profile = ConfigLoader.LoadProfileRaw(path, out string? error);
        if (profile is null)
        {
            ValidationMessage = $"Profila '{ProfileToLoad}' ni bilo mogoče naložiti: {error}";
            return;
        }

        Registers.Clear();
        foreach (var r in profile.Registers)
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
                ConstantValue = r.ConstantValue.ToString(CultureInfo.InvariantCulture),
            });
        }
        if (Registers.Count == 0)
            Registers.Add(new RegisterRowVm());

        ValidationMessage = "";
    }

    /// <summary>
    /// Shrani trenutno register mapo kot samostojen profil (predlogo) — odpre Windows okno
    /// "Shrani kot", ki se odpre neposredno v mapi profiles/, kamor uporabnik vpiše ime.
    /// </summary>
    private void SaveAsProfile()
    {
        var errors = new List<string>();
        var parsed = ParseRegisters(errors);
        if (parsed.Count == 0)
            errors.Add("Ni registrov za shranjevanje.");
        if (errors.Count > 0)
        {
            ValidationMessage = string.Join("\n", errors);
            return;
        }

        string profilesDir = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(_devicesPath))!, "profiles");
        Directory.CreateDirectory(profilesDir);

        var dialog = new SaveFileDialog
        {
            Title = "Shrani kot profil",
            InitialDirectory = profilesDir,
            Filter = "Profil (*.json)|*.json",
            DefaultExt = "json",
            FileName = SanitizeFileName(Label.Trim()) is { Length: > 0 } suggested ? suggested + ".json" : "profil.json",
        };
        if (dialog.ShowDialog() != true)
            return;

        try
        {
            string name = Path.GetFileNameWithoutExtension(dialog.FileName);
            var profile = new DeviceProfile { Name = name, Registers = parsed.Select(p => p.Def).ToList() };
            profile.PollGroups.AddRange(BuildPollGroups(parsed));
            ConfigLoader.SaveProfile(profile, dialog.FileName);

            // Osveži seznam, če je datoteka pristala v mapi profiles/ (kamor kaže "Naloži profil").
            AvailableProfiles.Clear();
            foreach (string n in ConfigLoader.ListAvailableProfiles(_devicesPath))
                AvailableProfiles.Add(n);
            if (AvailableProfiles.Contains(name, StringComparer.OrdinalIgnoreCase))
                ProfileToLoad = name;
            ValidationMessage = $"Profil '{name}' shranjen.";
        }
        catch (Exception ex)
        {
            ValidationMessage = $"Napaka pri shranjevanju profila: {ex.Message}";
        }
    }

    /// <summary>Izbriše izbran profil s diska, po potrditvi. Opozori, če ga trenutno uporablja katera naprava.</summary>
    private void DeleteProfile()
    {
        if (ProfileToLoad is null)
            return;
        string name = ProfileToLoad;

        var usedBy = ConfigLoader.Load(_devicesPath).Config.Devices
            .Where(d => string.Equals(d.Profile, name, StringComparison.OrdinalIgnoreCase))
            .Select(d => d.Label)
            .ToList();
        // Naprava, ki jo trenutno urejamo, se ob shranjevanju itak razcepi v svoj profil,
        // zato njena morebitna trenutna uporaba tega profila ni razlog za opozorilo.
        if (_editingOriginal is not null)
            usedBy.Remove(_editingOriginal.Label);

        string warning = usedBy.Count > 0
            ? $"\n\nOpozorilo: ta profil trenutno uporablja(jo) tudi: {string.Join(", ", usedBy)}. Po izbrisu ne bodo delovale, dokler jim ne izberete drugega profila."
            : "";
        var choice = MessageBox.Show(
            $"Izbrišem profil '{name}' s diska?{warning}",
            "Izbriši profil", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (choice != MessageBoxResult.Yes)
            return;

        try
        {
            string profilesDir = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(_devicesPath))!, "profiles");
            File.Delete(Path.Combine(profilesDir, name + ".json"));
            AvailableProfiles.Remove(name);
            ProfileToLoad = AvailableProfiles.FirstOrDefault();
            ValidationMessage = $"Profil '{name}' izbrisan.";
        }
        catch (Exception ex)
        {
            ValidationMessage = $"Napaka pri brisanju profila: {ex.Message}";
        }
    }

    private void Save()
    {
        var errors = new List<string>();
        string label = Label.Trim();
        if (label.Length == 0)
            errors.Add("Ime naprave ne sme biti prazno.");
        if (SlaveId is < 1 or > 247)
            errors.Add("Slave ID mora biti med 1 in 247.");

        var parsed = ParseRegisters(errors);
        if (parsed.Count == 0)
            errors.Add("Naprava potrebuje vsaj en register.");

        if (errors.Count > 0)
        {
            ValidationMessage = string.Join("\n", errors);
            return;
        }

        var newProfile = new DeviceProfile { Registers = parsed.Select(p => p.Def).ToList() };
        newProfile.PollGroups.AddRange(BuildPollGroups(parsed));

        try
        {
            var loaded = ConfigLoader.Load(_devicesPath);
            string profilesDir = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(_devicesPath))!, "profiles");
            Directory.CreateDirectory(profilesDir);

            // Če ta naprava trenutno souporablja profil z drugimi, ga ob shranjevanju "razcepimo"
            // v nov, zasebni profil te naprave — spremembe registrov tako ne vplivajo na druge.
            string profileFileName;
            if (_editingOriginal is not null &&
                loaded.Config.Devices.Count(d => string.Equals(d.Profile, _editingOriginal.Profile, StringComparison.OrdinalIgnoreCase)) <= 1)
            {
                profileFileName = _editingOriginal.Profile;   // že zaseben — obdrži isto ime/datoteko
            }
            else
            {
                profileFileName = GenerateUniquePrivateName(label, profilesDir);
            }

            newProfile.Name = profileFileName;
            ConfigLoader.SaveProfile(newProfile, Path.Combine(profilesDir, profileFileName + ".json"));

            var updatedEntry = new DeviceEntry
            {
                SlaveId = SlaveId,
                Profile = profileFileName,
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

    /// <summary>Razčleni vrstice urejevalnika v RegisterDef + pomožne podatke za BuildPollGroups.</summary>
    private List<(RegisterDef Def, ushort Addr, int WordCount, string Function)> ParseRegisters(List<string> errors)
    {
        var parsed = new List<(RegisterDef Def, ushort Addr, int WordCount, string Function)>();
        foreach (var row in Registers)
        {
            if (string.IsNullOrWhiteSpace(row.Name))
            {
                errors.Add("Vsak register potrebuje ime.");
                continue;
            }

            if (string.Equals(row.Function, "constant", StringComparison.OrdinalIgnoreCase))
            {
                if (!double.TryParse(row.ConstantValue, NumberStyles.Float, CultureInfo.InvariantCulture, out double constantValue))
                {
                    errors.Add($"Neveljavna konstantna vrednost za '{row.Name}': '{row.ConstantValue}'.");
                    continue;
                }
                var constantDef = new RegisterDef
                {
                    Name = row.Name.Trim(),
                    Unit = row.Unit.Trim(),
                    Function = "constant",
                    ConstantValue = constantValue,
                };
                parsed.Add((constantDef, 0, 0, row.Function));
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
        return parsed;
    }

    /// <summary>Poišče prosto ime datoteke profila, ki izhaja iz imena naprave (npr. "Ventilator-1", "-2" ...).</summary>
    private static string GenerateUniquePrivateName(string label, string profilesDir)
    {
        string baseName = SanitizeFileName(label);
        if (baseName.Length == 0)
            baseName = "naprava";

        string candidate = baseName;
        int i = 2;
        while (File.Exists(Path.Combine(profilesDir, candidate + ".json")))
        {
            candidate = $"{baseName}-{i}";
            i++;
        }
        return candidate;
    }

    /// <summary>
    /// Registre razdeli po funkciji (input/holding — vsak Modbus paket lahko uporabi le eno),
    /// nato jih znotraj vsake funkcije po naslovu požrešno združi v čim manj blokov,
    /// tako da nihče ne presega Modbus omejitve 125 registrov na branje. Konstantni "registri"
    /// se nikoli ne berejo z vodila, zato zanje ni poll group-a.
    /// </summary>
    private static List<PollGroup> BuildPollGroups(List<(RegisterDef Def, ushort Addr, int WordCount, string Function)> registers)
    {
        var groups = new List<PollGroup>();
        var pollable = registers.Where(r => !string.Equals(r.Function, "constant", StringComparison.OrdinalIgnoreCase));

        foreach (var byFunction in pollable.GroupBy(r => r.Function, StringComparer.OrdinalIgnoreCase))
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
