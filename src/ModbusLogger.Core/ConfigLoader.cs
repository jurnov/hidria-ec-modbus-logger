using System.Globalization;
using System.Text.Json;

namespace ModbusLogger.Core;

public sealed class LoadedConfig
{
    public required string DevicesPath { get; init; }
    public required AppConfig Config { get; init; }
    public required Dictionary<string, DeviceProfile> Profiles { get; init; }
    public List<string> Errors { get; } = new();
    public List<string> Warnings { get; } = new();
    public bool IsValid => Errors.Count == 0;
}

public static class ConfigLoader
{
    private static readonly JsonSerializerOptions WriteOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// Prepiše devices.json. Opozorilo: ker gre za polno prepisovanje objekta, se
    /// ročno vpisani komentarji (//) v datoteki ob tem izgubijo.
    /// </summary>
    public static void Save(AppConfig config, string devicesJsonPath) =>
        File.WriteAllText(devicesJsonPath, JsonSerializer.Serialize(config, WriteOpts));

    public static void SaveProfile(DeviceProfile profile, string profilePath) =>
        File.WriteAllText(profilePath, JsonSerializer.Serialize(profile, WriteOpts));

    /// <summary>Prebere en profil neposredno (za "Naloži profil" v urejevalniku naprave) brez validacije.</summary>
    public static DeviceProfile? LoadProfileRaw(string profilePath, out string? error) =>
        Deserialize<DeviceProfile>(profilePath, out error);

