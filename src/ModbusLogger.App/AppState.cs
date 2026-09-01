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
}
