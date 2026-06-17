using Pm3UsbApi.Commands;

namespace Pm3UsbApi.Execution;

/// <summary>
/// Maps typed device commands to proxmark3 client CLI strings. Used only by <see cref="Pm3ProcessExecutor"/>.
/// </summary>
internal static class Pm3CliFormatter
{
    public static string Format(IPm3DeviceCommand command) => command switch
    {
        HwVersionCommand => "hw version",
        LfTuneCommand => "lf tune",
        T55DetectCommand => "lf t55 detect",
        T55ReadBlockCommand read => $"lf t55 read -b {read.Block}",
        T55WriteBlockCommand write => $"lf t55 write -b {write.Block} -d {write.Data.ToHex()}",
        T55DumpCommand => "lf t55 dump",
        CliPassthroughCommand passthrough => passthrough.CliText,
        _ => throw new ArgumentException($"Unsupported command type: {command.GetType().Name}", nameof(command))
    };

    public static string FormatBatch(IReadOnlyList<IPm3DeviceCommand> commands) =>
        string.Join("; ", commands.Select(Format));
}