    /// <summary>Vsi profili (imena datotek brez .json) v mapi profiles/ ob devices.json.</summary>
    public static List<string> ListAvailableProfiles(string devicesJsonPath)
    {
        string dir = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(devicesJsonPath))!, "profiles");
        return ListJsonFileNames(dir);
    }

    /// <summary>
    /// Vse shranjene konfiguracije (imena *.json datotek, brez končnice) v isti mapi kot devices.json.
    /// Mapa "profiles" ni vključena, ker je to podmapa, ne konfiguracijska datoteka.
    /// </summary>
    public static List<string> ListAvailableConfigs(string configDir) => ListJsonFileNames(configDir);

    private static List<string> ListJsonFileNames(string dir)
    {
        if (!Directory.Exists(dir))
            return new List<string>();
        return Directory.GetFiles(dir, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(n => !string.IsNullOrEmpty(n))
            .Select(n => n!)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Naloži devices.json in vse profile, na katere se sklicuje, ter vse validira.</summary>
    public static LoadedConfig Load(string devicesJsonPath)
    {
        var config = Deserialize<AppConfig>(devicesJsonPath, out string? configError);
        var result = new LoadedConfig
        {
            DevicesPath = Path.GetFullPath(devicesJsonPath),
            Config = config ?? new AppConfig(),
            Profiles = new(StringComparer.OrdinalIgnoreCase),
        };
        if (config is null)
        {
            result.Errors.Add($"{devicesJsonPath}: {configError}");
            return result;
        }

        ValidateSerial(config.Serial, result);
        ValidateLogging(config.Logging, result);

        if (config.SampleIntervalSeconds < 1)
            result.Errors.Add(string.Format(Strings.Err_SampleIntervalMoraBiti, config.SampleIntervalSeconds));
        if (config.WriteIntervalSeconds < 1)
            result.Errors.Add(string.Format(Strings.Err_WriteIntervalMoraBiti, config.WriteIntervalSeconds));

        if (config.Devices.Count == 0)
            result.Errors.Add(Strings.Err_DevicesListPrazen);

        string profilesDir = Path.Combine(Path.GetDirectoryName(result.DevicesPath)!, "profiles");

        foreach (var dev in config.Devices)
        {
            string who = string.Format(Strings.Err_Who_Naprava, dev.Label, dev.SlaveId);
            if (dev.SlaveId is < 1 or > 247)
                result.Errors.Add(string.Format(Strings.Err_NapravaSlaveIdMoraBiti, who));
            if (string.IsNullOrWhiteSpace(dev.Profile))
            {
                result.Errors.Add(string.Format(Strings.Err_NapravaManjkaProfile, who));
                continue;
            }
            if (result.Profiles.ContainsKey(dev.Profile))
                continue;

            string profilePath = Path.Combine(profilesDir, dev.Profile + ".json");
            if (!File.Exists(profilePath))
            {
                result.Errors.Add(string.Format(Strings.Err_ProfilNeObstaja, who, profilePath));
                continue;
            }
            var profile = Deserialize<DeviceProfile>(profilePath, out string? profileError);
            if (profile is null)
            {
                result.Errors.Add($"{profilePath}: {profileError}");
                continue;
            }
            ValidateProfile(profile, dev.Profile, result);
            result.Profiles[dev.Profile] = profile;
        }

        var duplicates = config.Devices.Where(d => d.Enabled).GroupBy(d => d.SlaveId).Where(g => g.Count() > 1);
        foreach (var g in duplicates)
            result.Warnings.Add(string.Format(Strings.Warn_SlaveIdUporabljenVecKrat, g.Key));

        return result;
    }

    private static void ValidateSerial(SerialSettings s, LoadedConfig result)
    {
        if (string.IsNullOrWhiteSpace(s.Port))
            result.Errors.Add(Strings.Err_SerialPortNiNastavljen);
        if (s.Baud < 300)
            result.Errors.Add(string.Format(Strings.Err_SerialBaudNiVeljaven, s.Baud));
        if (s.Parity.ToLowerInvariant() is not ("none" or "even" or "odd"))
            result.Errors.Add(string.Format(Strings.Err_SerialParityMoraBiti, s.Parity));
        if (s.StopBits is not (1 or 2))
            result.Errors.Add(string.Format(Strings.Err_SerialStopBitsMoraBiti, s.StopBits));
        if (s.TimeoutMs < 50)
            result.Errors.Add(string.Format(Strings.Err_SerialTimeoutMoraBiti, s.TimeoutMs));
    }

    private static void ValidateLogging(LoggingSettings l, LoadedConfig result)
    {
        if (string.IsNullOrWhiteSpace(l.Folder))
            result.Errors.Add(Strings.Err_LoggingFolderPrazen);
        if (l.Delimiter.Length != 1)
            result.Errors.Add(string.Format(Strings.Err_LoggingDelimiterEnZnak, l.Delimiter));
        if (l.DecimalSeparator is not ("." or ","))
            result.Errors.Add(string.Format(Strings.Err_LoggingDecimalSep, l.DecimalSeparator));
        if (l.Delimiter == l.DecimalSeparator)
            result.Errors.Add(Strings.Err_LoggingDelimiterEnakDecimal);
    }

    private static void ValidateProfile(DeviceProfile p, string profileName, LoadedConfig result)
    {
        string who = string.Format(Strings.Err_Who_Profil, profileName);

        if (p.PollGroups.Count == 0)
            result.Errors.Add(string.Format(Strings.Err_ProfilNimaPollGroup, who));
        if (p.Registers.Count == 0)
            result.Errors.Add(string.Format(Strings.Err_ProfilNimaRegistra, who));

        foreach (var g in p.PollGroups)
        {
            g.FunctionValue = ParseFunction(g.Function, string.Format(Strings.Err_NeznanaFunkcijaPollGroup, who, g.Function), result);
            if (!TryParseAddress(g.StartAddress, out ushort start))
                result.Errors.Add(string.Format(Strings.Err_NeveljavenStartAddress, who, g.StartAddress));
            g.StartAddressValue = start;
            if (g.Count is < 1 or > 125)
                result.Errors.Add(string.Format(Strings.Err_CountMoraBiti, who, g.Count));
            if (start + g.Count > 0x10000)
                result.Errors.Add(string.Format(Strings.Err_BlokPresegaNaslovniProstor, who, $"{start:X4}", g.Count));
        }

        foreach (var r in p.Registers)
        {
            r.Type = r.Type.ToLowerInvariant();
            r.WordOrder = r.WordOrder.ToLowerInvariant();
            string rwho = string.Format(Strings.Err_Who_Register, who, r.Name);

            r.FunctionValue = ParseFunction(r.Function, string.Format(Strings.Err_NeznanaFunkcijaRegister, rwho, r.Function), result);

            // Konstantni "register" se nikoli ne bere z vodila — brez naslova, tipa ali poll group pokritosti.
            if (r.FunctionValue == ModbusFunction.Constant)
                continue;

            if (!TryParseAddress(r.Address, out ushort addr))
                result.Errors.Add(string.Format(Strings.Err_NeveljavenNaslovProfil, rwho, r.Address));
            r.AddressValue = addr;

            if (r.Type is not ("uint16" or "int16" or "uint32" or "int32" or "float32"))
                result.Errors.Add(string.Format(Strings.Err_NeznanTip, rwho, r.Type));
            if (r.WordOrder is not ("big" or "little"))
                result.Errors.Add(string.Format(Strings.Err_WordOrderMoraBiti, rwho));

            bool covered = p.PollGroups.Any(g =>
                g.FunctionValue == r.FunctionValue &&
                addr >= g.StartAddressValue && addr + r.WordCount <= g.StartAddressValue + g.Count);
            if (!covered)
                result.Errors.Add(string.Format(Strings.Err_NaslovNiPokrit, rwho, r.Address, r.Function));
        }
    }

    private static ModbusFunction ParseFunction(string function, string errorIfUnknown, LoadedConfig result) =>
        function.ToLowerInvariant() switch
        {
            "input" or "04" or "4" or "readinputregisters" => ModbusFunction.ReadInputRegisters,
            "constant" => ModbusFunction.Constant,
            "holding" or "03" or "3" or "readholdingregisters" => ModbusFunction.ReadHoldingRegisters,
            _ => Fail(errorIfUnknown, result),
        };

    private static ModbusFunction Fail(string msg, LoadedConfig result)
    {
        result.Errors.Add(msg);
        return ModbusFunction.ReadInputRegisters;
    }

    /// <summary>
    /// Poišče devices.json: eksplicitna pot, sicer config\devices.json v trenutni mapi,
    /// mapi ob .exe in njunih nadrejenih mapah.
    /// </summary>
    public static string? FindDefaultConfig(string? explicitPath = null)
    {
        if (explicitPath is not null)
            return File.Exists(explicitPath) ? explicitPath : null;

        foreach (string root in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var dir = new DirectoryInfo(root);
            for (int i = 0; i < 6 && dir is not null; i++, dir = dir.Parent)
            {
                string candidate = Path.Combine(dir.FullName, "config", "devices.json");
                if (File.Exists(candidate))
                    return candidate;
            }
        }
        return null;
    }

    /// <summary>
    /// Poišče obstoječo mapo "config" (za brskanje po shranjenih konfiguracijah, tudi če
    /// devices.json še ni bil naložen), sicer privzeto mesto poleg .exe.
    /// </summary>
    public static string ResolveConfigDir()
    {
        foreach (string root in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var dir = new DirectoryInfo(root);
            for (int i = 0; i < 6 && dir is not null; i++, dir = dir.Parent)
            {
                string candidate = Path.Combine(dir.FullName, "config");
                if (Directory.Exists(candidate))
                    return candidate;
            }
        }
        return Path.Combine(AppContext.BaseDirectory, "config");
    }

    /// <summary>Sprejme "0xD100" (hex) ali "53504" (decimalno).</summary>
    public static bool TryParseAddress(string text, out ushort value)
    {
        text = text.Trim();
        return text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? ushort.TryParse(text[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value)
            : ushort.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static T? Deserialize<T>(string path, out string? error) where T : class
    {
        try
        {
            error = null;
            var value = JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOpts);
            if (value is null)
                error = Strings.Err_DatotekaPrazna;
            return value;
        }
        catch (JsonException ex)
        {
            error = string.Format(Strings.Err_NeveljavenJson, ex.LineNumber + 1, ex.Message);
            return null;
        }
        catch (IOException ex)
        {
            error = ex.Message;
            return null;
        }
    }
}
