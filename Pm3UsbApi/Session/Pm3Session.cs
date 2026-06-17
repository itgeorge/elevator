using Pm3UsbApi.Commands;
using Pm3UsbApi.Diagnostics;
using Pm3UsbApi.Execution;
using Pm3UsbApi.Parsers;
using System.Diagnostics;

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
    private string? _discoveredPort;
    private readonly Pm3T55DetectCache _detectCache = new();
    private bool _disposed;
    private readonly object _lock = new();

    /// <summary>
    /// Creates a new session with the given executor and options.
    /// </summary>
    public Pm3Session(IPm3CommandExecutor executor, Pm3Options options)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        Pm3DiagnosticLog.EnsureInitialized();
    }

    /// <summary>
    /// Connect to the Proxmark3 device by verifying it responds to hw version.
    /// Uses Pm3Options.AutoConnect and DevicePort; see options for port/auto discovery behavior.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="Pm3ConnectionException">When the device cannot be reached.</exception>
    public async Task ConnectAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var log = Pm3DiagnosticLog.Current;

        try
        {
            string? portOverride = null;

            if (string.IsNullOrWhiteSpace(_options.DevicePort))
            {
                if (!_options.AutoConnect)
                {
                    throw new Pm3ConnectionException(
                        "DevicePort must be set when autoConnect is false. Use Pm3Options.DevicePort or config to set the port. " +
                        "Run 'pm3 --list' (or use PortDiscovery.ListPortsAsync) to discover available ports.");
                }

                portOverride = await PortDiscovery.DiscoverFirstPortAsync(_options.Pm3ClientPath, ct).ConfigureAwait(false);
                if (string.IsNullOrEmpty(portOverride))
                {
                    throw new Pm3ConnectionException(
                        "No Proxmark3 device found. Connect the device via USB and ensure no other process is using it. " +
                        "Run 'pm3 --list' to verify detection.");
                }
                _discoveredPort = portOverride;
            }

            log.WriteSession($"connect start executor={_options.ExecutorKind} port={portOverride ?? _options.DevicePort ?? "auto"}");

            var result = await ExecuteAsync(
                [new HwVersionCommand()],
                _options.ConnectTimeout,
                ct,
                portOverride).ConfigureAwait(false);

            if (OutputParser.DetectOfflineMode(result.OutputLines))
            {
                throw new Pm3ConnectionException(
                    "Proxmark3 is in offline mode. Connect the device via USB and ensure no other process is using it.",
                    result);
            }

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

            log.WriteSession($"connect ok port={portOverride ?? _options.DevicePort ?? _discoveredPort}");
        }
        catch (Exception ex)
        {
            log.WriteError("connect failed", ex);
            throw;
        }
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
            _discoveredPort = null;
        }

        CloseTranscript();
        _detectCache.InvalidateForDisconnect();
        Pm3DiagnosticLog.Current.WriteSession("disconnect");

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
            var port = _options.DevicePort ?? _discoveredPort;
            var result = await ExecuteAsync(
                [new HwVersionCommand()],
                TimeSpan.FromSeconds(5),
                ct,
                port).ConfigureAwait(false);

            if (OutputParser.DetectOfflineMode(result.OutputLines))
                return false;

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
    /// Clears the T55 detect cache so the next T55 operation re-runs detect.
    /// </summary>
    public void InvalidateT55DetectCache() => _detectCache.Invalidate();

    /// <summary>
    /// Execute a T55 command, chaining detect before it when cache is cold.
    /// Use for commands like T55 read, T55 write, T55 dump.
    /// </summary>
    public async Task<CommandResult> ExecuteT55Async(
        IPm3DeviceCommand command,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        var port = _options.DevicePort ?? _discoveredPort;
        var now = DateTime.UtcNow;
        var skippedDetect = _detectCache.ShouldSkipDetect(_options.ExecutorKind, port, command, now);
        var commands = Pm3T55DetectCache.BuildT55CommandBatch(
            _detectCache,
            _options.ExecutorKind,
            port,
            command,
            now);

        if (skippedDetect)
            Pm3DiagnosticLog.Current.WriteSession("T55 detect cache hit; skipping detect");

        var result = await ExecuteAsync(commands, timeout, ct).ConfigureAwait(false);
        ApplyT55CacheAfterFollowOn(command, skippedDetect, result);
        return result;
    }

    /// <summary>
    /// Execute one or more device commands without T55 detect chaining.
    /// </summary>
    public async Task<CommandResult> ExecuteAsync(
        IReadOnlyList<IPm3DeviceCommand> commands,
        TimeSpan? timeout = null,
        CancellationToken ct = default,
        string? portOverride = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        CommandBatchValidator.Validate(commands);
        if (commands.Any(c => c is LfTuneCommand))
            _detectCache.InvalidateForLfTune();

        var batch = Pm3CliFormatter.FormatBatch(commands);
        LogTranscript($">>> {batch}");
        Pm3DiagnosticLog.Current.WriteSession($">>> {batch}");

        var port = portOverride ?? _options.DevicePort ?? _discoveredPort;
        var sw = Stopwatch.StartNew();
        try
        {
            var result = await _executor.ExecuteAsync(
                commands,
                timeout ?? _options.DefaultCommandTimeout,
                ct,
                port).ConfigureAwait(false);

            LogTranscript("<<< " + result.RawOutput);
            var summary = result.HasErrors
                ? $"<<< FAIL exit={result.ExitCode} {result.ErrorSummary}"
                : $"<<< OK exit={result.ExitCode}";
            Pm3DiagnosticLog.Current.WriteSession($"{summary} ({sw.ElapsedMilliseconds}ms)");
            if (result.HasErrors)
                Pm3DiagnosticLog.Current.WriteError($"command batch failed: {batch} — {result.ErrorSummary}");

            var now = DateTime.UtcNow;
            if (commands.Any(c => c is T55DetectCommand) && !result.HasErrors)
                _detectCache.TryRecordFromBatchResult(_options.ExecutorKind, port, commands, result, now);

            if (commands.Any(c => c is T55WriteBlockCommand) && !result.HasErrors)
                _detectCache.InvalidateForWrite();

            return result;
        }
        catch (Exception ex)
        {
            Pm3DiagnosticLog.Current.WriteError($"command batch exception: {batch} ({sw.ElapsedMilliseconds}ms)", ex);
            throw;
        }
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

    private void ApplyT55CacheAfterFollowOn(
        IPm3DeviceCommand followOn,
        bool skippedDetect,
        CommandResult result)
    {
        if (!skippedDetect || followOn is not T55ReadBlockCommand read)
            return;

        if (result.HasErrors)
        {
            _detectCache.InvalidateForReadFailure();
            return;
        }

        if (read.Block != 0)
            return;

        var parsed = BlockReadParser.Parse(result, 0);
        if (parsed.Success && parsed.HexData is not null)
            _detectCache.InvalidateForBlock0Mismatch(parsed.HexData);
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

