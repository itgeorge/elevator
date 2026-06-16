using Pm3UsbApi.Commands;
using Pm3UsbApi.Execution;
using Pm3UsbApi.Native.Protocol;
using Pm3UsbApi.Native.Transport;

namespace Pm3UsbApi.Native;

/// <summary>
/// Stage B executor: speaks Proxmark3 NG packets over USB CDC serial.
/// </summary>
public sealed class Pm3NativeExecutor : IPm3CommandExecutor
{
    private readonly Pm3Options _options;
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

        if (commands.Count != 1)
            throw new Pm3CommandException("Native executor supports one device command per invocation.");

        var effectiveTimeout = timeout ?? _options.DefaultCommandTimeout;
        await EnsureTransportAsync(portOverride, effectiveTimeout, ct).ConfigureAwait(false);

        return commands[0] switch
        {
            HwVersionCommand => ExecuteHwVersion(commands, effectiveTimeout, ct),
            LfTuneCommand => await ExecuteLfTuneAsync(commands, ct).ConfigureAwait(false),
            CliPassthroughCommand => throw new Pm3CommandException("Raw CLI commands are not supported by the native executor."),
            T55DetectCommand or T55ReadBlockCommand or T55WriteBlockCommand or T55DumpCommand =>
                throw new Pm3CommandException($"{commands[0].GetType().Name} is not supported by the native executor yet."),
            _ => throw new Pm3CommandException($"Unsupported command type: {commands[0].GetType().Name}"),
        };
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
