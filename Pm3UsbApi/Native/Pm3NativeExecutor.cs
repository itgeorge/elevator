using Pm3UsbApi.Commands;
using Pm3UsbApi.Execution;
using Pm3UsbApi.Native.Demod;
using Pm3UsbApi.Native.Protocol;
using Pm3UsbApi.Native.T55;
using Pm3UsbApi.Native.Transport;

namespace Pm3UsbApi.Native;

/// <summary>
/// Stage B executor: speaks Proxmark3 NG packets over USB CDC serial.
/// </summary>
public sealed class Pm3NativeExecutor : IPm3CommandExecutor
{
    private readonly Pm3Options _options;
    private readonly Pm3T55Config _t55Config = new();
    private Pm3SerialTransport? _transport;
    private string? _connectedPort;
    private bool _disposed;

    public Pm3NativeExecutor(Pm3Options options)
    {
        _options = options ?? new Pm3Options();
    }

    public async Task<CommandResult> ExecuteAsync(
        IReadOnlyList<IPm3DeviceCommand> commands,
        TimeSpan? timeout = null,
        CancellationToken ct = default,
        string? portOverride = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (commands is null || commands.Count == 0)
            throw new ArgumentException("At least one command is required.", nameof(commands));

        var effectiveTimeout = timeout ?? _options.DefaultCommandTimeout;
        await EnsureTransportAsync(portOverride, effectiveTimeout, ct).ConfigureAwait(false);

        if (commands.Count == 1)
        {
            return commands[0] switch
            {
                HwVersionCommand => ExecuteHwVersion(commands, effectiveTimeout, ct),
                LfTuneCommand => await ExecuteLfTuneAsync(commands, ct).ConfigureAwait(false),
                T55DetectCommand => ExecuteT55Detect(commands, ct),
                T55ReadBlockCommand read => ExecuteT55ReadBlock(commands, read.Block, ct),
                CliPassthroughCommand => throw new Pm3CommandException("Raw CLI commands are not supported by the native executor."),
                T55WriteBlockCommand or T55DumpCommand =>
                    throw new Pm3CommandException($"{commands[0].GetType().Name} is not supported by the native executor yet."),
                _ => throw new Pm3CommandException($"Unsupported command type: {commands[0].GetType().Name}"),
            };
        }

        return await ExecuteBatchAsync(commands, ct).ConfigureAwait(false);
    }

