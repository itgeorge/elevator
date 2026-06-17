namespace Pm3UsbApi;

/// <summary>
/// Selects how commands are sent to the Proxmark3 device.
/// </summary>
public enum Pm3ExecutorKind
{
    /// <summary>Launch the proxmark3 client process (Stage A).</summary>
    Process,

    /// <summary>Direct USB CDC binary protocol (Stage B).</summary>
    Native,
}
