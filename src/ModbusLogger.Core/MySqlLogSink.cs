using MySqlConnector;

namespace ModbusLogger.Core;

/// <summary>
/// Zapisuje meritve v obstoječo MySQL tabelo: en INSERT na napravo na cikel branja (enaka
/// zrnatost kot CsvLogSink), le v stolpce, ki jih je uporabnik povezal s podatkom v
/// nastavitvah beleženja. Stolpci, ki nimajo povezanega vira ali katerih vir za trenutno
/// napravo ni na voljo (npr. register z drugim imenom), se v INSERT preprosto izpustijo.
/// </summary>
public sealed class MySqlLogSink : ILogSink, IDisposable
{
    /// <summary>Fiksni ključi virov podatkov, neodvisni od profila naprave.</summary>
    public const string FieldTimestamp = "__timestamp__";
    public const string FieldDevice = "__device__";
    public const string FieldSlaveId = "__slaveid__";
    public const string FieldStatus = "__status__";
    public const string FieldResponseMs = "__response_ms__";

    /// <summary>1 = napaka komunikacije pri tem branju, 0 = uspešno branje.</summary>
    public const string FieldErrorFlag = "__error_flag__";

    /// <summary>Predpona za ključ vira, ki pomeni "vrednost registra s tem imenom".</summary>
    public const string RegisterFieldPrefix = "reg:";

    /// <summary>Predpona za ključ vira, ki pomeni "fiksna besedilna vrednost" (sledi ji dejanska vrednost).</summary>
    public const string ConstantFieldPrefix = "const:";

    private readonly MySqlSettings _settings;
    private readonly MySqlConnection _connection;

    /// <summary>Diagnostična sporočila, ki niso napake (npr. vrstica izpuščena, ker se noben stolpec ni ujel).</summary>
    public event Action<string>? Diagnostic;

    public MySqlLogSink(MySqlSettings settings)
    {
        _settings = settings;
        _connection = new MySqlConnection(BuildConnectionString(settings));
        _connection.Open();
    }

    public static string BuildConnectionString(MySqlSettings s) =>
        $"Server={s.Host};Port={s.Port};Database={s.Database};User ID={s.User};Password={s.Password};";

    /// <summary>Prebere imena stolpcev obstoječe tabele (za izbirnik povezovanja v vmesniku).</summary>
    public static List<string> FetchColumns(MySqlSettings s)
    {
        using var connection = new MySqlConnection(BuildConnectionString(s));
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS " +
                           "WHERE TABLE_SCHEMA = @db AND TABLE_NAME = @t ORDER BY ORDINAL_POSITION";
        cmd.Parameters.AddWithValue("@db", s.Database);
        cmd.Parameters.AddWithValue("@t", s.Table);

        var columns = new List<string>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            columns.Add(reader.GetString(0));
        return columns;
    }

    public void Write(DeviceEntry device, DeviceProfile profile, DeviceReadResult result)
    {
        if (_connection.State != System.Data.ConnectionState.Open)
            _connection.Open();

        if (_settings.ColumnMapping.Count == 0)
        {
            Diagnostic?.Invoke("MySQL: v nastavitvah beleženja ni povezanega nobenega stolpca — nič se ne zapisuje. Klikni 'Preberi stolpce' in poveži vsaj enega.");
            return;
        }

        var values = BuildFieldValues(device, result);
        var (sql, parameters) = BuildInsert(_settings.Table, _settings.ColumnMapping, values);
        if (sql is null)
        {
            Diagnostic?.Invoke($"MySQL: naprava '{device.Label}' — noben povezan stolpec trenutno nima podatka (npr. register z drugim imenom ali neuspešno branje) — vrstica izpuščena.");
            return;
        }

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Sestavi INSERT stavek iz mapiranja stolpec->ključ in razpoložljivih vrednosti za trenutni
    /// zapis. Stolpci brez mapiranja ali katerih ključ trenutno ni na voljo (npr. drug profil)
    /// se izpustijo. Ključi s predpono "const:" pomenijo fiksno vrednost, ki ne pride iz naprave
    /// in je zato na voljo za vsako vrstico ne glede na to, katera naprava jo piše.
    /// Vrne (null, ...) če noben stolpec ni na voljo za vpis.
    /// </summary>
    public static (string? Sql, List<(string Name, object? Value)> Parameters) BuildInsert(
        string table, IReadOnlyDictionary<string, string> columnMapping, IReadOnlyDictionary<string, object?> values)
    {
        var columns = new List<string>();
        var parameters = new List<(string, object?)>();
        int i = 0;
        foreach (var (column, fieldKey) in columnMapping)
        {
            if (string.IsNullOrEmpty(fieldKey))
                continue;

            object? value;
            if (fieldKey.StartsWith(ConstantFieldPrefix, StringComparison.Ordinal))
                value = fieldKey[ConstantFieldPrefix.Length..];
            else if (!values.TryGetValue(fieldKey, out value))
                continue;

            string p = "@p" + i++;
            columns.Add($"`{column}`");
            parameters.Add((p, value));
        }

        if (columns.Count == 0)
            return (null, parameters);

        string sql = $"INSERT INTO `{table}` ({string.Join(", ", columns)}) VALUES ({string.Join(", ", parameters.Select(p => p.Item1))})";
        return (sql, parameters);
    }

    /// <summary>Razpoložljivi podatki enega branja ene naprave, ključani po virih iz ColumnMapping.</summary>
    public static Dictionary<string, object?> BuildFieldValues(DeviceEntry device, DeviceReadResult result)
    {
        var values = new Dictionary<string, object?>
        {
            [FieldTimestamp] = result.Timestamp,
            [FieldDevice] = device.Label,
            [FieldSlaveId] = (int)device.SlaveId,
            [FieldStatus] = result.Success ? "OK" : (result.Error ?? "napaka"),
            [FieldResponseMs] = result.ElapsedMs,
            [FieldErrorFlag] = result.Success ? 0 : 1,
        };

        if (result.Success)
            foreach (var reading in result.Readings)
                values[RegisterFieldPrefix + reading.Def.Name] = reading.Value;

        return values;
    }

    public void Dispose() => _connection.Dispose();
}
