using System.Text.RegularExpressions;
using Pm3UsbApi;

namespace Pm3UsbApi.Parsers;

/// <summary>
/// Parses lf t55 detect / lf t55xx detect output.
/// </summary>
public static class DetectParser
{
    private static readonly Regex ChipTypeRegex = new(
        @"Chip\s+Type\s*:\s*(.+?)(?:\s*$|\s*\[)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ModulationRegex = new(
        @"Modulation\s*:\s*(\S+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex Block0Regex = new(
        @"Block0\s*:\s*(?:0x)?([0-9A-Fa-f]{8})\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Parse detect command output.
    /// </summary>
    /// <param name="result">Command result from lf t55 detect.</param>
    /// <returns>Parsed detect result. ChipFound is true when Chip Type is present and not "none"/"unknown".</returns>
    public static DetectResult Parse(CommandResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        string? chipType = null;
        string? modulation = null;
        string? block0Hex = null;

        foreach (var line in result.OutputLines)
        {
            var stripped = OutputParser.StripAnsi(line).Trim();
            if (string.IsNullOrEmpty(stripped)) continue;

            var chipMatch = ChipTypeRegex.Match(stripped);
            if (chipMatch.Success)
                chipType = chipMatch.Groups[1].Value.Trim();

            var modMatch = ModulationRegex.Match(stripped);
            if (modMatch.Success)
                modulation = modMatch.Groups[1].Value.Trim();

            var blockMatch = Block0Regex.Match(stripped);
            if (blockMatch.Success)
                block0Hex = blockMatch.Groups[1].Value.ToUpperInvariant();
        }

        var chipFound = !string.IsNullOrEmpty(chipType)
            && !chipType.Contains("none", StringComparison.OrdinalIgnoreCase)
            && !chipType.Contains("unknown", StringComparison.OrdinalIgnoreCase);

        return new DetectResult(chipFound, chipType, modulation, block0Hex);
    }
}
