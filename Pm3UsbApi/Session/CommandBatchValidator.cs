using Pm3UsbApi.Commands;

namespace Pm3UsbApi.Session;

/// <summary>
/// Validates command batches before they reach an executor.
/// </summary>
internal static class CommandBatchValidator
{
    public static void Validate(IReadOnlyList<IPm3DeviceCommand> commands)
    {
        if (commands is null || commands.Count == 0)
            throw new ArgumentException("At least one command is required.", nameof(commands));

        if (commands.Count > 1 && commands.Any(c => c is LfTuneCommand))
            throw new InvalidOperationException("LfTune cannot be combined with other commands.");
    }
}
