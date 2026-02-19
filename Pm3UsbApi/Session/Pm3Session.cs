using Pm3UsbApi.Execution;

namespace Pm3UsbApi.Session;

/// <summary>
/// Orchestrates Proxmark3 commands with session state: connection management,
/// T55 command chaining (detect before commands), and optional transcript logging.
/// </summary>
public sealed class Pm3Session : IAsyncDisposable
{
    private readonly IPm3CommandExecutor _executor;
    private readonly Pm3Options _options;
    private StreamWriter? _transcriptWriter;
    private bool _connected;
    private DateTime _lastDetectTime;
    private TimeSpan _detectCacheTtl = TimeSpan.FromSeconds(5);
    private bool _disposed;
    private readonly object _lock = new();

    /// <summary>
    /// Creates a new session with the given executor and options.
    /// </summary>
    public Pm3Session(IPm3CommandExecutor executor, Pm3Options options)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// Connect to the Proxmark3 device by verifying it responds to hw version.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="Pm3ConnectionException">When the device cannot be reached.</exception>
    public async Task ConnectAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var result = await _executor.ExecuteAsync(
            ["hw version"],
            _options.ConnectTimeout,
            ct).ConfigureAwait(false);

        // Consider connected if we got a device response, even with firmware-mismatch warnings ([!])
        var hasDeviceResponse = result.RawOutput.Contains("Proxmark3", StringComparison.OrdinalIgnoreCase)
            && (result.RawOutput.Contains("RDV4") || result.RawOutput.Contains("Device") || result.RawOutput.Contains("Bootrom") || result.RawOutput.Contains("AT91SAM"));

        if (!hasDeviceResponse && (result.HasErrors || result.ExitCode != 0))
        {
            throw new Pm3ConnectionException(
                $"Failed to connect to Proxmark3. {result.ErrorSummary ?? "Unknown error"}",
                result);
        }

        lock (_lock)
        {
            _connected = true;
        }

        LogTranscript(">>> hw version");
        LogTranscript("<<< " + result.RawOutput);

        // Version info is useful for diagnostics; caller can inspect CommandResult if needed
    }

    /// <summary>
    /// Disconnect from the device and dispose the executor.
    /// </summary>
    public async Task DisconnectAsync()
    {
        if (_disposed) return;

        lock (_lock)
        {
            _connected = false;
        }

        CloseTranscript();

        await _executor.DisposeAsync().ConfigureAwait(false);

        lock (_lock)
        {
            _disposed = true;
        }
    }

    /// <summary>
    /// Returns whether the session is connected. If connected, optionally pings the device
    /// to verify it still responds.
    /// </summary>
    public async Task<bool> IsConnectedAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_lock)
        {
            if (!_connected)
                return false;
        }

        // Quick hw version ping to verify device still responds
        try
        {
            var result = await _executor.ExecuteAsync(
                ["hw version"],
                TimeSpan.FromSeconds(5),
                ct).ConfigureAwait(false);

            // Use same lenient check as ConnectAsync: device responded even with firmware warnings
            var hasDeviceResponse = result.RawOutput.Contains("Proxmark3", StringComparison.OrdinalIgnoreCase)
                && (result.RawOutput.Contains("RDV4") || result.RawOutput.Contains("Device") || result.RawOutput.Contains("Bootrom") || result.RawOutput.Contains("AT91SAM"));
            return hasDeviceResponse || (!result.HasErrors && result.ExitCode == 0);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Execute a T55 command, chaining lf t55 detect before it.
    /// Use for commands like lf t55 read, lf t55 write, lf t55 dump.
    /// </summary>
    public async Task<CommandResult> ExecuteT55CommandAsync(
        string command,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var commands = new[] { "lf t55 detect", command };
        LogTranscript($">>> {string.Join("; ", commands)}");

        var result = await _executor.ExecuteAsync(
            commands,
            timeout ?? _options.DefaultCommandTimeout,
            ct).ConfigureAwait(false);

        LogTranscript("<<< " + result.RawOutput);

        _lastDetectTime = DateTime.UtcNow; // For future interactive-mode cache optimization

        return result;
    }

    /// <summary>
    /// Execute a non-T55 command (e.g., hw version, lf tune) without chaining detect.
    /// </summary>
    public async Task<CommandResult> ExecuteCommandAsync(
        string command,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        LogTranscript($">>> {command}");

        var result = await _executor.ExecuteAsync(
            [command],
            timeout ?? _options.DefaultCommandTimeout,
            ct).ConfigureAwait(false);

        LogTranscript("<<< " + result.RawOutput);

        return result;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    private void LogTranscript(string line)
    {
        if (!_options.EnableTranscriptLogging) return;

        var path = _options.TranscriptPath ?? Path.Combine(
            Path.GetTempPath(),
            $"pm3-transcript-{DateTime.UtcNow:yyyyMMdd}-{Environment.ProcessId}.log");

        lock (_lock)
        {
            try
            {
                _transcriptWriter ??= OpenTranscriptWriter(path);
                var ts = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff");
                _transcriptWriter.WriteLine($"[{ts}] {line}");
                _transcriptWriter.Flush();
            }
            catch
            {
                // Best effort; don't fail the command for logging errors
            }
        }
    }

    private static StreamWriter OpenTranscriptWriter(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
        return new StreamWriter(fs) { AutoFlush = true };
    }

    private void CloseTranscript()
    {
        lock (_lock)
        {
            _transcriptWriter?.Dispose();
            _transcriptWriter = null;
        }
    }
}
