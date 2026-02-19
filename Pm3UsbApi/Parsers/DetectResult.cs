namespace Pm3UsbApi.Parsers;

/// <summary>
/// Result of parsing lf t55 detect (or lf t55xx detect) output.
/// </summary>
/// <param name="ChipFound">True if a valid chip type was detected.</param>
/// <param name="ChipType">Chip type string, e.g. "T55x7".</param>
/// <param name="Modulation">Modulation mode, e.g. "ASK", "FSK".</param>
/// <param name="Block0Hex">Block 0 configuration value as 8-char hex.</param>
public record DetectResult(
    bool ChipFound,
    string? ChipType,
    string? Modulation,
    string? Block0Hex);
