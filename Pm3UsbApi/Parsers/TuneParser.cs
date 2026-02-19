using System.Text.RegularExpressions;
using Pm3UsbApi;

namespace Pm3UsbApi.Parsers;

/// <summary>
/// Parses lf tune / hw tune output for peak millivolt value.
/// </summary>
public static class TuneParser
{
    private static readonly Regex MvRegex = new(
        @"\[=\]\s*(\d+)\s*mV\b",
        RegexOptions.Compiled);

    /// <summary>
    /// Parse tune command output. Uses the last mV match if multiple exist.
    /// </summary>
    public static TuneResult Parse(CommandResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        uint lastMv = 0;
        var found = false;

        var fullText = result.RawOutput;
        var match = MvRegex.Match(fullText);
        while (match.Success)
        {
            if (uint.TryParse(match.Groups[1].Value, out var mv))
            {
                lastMv = mv;
                found = true;
            }
            match = match.NextMatch();
        }

        return new TuneResult(found, lastMv);
    }
}
