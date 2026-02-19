namespace Pm3UsbApi.Parsers;

/// <summary>
/// Result of parsing lf t55 read -b N output.
/// </summary>
/// <param name="Success">True if hex data was found for the block.</param>
/// <param name="HexData">8-character hex string, uppercase. Null if not found.</param>
public record BlockReadResult(bool Success, string? HexData);
