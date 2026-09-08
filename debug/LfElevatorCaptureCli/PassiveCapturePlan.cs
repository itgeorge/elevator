using System.Globalization;

namespace LfElevatorCaptureCli;

/// <summary>Builds the only device command this debug CLI is permitted to send.</summary>
public static class PassiveCapturePlan
{
    public const string ConnectCommand = "hw version";

    public static string BuildCommand(int samples, string saveBasePath)
    {
        if (samples is < 1 or > 1_000_000_000)
            throw new ArgumentOutOfRangeException(nameof(samples), "Samples must be between 1 and 1,000,000,000.");
        if (string.IsNullOrWhiteSpace(saveBasePath) || saveBasePath.Any(c => c is '"' or '\r' or '\n'))
            throw new ArgumentException("The save path must be non-empty and cannot contain quotes or newlines.", nameof(saveBasePath));

        // No user-supplied command text is accepted.  The second operation is local
        // graph-buffer persistence; it does not communicate with or modify a tag.
        return $"lf sniff -s {samples.ToString(CultureInfo.InvariantCulture)}; data save -f \"{saveBasePath}\"";
    }

    public static bool IsAllowed(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return false;
        var text = command.Trim();
        const string saveMarker = "; data save -f \"";
        var marker = text.IndexOf(saveMarker, StringComparison.OrdinalIgnoreCase);
        if (marker <= 0 || !text.EndsWith('"')) return false;

        var sniff = text[..marker].Trim();
        var savePath = text[(marker + saveMarker.Length)..^1];
        if (savePath.Length == 0 || savePath.Any(c => c is '"' or '\r' or '\n')) return false;

        var parts = sniff.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 4 &&
               parts[0].Equals("lf", StringComparison.OrdinalIgnoreCase) &&
               parts[1].Equals("sniff", StringComparison.OrdinalIgnoreCase) &&
               parts[2].Equals("-s", StringComparison.OrdinalIgnoreCase) &&
               int.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out var samples) &&
               samples > 0;
    }
}
