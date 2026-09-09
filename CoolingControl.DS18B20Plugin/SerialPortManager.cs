namespace CoolingControl.DS18B20Plugin;

using System.Collections.Concurrent;
using System.IO.Ports;
using Serilog;

/// <summary>
/// Owns a single serial port connected to a DS18B20 USB adapter. Continuously reads
/// ASCII lines (e.g. <c>t1=+28.70</c>) on a background thread and keeps the latest value
/// per sensor tag. Automatically reconnects if the port is unplugged or fails to open.
/// </summary>
public sealed class SerialPortManager : IDisposable
{
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(5);
    private const int ReadTimeoutMs = 2000;
    private const int BaudRate = 9600;

    private readonly string _portName;
    private readonly ConcurrentDictionary<string, (float Value, DateTime TimestampUtc)> _readings = new();
    private CancellationTokenSource? _cts;
    private Thread? _readerThread;
    private SerialPort? _port;
    private bool _disposed;

    public SerialPortManager(string portName)
    {
        _portName = portName;
    }

    /// <summary>Starts the background read/reconnect loop. Safe to call after <see cref="Stop"/>.</summary>
    public void Start()
    {
        if (_readerThread is { IsAlive: true })
            return;

        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _readerThread = new Thread(() => ReadLoop(token))
        {
            IsBackground = true,
            Name = $"DS18B20-{_portName}"
        };
        _readerThread.Start();
    }

    /// <summary>Stops the read loop and closes the port. Readings remain cached until values go stale.</summary>
    public void Stop()
    {
        _cts?.Cancel();
        _readerThread?.Join(TimeSpan.FromSeconds(2));
        _readerThread = null;
        ClosePort();
        _cts?.Dispose();
        _cts = null;
    }

    /// <summary>
    /// Returns the last known value for <paramref name="tag"/> if it was received within
    /// <paramref name="maxAge"/>; otherwise <c>null</c>.
    /// </summary>
    public float? TryGetValue(string tag, TimeSpan maxAge)
    {
        if (!_readings.TryGetValue(tag, out var reading))
            return null;

        return DateTime.UtcNow - reading.TimestampUtc <= maxAge ? reading.Value : null;
    }

    /// <summary>Returns all currently cached tags and their last values, for diagnostics/logging.</summary>
    public IReadOnlyDictionary<string, (float Value, DateTime TimestampUtc)> Snapshot() =>
        _readings.ToDictionary(kv => kv.Key, kv => kv.Value);

    private void ReadLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            if (_port is not { IsOpen: true })
            {
                if (!TryOpenPort())
                {
                    if (token.WaitHandle.WaitOne(ReconnectDelay))
                        return;
                    continue;
                }
            }

            try
            {
                var line = _port!.ReadLine();
                if (LineParser.TryParse(line, out var tag, out var value))
                    _readings[tag] = (value, DateTime.UtcNow);
                else
                    Log.Debug("DS18B20 [{Port}]: ignoring unrecognized line '{Line}'", _portName, line);
            }
            catch (TimeoutException)
            {
                // No data within the read timeout — normal, just loop and check cancellation.
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                Log.Warning(ex, "DS18B20 [{Port}]: serial port error, will retry in {Delay}s", _portName, ReconnectDelay.TotalSeconds);
                ClosePort();
                token.WaitHandle.WaitOne(ReconnectDelay);
            }
        }
    }

    private bool TryOpenPort()
    {
        try
        {
            _port = new SerialPort(_portName, BaudRate, Parity.None, 8, StopBits.One)
            {
                ReadTimeout = ReadTimeoutMs,
                NewLine = "\n\r"
            };
            _port.Open();
            Log.Information("DS18B20 [{Port}]: opened at {Baud} 8N1", _portName, BaudRate);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Log.Error(ex, "DS18B20 [{Port}]: failed to open, will retry in {Delay}s", _portName, ReconnectDelay.TotalSeconds);
            _port = null;
            return false;
        }
    }

    private void ClosePort()
    {
        try
        {
            if (_port is { IsOpen: true })
                _port.Close();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "DS18B20 [{Port}]: error closing port", _portName);
        }
        finally
        {
            _port?.Dispose();
            _port = null;
        }
    }

    public void Dispose()
    {
        if (_disposed) 
            return;
        _disposed = true;
        Stop();
        GC.SuppressFinalize(this);
    }
}
