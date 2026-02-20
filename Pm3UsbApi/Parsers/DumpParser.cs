using System.Text.RegularExpressions;
using Pm3UsbApi;
using Tokens;

namespace Pm3UsbApi.Parsers;

/// <summary>
/// Parses lf t55 dump output.
/// </summary>
public static class DumpParser
{
    // Matches table rows: "0   | 00107060 | ..." or "[+]  00 | 00148040 | ..." (Iceman format)
    private static readonly Regex BlockRowRegex = new(
        @"^\s*(?:\[\+\]\s*)?(\d+)\s+\|\s+([0-9A-Fa-f]{8})\b",
        RegexOptions.Compiled | RegexOptions.Multiline);

    /// <summary>
    /// Parse dump command output into T55Block list.
    /// </summary>
    public static DumpResult Parse(CommandResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var blocks = new List<T55Block>();
        var rawOutput = result.RawOutput;

        foreach (var line in result.OutputLines)
        {
            var stripped = OutputParser.StripAnsi(line).Trim();
            if (string.IsNullOrEmpty(stripped)) continue;

            var match = BlockRowRegex.Match(stripped);
            if (match.Success)
            {
                var hex = match.Groups[2].Value.ToUpperInvariant();
                try
                {
                    blocks.Add(T55Block.FromHex(hex));
                }
                catch
                {
                    // Skip invalid hex
                }
            }
        }

        return new DumpResult(blocks.Count > 0, blocks, rawOutput);
    }
}
