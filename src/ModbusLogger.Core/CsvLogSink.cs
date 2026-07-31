using System.Globalization;
using System.Text;

namespace ModbusLogger.Core;

/// <summary>Cilj za zapisovanje meritev; kasneje lahko dodamo npr. SQLite ali SQL brez sprememb pollerja.</summary>
public interface ILogSink
{
    void Write(DeviceEntry device, DeviceProfile profile, DeviceReadResult result);
}

/// <summary>
/// Zapisuje meritve v CSV: ena datoteka na napravo na dan (label_yyyy-MM-dd.csv).
/// Ob komunikacijski napaki zapiše vrstico s statusom napake in praznimi vrednostmi,
/// da so izpadi vidni tudi v podatkih.
/// </summary>
public sealed class CsvLogSink : ILogSink
{
    private readonly LoggingSettings _settings;
    private readonly string _folder;

    public string Folder => _folder;

    public CsvLogSink(LoggingSettings settings, string baseDir)
    {
        _settings = settings;
        _folder = Path.IsPathRooted(settings.Folder) ? settings.Folder : Path.Combine(baseDir, settings.Folder);
        Directory.CreateDirectory(_folder);
    }

    public void Write(DeviceEntry device, DeviceProfile profile, DeviceReadResult result)
    {
        string path = Path.Combine(_folder, $"{Sanitize(device.Label)}_{result.Timestamp:yyyy-MM-dd}.csv");
        char d = _settings.Delimiter[0];

        if (!File.Exists(path))
        {
            var header = new StringBuilder();
            header.Append("timestamp").Append(d).Append("status").Append(d).Append("response_ms");
            foreach (var reg in profile.Registers)
            {
                header.Append(d).Append(Escape(string.IsNullOrEmpty(reg.Unit) ? reg.Name : $"{reg.Name} [{reg.Unit}]", d));
            }
            header.AppendLine();
            // BOM, da Excel pravilno prikaže °C in šumnike
            File.WriteAllText(path, header.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        }

        var line = new StringBuilder();
        line.Append(result.Timestamp.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        line.Append(d).Append(result.Success ? "OK" : Escape(result.Error ?? "napaka", d));
        line.Append(d).Append(result.ElapsedMs);

        if (result.Success)
        {
            foreach (var r in result.Readings)
                line.Append(d).Append(FormatValue(r.Value));
        }
        else
        {
            line.Append(new string(d, profile.Registers.Count));
        }

        line.AppendLine();
        File.AppendAllText(path, line.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private string FormatValue(double value)
    {
        string text = value.ToString(value == Math.Floor(value) ? "0" : "0.######", CultureInfo.InvariantCulture);
        return _settings.DecimalSeparator == "," ? text.Replace('.', ',') : text;
    }

    private static string Escape(string text, char delimiter) =>
        text.IndexOfAny(new[] { delimiter, '"', '\n', '\r' }) >= 0
            ? $"\"{text.Replace("\"", "\"\"")}\""
            : text;

    private static string Sanitize(string label)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(label.Length);
        foreach (char c in label)
            sb.Append(invalid.Contains(c) || c == ' ' ? '_' : c);
        return sb.Length == 0 ? "naprava" : sb.ToString();
    }
}
