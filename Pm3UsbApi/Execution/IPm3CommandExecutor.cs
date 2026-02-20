namespace Pm3UsbApi.Execution;

/// <summary>
/// Abstraction for executing Proxmark3 commands.
/// Stage A uses a process wrapper; Stage B will use native binary protocol.
/// </summary>
public interface IPm3CommandExecutor : IAsyncDisposable
{
    /// <summary>
    /// Execute one or more chained commands. For per-invocation mode, these are joined with "; ".
    /// </summary>
    /// <param name="commands">Commands to execute (e.g., ["lf t55 detect", "lf t55 read -b 0"]).</param>
    /// <param name="timeout">Override for execution timeout. null = use options default.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="portOverride">Optional port to use for this invocation (e.g. from auto-discovery).</param>
    /// <returns>The command result with output lines and exit code.</returns>
    Task<CommandResult> ExecuteAsync(
        string[] commands,
        TimeSpan? timeout = null,
        CancellationToken ct = default,
        string? portOverride = null);

    /// <summary>
    /// Send a break/cancel signal to abort a running operation.
    /// </summary>
    Task CancelCurrentAsync(CancellationToken ct = default);
}
