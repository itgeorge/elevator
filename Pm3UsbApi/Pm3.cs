using Pm3UsbApi.Commands;
using Pm3UsbApi.Execution;
using Pm3UsbApi.Parsers;
using Pm3UsbApi.Session;
using Tokens;

namespace Pm3UsbApi;

/// <summary>
/// High-level Proxmark3 API for T55xx tag operations, LF tuning, and raw commands.
/// </summary>
public sealed class Pm3 : IAsyncDisposable
{
    private readonly Pm3Session _session;
    private CommandResult? _lastTuneResult;

    /// <summary>
    /// Creates a new Pm3 instance with the given options.
    /// </summary>
    /// <param name="options">Configuration. Uses sensible defaults if null.</param>
    public Pm3(Pm3Options? options = null)
    {
        var opts = options ?? new Pm3Options();
        IPm3CommandExecutor executor = opts.ExecutorKind switch
        {
            Pm3ExecutorKind.Native => new Native.Pm3NativeExecutor(opts),
            _ => new Pm3ProcessExecutor(opts),
        };
        _session = new Pm3Session(executor, opts);
    }

    /// <summary>
    /// Connect to the Proxmark3 device. Uses Pm3Options for port/auto-discovery.
    /// </summary>
    /// <returns>True on success.</returns>
    /// <exception cref="Pm3ConnectionException">When the device cannot be reached.</exception>
    public async Task<bool> ConnectAsync(CancellationToken ct = default)
    {
        await _session.ConnectAsync(ct).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Disconnect from the device and release resources.
    /// </summary>
    public async Task<bool> DisconnectAsync(CancellationToken ct = default)
    {
        await _session.DisconnectAsync().ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Returns whether the session is connected and the device responds.
    /// </summary>
    public async Task<bool> IsConnectedAsync(CancellationToken ct = default)
    {
        return await _session.IsConnectedAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Ensures a T55xx tag is detected on the reader. Throws if no chip found.
    /// </summary>
    /// <exception cref="Pm3CommandException">When no T55xx chip is detected.</exception>
    public async Task EnsureT55SessionActiveAsync(CancellationToken ct = default)
    {
        var result = await _session.ExecuteAsync([new T55DetectCommand()], null, ct).ConfigureAwait(false);
        var detect = DetectParser.Parse(result);
        if (!detect.ChipFound)
            throw new Pm3CommandException("No T55xx chip detected. Place a tag on the reader.", result);
    }

    /// <summary>
    /// Read a block from page 0.
    /// </summary>
    /// <param name="block">Block number 0-7.</param>
    /// <returns>8-character hex string (uppercase).</returns>
    /// <exception cref="Pm3CommandException">When read fails.</exception>
    public async Task<string> ReadPage0BlockAsync(uint block, CancellationToken ct = default)
    {
        if (block > 7)
            throw new ArgumentOutOfRangeException(nameof(block), "Block must be between 0 and 7.");

        var result = await _session.ExecuteT55Async(new T55ReadBlockCommand(block), null, ct).ConfigureAwait(false);
        var parsed = BlockReadParser.Parse(result, (int)block);
        if (!parsed.Success || parsed.HexData is null)
            throw new Pm3CommandException($"Failed to read block {block}.", result);
        return parsed.HexData;
    }

    /// <summary>
    /// Write a block to page 0. Block 0 and 7 are forbidden for safety.
    /// </summary>
    /// <param name="block">Block number 1-6.</param>
    /// <param name="data">Block data.</param>
    /// <exception cref="Pm3CommandException">When write fails.</exception>
    public async Task<bool> WritePage0BlockAsync(uint block, T55Block data, CancellationToken ct = default)
    {
        if (block == 0)
            throw new ArgumentException("Block 0 (configuration) is forbidden for this tool, it is too dangerous to write to. NEVER WRITE TO BLOCK 0.", nameof(block));
        if (block == 7)
            throw new ArgumentException("Block 7 (password) is forbidden for this tool, it is too dangerous to write to. NEVER WRITE TO BLOCK 7.", nameof(block));
        if (block > 7)
            throw new ArgumentOutOfRangeException(nameof(block), "Block must be between 1 and 6.");

        var result = await _session.ExecuteT55Async(new T55WriteBlockCommand(block, data), null, ct).ConfigureAwait(false);
        if (result.HasErrors)
            throw new Pm3CommandException($"Failed to write block {block}. {result.ErrorSummary}", result);
        return true;
    }

    /// <summary>
    /// Dump all page 0 blocks from the tag.
    /// </summary>
    /// <returns>Raw dump output string.</returns>
    public async Task<string> DumpAsync(CancellationToken ct = default)
    {
        var result = await _session.ExecuteT55Async(new T55DumpCommand(), null, ct).ConfigureAwait(false);
        return result.RawOutput;
    }

    /// <summary>
    /// Run LF tune to measure antenna characteristics. Call GetLfTuneLastMilliVoltsAsync to read the result.
    /// </summary>
    public async Task<bool> StartLfTuneAsync(CancellationToken ct = default)
    {
        _lastTuneResult = await _session.ExecuteAsync([new LfTuneCommand()], null, ct).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Get the last millivolt value from the most recent StartLfTuneAsync run.
    /// </summary>
    /// <exception cref="InvalidOperationException">When StartLfTuneAsync has not been called.</exception>
    public Task<uint> GetLfTuneLastMilliVoltsAsync(CancellationToken ct = default)
    {
        if (_lastTuneResult is null)
            throw new InvalidOperationException("Call StartLfTuneAsync first.");
        var parsed = TuneParser.Parse(_lastTuneResult);
        if (!parsed.Success)
            throw new Pm3CommandException("No peak mV value in tune output.", _lastTuneResult);
        return Task.FromResult(parsed.PeakMilliVolts);
    }

    /// <summary>
    /// Stop LF tune. No-op for per-invocation mode (lf tune runs and exits).
    /// </summary>
    public Task<bool> StopLfTuneAsync(CancellationToken ct = default) => Task.FromResult(true);

    /// <summary>
    /// Execute a raw Proxmark3 CLI command and return the output.
    /// Use for commands that are not wrapped by the high-level API (e.g., hw version, lf search).
    /// Only supported by the process-wrapper executor.
    /// </summary>
    /// <param name="command">The full pm3 command string.</param>
    /// <returns>The raw output from the command.</returns>
    public async Task<string> ExecuteRawCommandAsync(string command, CancellationToken ct = default)
    {
        var result = await _session.ExecuteAsync([new CliPassthroughCommand(command)], null, ct).ConfigureAwait(false);
        return result.RawOutput;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _session.DisposeAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }
}

