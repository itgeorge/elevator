using Tokens;

namespace Pm3UsbApi.Commands;

public sealed record HwVersionCommand : IPm3DeviceCommand;

public sealed record LfTuneCommand(int? SampleCount = null, TimeSpan? Timeout = null) : IPm3DeviceCommand;

public sealed record T55DetectCommand : IPm3DeviceCommand;

public sealed record T55ReadBlockCommand(uint Block) : IPm3DeviceCommand;

public sealed record T55WriteBlockCommand(uint Block, T55Block Data) : IPm3DeviceCommand;

public sealed record T55DumpCommand : IPm3DeviceCommand;

/// <summary>
/// Stage A escape hatch: pass arbitrary CLI text to the proxmark3 client.
/// Not supported by native USB executors.
/// </summary>
public sealed record CliPassthroughCommand(string CliText) : IPm3DeviceCommand;
