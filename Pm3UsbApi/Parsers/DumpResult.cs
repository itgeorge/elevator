using Tokens;

namespace Pm3UsbApi.Parsers;

/// <summary>
/// Result of parsing lf t55 dump output.
/// </summary>
/// <param name="Success">True if at least one block was parsed.</param>
/// <param name="Blocks">Parsed blocks (typically 8 for page 0).</param>
/// <param name="RawOutput">Original raw output string.</param>
public record DumpResult(bool Success, IReadOnlyList<T55Block> Blocks, string RawOutput);
