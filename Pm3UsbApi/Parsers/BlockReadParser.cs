using System.Text.RegularExpressions;
using Pm3UsbApi;

namespace Pm3UsbApi.Parsers;

/// <summary>
/// Parses lf t55 read -b N output.
/// </summary>
public static class BlockReadParser
{
    // Matches block table rows: "0 | 00148040 | ..." or "[+] 0  | 00148040 | ..."
    private static readonly Regex BlockTableRegex = new(
        @"(?:^|\s)(\d+)\s*\|\s*([0-9A-Fa-f]{8})\b",
        RegexOptions.Compiled);

    // Matches "[+] Block N: XXXXXXXX" format
    private static readonly Regex BlockColonRegex = new(
        @"Block\s+(\d+)\s*:\s*([0-9A-Fa-f]{8})\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Parse read block output for the requested block number.
    /// </summary>
    /// <param name="result">Command result from lf t55 read -b N.</param>
    /// <param name="block">Block number (0-7) that was read.</param>
    public static BlockReadResult Parse(CommandResult result, int block)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (block is < 0 or > 7)
            throw new ArgumentOutOfRangeException(nameof(block), "Block must be 0-7.");

        var blockStr = block.ToString();
        string? hexData = null;

        foreach (var line in result.OutputLines)
        {
            var stripped = OutputParser.StripAnsi(line).Trim();
            if (string.IsNullOrEmpty(stripped)) continue;

            // Try "[+] Block N: XXXXXXXX" format first
            var colonMatch = BlockColonRegex.Match(stripped);
            if (colonMatch.Success && int.TryParse(colonMatch.Groups[1].Value, out var colonBlock) && colonBlock == block)
            {
                hexData = colonMatch.Groups[2].Value.ToUpperInvariant();
                break;
            }

            // Try table format: "N | XXXXXXXX"
            var tableMatch = BlockTableRegex.Match(stripped);
            if (tableMatch.Success)
            {
                var capturedBlock = tableMatch.Groups[1].Value.TrimStart('0');
                if (string.IsNullOrEmpty(capturedBlock)) capturedBlock = "0";
                if (capturedBlock == blockStr)
                {
                    hexData = tableMatch.Groups[2].Value.ToUpperInvariant();
                    break;
                }
            }
        }

        return new BlockReadResult(hexData is not null, hexData);
    }
}
