namespace Pm3UsbApi.Parsers;

/// <summary>
/// Result of parsing lf tune / hw tune output.
/// </summary>
/// <param name="Success">True if a peak mV value was found.</param>
/// <param name="PeakMilliVolts">Peak voltage in millivolts. 0 if not found.</param>
public record TuneResult(bool Success, uint PeakMilliVolts);
