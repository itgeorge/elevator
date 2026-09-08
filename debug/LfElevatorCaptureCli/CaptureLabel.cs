namespace LfElevatorCaptureCli;

/// <summary>Converts an operator label into a single safe filename component.</summary>
public static class CaptureLabel
{
    public static string Sanitize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            throw new ArgumentException("A non-empty label is required.", nameof(input));

        var source = input.Trim();
        var chars = new List<char>(Math.Min(source.Length, 48));
        var previousUnderscore = false;
        foreach (var c in source)
        {
            var safe = (c is >= 'a' and <= 'z') || (c is >= 'A' and <= 'Z') ||
                       (c is >= '0' and <= '9') || c is '-' or '_';
            var mapped = safe ? c : '_';
            if (mapped == '_')
            {
                if (previousUnderscore) continue;
                previousUnderscore = true;
            }
            else
            {
                previousUnderscore = false;
            }
            chars.Add(mapped);
            if (chars.Count == 48) break;
        }

        var result = new string(chars.ToArray()).Trim('_', '-');
        if (result.Length == 0 || result is "." or "..")
            throw new ArgumentException("The label contains no safe filename characters.", nameof(input));
        return result;
    }
}
