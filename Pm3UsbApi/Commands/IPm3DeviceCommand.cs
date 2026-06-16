namespace Pm3UsbApi.Commands;

/// <summary>
/// Marker for a typed Proxmark3 device operation. Executors map these to transport-specific
/// encodings (CLI strings for process wrapper, binary packets for native USB).
/// </summary>
public interface IPm3DeviceCommand
{
}
