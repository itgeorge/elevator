using Pm3UsbApi.Commands;

namespace Pm3UsbApi;

/// <summary>
/// Result of a Proxmark3 command execution.
/// </summary>
public class CommandResult
{
    /// <summary>
    /// The commands that were executed.
    /// </summary>
    public required IReadOnlyList<IPm3DeviceCommand> Commands { get; init; }

    /// <summary>
    /// Output lines from the command (typically stdout, optionally stderr merged).
    /// </summary>
    public required IReadOnlyList<string> OutputLines { get; init; }

    /// <summary>
    /// Full raw output as a single string.
    /// </summary>
    public string RawOutput => string.Join(Environment.NewLine, OutputLines);

    /// <summary>
    /// Process exit code (0 = success for most commands).
    /// </summary>
    public int ExitCode { get; init; }

    /// <summary>
    /// True if the output contained error indicators (e.g., [!], [-], failed).
    /// </summary>
    public bool HasErrors { get; init; }

    /// <summary>
    /// Summary of detected errors, if any.
    /// </summary>
    public string? ErrorSummary { get; init; }
}
