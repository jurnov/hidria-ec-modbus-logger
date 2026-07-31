using NModbus.IO;

namespace ModbusLogger.Core;

public enum TrafficDirection { Sent, Received }

/// <summary>Surov paket bajtov, kot ga NModbus dejansko pošlje/prejme na serijskem portu.</summary>
public sealed record TrafficEntry(DateTime Time, TrafficDirection Direction, byte[] Bytes);

/// <summary>
/// En pomerjen Modbus RTU zahtevek in nanj prejet odgovor (ali brez odgovora ob timeoutu).
/// ResponseTime je čas zadnjega prejetega kosa odgovora (ko je bil ta v celoti sestavljen).
/// </summary>
public sealed record TrafficExchange(DateTime Time, byte[] Request, byte[]? Response, DateTime? ResponseTime);

/// <summary>
/// Ovije obstoječ IStreamResource (serijski port) in prestreže surove bajte vsakega
/// branja/pisanja, ne da bi spremenil dejansko komunikacijo — NModbus dela ves I/O
/// izključno prek te povezave, zato tu vidimo natanko to, kar gre po žici.
/// </summary>
public sealed class LoggingStreamResource : IStreamResource
{
    private readonly IStreamResource _inner;

    public LoggingStreamResource(IStreamResource inner) => _inner = inner;

    public event Action<TrafficEntry>? TrafficCaptured;

    public int InfiniteTimeout => _inner.InfiniteTimeout;
    public int ReadTimeout { get => _inner.ReadTimeout; set => _inner.ReadTimeout = value; }
    public int WriteTimeout { get => _inner.WriteTimeout; set => _inner.WriteTimeout = value; }

    public void DiscardInBuffer() => _inner.DiscardInBuffer();

    public int Read(byte[] buffer, int offset, int count)
    {
        int read = _inner.Read(buffer, offset, count);
        if (read > 0)
        {
            var copy = new byte[read];
            Array.Copy(buffer, offset, copy, 0, read);
            TrafficCaptured?.Invoke(new TrafficEntry(DateTime.Now, TrafficDirection.Received, copy));
        }
        return read;
    }

    public void Write(byte[] buffer, int offset, int count)
    {
        var copy = new byte[count];
        Array.Copy(buffer, offset, copy, 0, count);
        TrafficCaptured?.Invoke(new TrafficEntry(DateTime.Now, TrafficDirection.Sent, copy));
        _inner.Write(buffer, offset, count);
    }

    public void Dispose() => _inner.Dispose();
}

/// <summary>
/// Združuje surove Sent/Received dogodke v pare zahteva-odgovor. En Write = en zahtevek;
/// vsi Read-i do naslednjega Write-a sestavijo njegov odgovor (NModbus lahko odgovor
/// prebere v več kosih). Brez odgovora (timeout) se to vidi kot Response == null.
/// </summary>
public sealed class TrafficLog
{
    private byte[]? _pendingRequest;
    private DateTime _pendingTime;
    private DateTime? _pendingResponseTime;
    private readonly List<byte> _pendingResponse = new();

    public event Action<TrafficExchange>? ExchangeCompleted;

    public void OnTraffic(TrafficEntry entry)
    {
        if (entry.Direction == TrafficDirection.Sent)
        {
            Flush();
            _pendingRequest = entry.Bytes;
            _pendingTime = entry.Time;
        }
        else
        {
            _pendingResponse.AddRange(entry.Bytes);
            _pendingResponseTime = entry.Time;   // čas zadnjega (torej dokončnega) prejetega kosa
        }
    }

    private void Flush()
    {
        if (_pendingRequest is null)
            return;
        ExchangeCompleted?.Invoke(new TrafficExchange(
            _pendingTime, _pendingRequest,
            _pendingResponse.Count > 0 ? _pendingResponse.ToArray() : null,
            _pendingResponseTime));
        _pendingRequest = null;
        _pendingResponseTime = null;
        _pendingResponse.Clear();
    }
}
