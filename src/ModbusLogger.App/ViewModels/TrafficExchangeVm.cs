using ModbusLogger.Core;

namespace ModbusLogger.App.ViewModels;

/// <summary>En prikazan Modbus RTU zahtevek/odgovor v pregledovalniku prometka (HEX).</summary>
public sealed class TrafficExchangeVm
{
    public TrafficExchangeVm(TrafficExchange exchange)
    {
        TimeText = exchange.Time.ToString("HH:mm:ss.fff");
        Request = FormatHex(exchange.Request);
        HasResponse = exchange.Response is not null;
        Response = exchange.Response is not null ? FormatHex(exchange.Response) : "(brez odgovora — timeout)";
        ResponseTimeText = exchange.ResponseTime is { } t ? t.ToString("HH:mm:ss.fff") : "—";
    }

    public string TimeText { get; }
    public string Request { get; }
    public string Response { get; }
    public string ResponseTimeText { get; }
    public bool HasResponse { get; }

    private static string FormatHex(byte[] bytes) => string.Join(" ", bytes.Select(b => b.ToString("X2")));
}