    public Task CancelCurrentAsync(CancellationToken ct = default) => Task.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_transport is not null)
            await _transport.DisposeAsync().ConfigureAwait(false);
        _transport = null;
        _connectedPort = null;
    }

    private async Task<CommandResult> ExecuteBatchAsync(IReadOnlyList<IPm3DeviceCommand> commands, CancellationToken ct)
    {
        var lines = new List<string>();
        var hasErrors = false;

        foreach (var command in commands)
        {
            switch (command)
            {
                case T55DetectCommand:
                {
                    var result = ExecuteT55Detect([command], ct);
                    lines.AddRange(result.OutputLines);
                    hasErrors |= result.HasErrors;
                    break;
                }
                case T55ReadBlockCommand read:
                {
                    var result = ExecuteT55ReadBlock([command], read.Block, ct);
                    lines.AddRange(result.OutputLines);
                    hasErrors |= result.HasErrors;
                    break;
                }
                default:
                    throw new Pm3CommandException(
                        $"Native executor batch supports T55 detect/read only. Unsupported: {command.GetType().Name}");
            }

            await Task.Yield();
        }

        return new CommandResult
        {
            Commands = commands,
            OutputLines = lines,
            ExitCode = hasErrors ? 1 : 0,
            HasErrors = hasErrors,
            ErrorSummary = hasErrors ? "One or more native T55 commands failed." : null,
        };
    }

    private CommandResult ExecuteT55Detect(IReadOnlyList<IPm3DeviceCommand> commands, CancellationToken ct)
    {
        var service = CreateT55Service();
        if (!service.Detect(_t55Config, ct))
        {
            var failed = new CommandResult
            {
                Commands = commands,
                OutputLines = Pm3NativeOutputBuilder.BuildDetectFailedLines(),
                ExitCode = 1,
                HasErrors = true,
                ErrorSummary = "T55 detect failed",
            };
            throw new Pm3CommandException("No T55xx chip detected.", failed);
        }

        return new CommandResult
        {
            Commands = commands,
            OutputLines = Pm3NativeOutputBuilder.BuildDetectLines(_t55Config),
            ExitCode = 0,
            HasErrors = false,
        };
    }

    private CommandResult ExecuteT55ReadBlock(IReadOnlyList<IPm3DeviceCommand> commands, uint block, CancellationToken ct)
    {
        if (!_t55Config.Detected)
            throw new Pm3CommandException("T55 session is not active. Run detect before read.");

        var service = CreateT55Service();
        if (!service.ReadBlock(_t55Config, (byte)block, out var value, ct))
        {
            var failed = new CommandResult
            {
                Commands = commands,
                OutputLines = Pm3NativeOutputBuilder.BuildReadFailedLines(block),
                ExitCode = 1,
                HasErrors = true,
                ErrorSummary = $"Failed to read block {block}",
            };
            throw new Pm3CommandException($"Failed to read block {block}.", failed);
        }

        return new CommandResult
        {
            Commands = commands,
            OutputLines = Pm3NativeOutputBuilder.BuildReadBlockLines(block, value),
            ExitCode = 0,
            HasErrors = false,
        };
    }

    private Pm3T55NativeService CreateT55Service()
    {
        if (_transport is null)
            throw new Pm3ConnectionException("Native transport is not connected.");
        return new Pm3T55NativeService(_transport);
    }

    private CommandResult ExecuteHwVersion(IReadOnlyList<IPm3DeviceCommand> commands, TimeSpan timeout, CancellationToken ct)
    {
        var response = Send(Pm3CommandCodes.CmdVersion, ReadOnlySpan<byte>.Empty, timeout, ct);
        if (response.Status != Pm3CommandCodes.Pm3Success)
            throw new Pm3CommandException($"CMD_VERSION failed with status {response.Status}.", ToErrorResult(commands, response));

        var lines = Pm3NativeOutputBuilder.BuildHwVersionLines(response);
        return new CommandResult
        {
            Commands = commands,
            OutputLines = lines,
            ExitCode = 0,
            HasErrors = false,
        };
    }

    private async Task<CommandResult> ExecuteLfTuneAsync(IReadOnlyList<IPm3DeviceCommand> commands, CancellationToken ct)
    {
        var divisor = Pm3CommandCodes.LfDivisor125;
        var init = new byte[] { 1, divisor };
        var measure = new byte[] { 2, divisor };
        var shutdown = new byte[] { 3, divisor };

        var measureTimeout = TimeSpan.FromSeconds(1);
        var response = Send(Pm3CommandCodes.CmdMeasureAntennaTuningLf, init, measureTimeout, ct);
        if (response.Status != Pm3CommandCodes.Pm3Success)
            throw new Pm3CommandException("LF tune initialization failed.", ToErrorResult(commands, response));

        uint peak = 0;
        var end = Environment.TickCount64 + (long)Pm3ProcessExecutor.LfTuneCaptureInterval.TotalMilliseconds;
        while (Environment.TickCount64 < end)
        {
            ct.ThrowIfCancellationRequested();
            response = Send(Pm3CommandCodes.CmdMeasureAntennaTuningLf, measure, measureTimeout, ct);
            if (response.Status == Pm3CommandCodes.Pm3EopAborted || response.Data.Length != sizeof(uint))
                break;

            if (response.Status != Pm3CommandCodes.Pm3Success)
                throw new Pm3CommandException("LF tune measurement failed.", ToErrorResult(commands, response));

            var volt = BitConverter.ToUInt32(response.Data, 0);
            if (volt > peak)
                peak = volt;

            await Task.Delay(50, ct).ConfigureAwait(false);
        }

        try
        {
            Send(Pm3CommandCodes.CmdMeasureAntennaTuningLf, shutdown, measureTimeout, ct);
        }
        catch
        {
            // Best effort shutdown, same as client on abort.
        }

        if (peak == 0)
            throw new Pm3CommandException("LF tune returned no voltage samples.", ToErrorResult(commands, response));

        return new CommandResult
        {
            Commands = commands,
            OutputLines = Pm3NativeOutputBuilder.BuildLfTuneLines(peak),
            ExitCode = 0,
            HasErrors = false,
        };
    }

    private Pm3ResponseFrame Send(ushort command, ReadOnlySpan<byte> payload, TimeSpan timeout, CancellationToken ct)
    {
        if (_transport is null)
            throw new Pm3ConnectionException("Native transport is not connected.");
        return _transport.SendCommand(command, payload, timeout, ct);
    }

    private async Task EnsureTransportAsync(string? portOverride, TimeSpan timeout, CancellationToken ct)
    {
        var port = portOverride ?? _options.DevicePort ?? _connectedPort;
        if (string.IsNullOrWhiteSpace(port))
        {
            if (!_options.AutoConnect)
                throw new Pm3ConnectionException("DevicePort must be set when autoConnect is false.");

            port = await DiscoverPortAsync(timeout, ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(port))
                throw new Pm3ConnectionException("No Proxmark3 device found via native port discovery.");
        }

        if (_transport?.IsOpen == true && string.Equals(_connectedPort, port, StringComparison.OrdinalIgnoreCase))
            return;

        if (_transport is not null)
            await _transport.DisposeAsync().ConfigureAwait(false);

        _transport = new Pm3SerialTransport(port, _options.SerialBaudRate);
        _transport.Open();
        _connectedPort = port;
    }

    private async Task<string?> DiscoverPortAsync(TimeSpan timeout, CancellationToken ct)
    {
        var ports = await PortDiscovery.ListPortsAsync(_options.Pm3ClientPath, ct).ConfigureAwait(false);
        foreach (var port in ports)
        {
            await using var probe = new Pm3SerialTransport(port, _options.SerialBaudRate);
            if (probe.TryPing(timeout, ct))
                return port;
        }

        return null;
    }

    private static CommandResult ToErrorResult(IReadOnlyList<IPm3DeviceCommand> commands, Pm3ResponseFrame response) =>
        new()
        {
            Commands = commands,
            OutputLines = Pm3NativeOutputBuilder.BuildErrorLines($"status={response.Status}, reason={response.Reason}"),
            ExitCode = response.Status,
            HasErrors = true,
            ErrorSummary = $"PM3 status {response.Status}",
        };
}
