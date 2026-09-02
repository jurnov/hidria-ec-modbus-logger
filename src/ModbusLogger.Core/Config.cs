using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModbusLogger.Core;

/// <summary>Vsebina devices.json: serijska povezava + seznam naprav na RS485 liniji.</summary>
public sealed class AppConfig
{
    /// <summary>serial (RS-485/RS-232 prek COM porta) | tcp (Modbus TCP prek IP naslova)</summary>
    public string ConnectionType { get; set; } = "serial";

    public SerialSettings Serial { get; set; } = new();
    public TcpSettings Tcp { get; set; } = new();

    /// <summary>Kako pogosto aplikacija dejansko komunicira z napravami (bere registre).</summary>
    public int SampleIntervalSeconds { get; set; } = 5;

    /// <summary>Kako pogosto se zadnji prebrani vzorec zapiše v CSV/MySQL; >= SampleIntervalSeconds je smiselno.</summary>
    public int WriteIntervalSeconds { get; set; } = 60;

    public LoggingSettings Logging { get; set; } = new();
    public MySqlSettings MySql { get; set; } = new();
    public List<DeviceEntry> Devices { get; set; } = new();
}

public sealed class LoggingSettings
{
    /// <summary>Ali se meritve zapisujejo v CSV; false = samo prikaz v vmesniku.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Mapa za CSV datoteke; relativna pot se razreši glede na mapo devices.json.</summary>
    public string Folder { get; set; } = "logs";

    /// <summary>Ločilo stolpcev; ";" je privzeto, ker ga slovenski Excel odpre pravilno.</summary>
    public string Delimiter { get; set; } = ";";

    /// <summary>Decimalno ločilo: "," ali "."</summary>
    public string DecimalSeparator { get; set; } = ",";
}

public sealed class SerialSettings
{
    public string Port { get; set; } = "COM3";
    public int Baud { get; set; } = 19200;

    /// <summary>none | even | odd</summary>
    public string Parity { get; set; } = "even";

    public int StopBits { get; set; } = 1;
    public int TimeoutMs { get; set; } = 1000;
    public int Retries { get; set; } = 2;
}

public sealed class TcpSettings
{
    public string Host { get; set; } = "192.168.1.100";
    public int Port { get; set; } = 502;
    public int TimeoutMs { get; set; } = 1000;
    public int Retries { get; set; } = 2;
}

/// <summary>Nastavitve za dodatno beleženje v MySQL, neodvisno od CSV (oboje je lahko omogočeno hkrati).</summary>
public sealed class MySqlSettings
{
    public bool Enabled { get; set; } = false;
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 3306;
    public string Database { get; set; } = "";
    public string User { get; set; } = "";
    public string Password { get; set; } = "";
    public string Table { get; set; } = "";

    /// <summary>Ime stolpca v tabeli -> ključ vira podatka (glej MySqlLogSink.BuildFieldValues).</summary>
    public Dictionary<string, string> ColumnMapping { get; set; } = new();
}

public sealed class DeviceEntry
{
    public byte SlaveId { get; set; }

    /// <summary>Ime profila = ime datoteke v mapi profiles/ brez končnice .json.</summary>
    public string Profile { get; set; } = "";

    /// <summary>Prikazno ime naprave, npr. "Ventilator strop 1".</summary>
    public string Label { get; set; } = "";

    public bool Enabled { get; set; } = true;
}

/// <summary>Profil naprave (profiles/*.json): kaj brati in kako interpretirati.</summary>
public sealed class DeviceProfile
{
    public string Name { get; set; } = "";
    public List<PollGroup> PollGroups { get; set; } = new();
    public List<RegisterDef> Registers { get; set; } = new();
}

/// <summary>En strnjen blok registrov, prebran z enim Modbus paketom.</summary>
public sealed class PollGroup
{
    /// <summary>input (fc 04) | holding (fc 03)</summary>
    public string Function { get; set; } = "input";

    [JsonConverter(typeof(FlexibleNumberStringConverter))]
    public string StartAddress { get; set; } = "0";

    public ushort Count { get; set; }

    [JsonIgnore] public ushort StartAddressValue { get; internal set; }
    [JsonIgnore] public ModbusFunction FunctionValue { get; internal set; }
}

public enum ModbusFunction
{
    ReadInputRegisters,
    ReadHoldingRegisters,

    /// <summary>Ni pravi Modbus register — fiksna vrednost, vpisana v profilu, ki se nikoli ne bere z vodila.</summary>
    Constant,
}

public sealed class RegisterDef
{
    [JsonConverter(typeof(FlexibleNumberStringConverter))]
    public string Address { get; set; } = "0";

    public string Name { get; set; } = "";
    public string Unit { get; set; } = "";

    /// <summary>uint16 | int16 | uint32 | int32 | float32</summary>
    public string Type { get; set; } = "uint16";

    public double Scale { get; set; } = 1.0;
    public double Offset { get; set; } = 0.0;

    /// <summary>Vrstni red besed pri 32-bitnih tipih: big (višja beseda prva) | little.</summary>
    public string WordOrder { get; set; } = "big";

    /// <summary>input (fc 04) | holding (fc 03) | constant — register lahko pripada drugi funkciji kot drugi v istem profilu.</summary>
    public string Function { get; set; } = "input";

    /// <summary>Vrednost, kadar je Function="constant" — se nikoli ne bere z vodila, uporabnik jo vpiše v profilu.</summary>
    public double ConstantValue { get; set; } = 0;

    [JsonIgnore] public ushort AddressValue { get; internal set; }
    [JsonIgnore] public ModbusFunction FunctionValue { get; internal set; }

    [JsonIgnore]
    public int WordCount => Type.ToLowerInvariant() switch
    {
        "uint32" or "int32" or "float32" => 2,
        _ => 1,
    };
}

/// <summary>Sprejme JSON število ali niz ("0xD100", "53504", 53504) in ga vrne kot niz.</summary>
public sealed class FlexibleNumberStringConverter : JsonConverter<string>
{
    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.Number => reader.GetUInt32().ToString(),
            JsonTokenType.String => reader.GetString() ?? "0",
            _ => throw new JsonException("pričakovan niz ali število"),
        };

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value);
}
