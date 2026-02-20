using System.Text.RegularExpressions;

namespace Pm3UsbApi.Parsers;

/// <summary>
/// Shared utilities for parsing Proxmark3 CLI output.
/// </summary>
public static class OutputParser
{
    private static readonly Regex AnsiEscapeRegex = new(
        @"\x1B\[[0-9;]*[A-Za-z]",
        RegexOptions.Compiled);

    /// <summary>
    /// Removes ANSI escape sequences (color codes, etc.) from a line.
    /// </summary>
    public static string StripAnsi(string line)
    {
        if (string.IsNullOrEmpty(line)) return line;
        return AnsiEscapeRegex.Replace(line, string.Empty);
    }

    /// <summary>
    /// Scans output lines for error indicators: [!], [-], or lines starting with "error"/"failed" (case-insensitive).
    /// </summary>
    /// <returns>True if errors detected; errorSummary contains the first error line(s) if any.</returns>
    public static (bool hasErrors, string? errorSummary) DetectErrors(IReadOnlyList<string> lines)
    {
        if (lines.Count == 0) return (false, null);

        var errorLines = new List<string>();
        foreach (var line in lines)
        {
            var stripped = StripAnsi(line).Trim();
            if (string.IsNullOrEmpty(stripped)) continue;

            if (stripped.StartsWith("[!]", StringComparison.Ordinal) ||
                stripped.StartsWith("[-]", StringComparison.Ordinal) ||
                stripped.StartsWith("error", StringComparison.OrdinalIgnoreCase) ||
                stripped.StartsWith("failed", StringComparison.OrdinalIgnoreCase))
            {
                errorLines.Add(stripped);
            }
        }

        if (errorLines.Count == 0) return (false, null);

        var summary = errorLines.Count <= 3
            ? string.Join("; ", errorLines)
            : string.Join("; ", errorLines.Take(2)) + $" ... (+{errorLines.Count - 2} more)";
        return (true, summary);
    }

    /// <summary>
    /// Detects if the Proxmark3 client is running in offline mode (no device connected).
    /// </summary>
    /// <returns>True if offline mode is indicated in the output.</returns>
    public static bool DetectOfflineMode(IReadOnlyList<string> lines)
    {
        if (lines.Count == 0) return false;

        foreach (var line in lines)
        {
            var stripped = StripAnsi(line);
            if (stripped.Contains("OFFLINE mode", StringComparison.OrdinalIgnoreCase))
                return true;
            if (stripped.Contains("[offline|", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
