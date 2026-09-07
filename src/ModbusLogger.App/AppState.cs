using System.IO;

namespace ModbusLogger.App;

/// <summary>
/// Zapomni si pot do nazadnje naložene konfiguracije med zagoni aplikacije, ločeno od
/// samih konfiguracijskih datotek (shranjeno v uporabniškem profilu, ne v config\).
/// </summary>
internal static class AppState
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ModbusLogger", "last-config.txt");

    private static readonly string LanguageFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ModbusLogger", "language.txt");

    public static string? LoadLastConfigPath()
    {
        try
        {
            string path = File.ReadAllText(FilePath).Trim();
            return path.Length > 0 && File.Exists(path) ? path : null;
        }
        catch
        {
            return null;
        }
    }

    public static void SaveLastConfigPath(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, path);
        }
        catch
        {
            // ni kritično, če si aplikacija ne zapomni zadnje konfiguracije
        }
    }

    /// <summary>Dvočrkovna koda kulture (sl/en/de/it/es) izbranega jezika vmesnika, ali null, če še ni izbran.</summary>
    public static string? LoadLanguage()
    {
        try
        {
            string code = File.ReadAllText(LanguageFilePath).Trim();
            return code.Length > 0 ? code : null;
        }
        catch
        {
            return null;
        }
    }

    public static void SaveLanguage(string cultureCode)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LanguageFilePath)!);
            File.WriteAllText(LanguageFilePath, cultureCode);
        }
        catch
        {
            // ni kritično, če si aplikacija ne zapomni izbranega jezika
        }
    }
}
