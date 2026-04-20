using System.Text.RegularExpressions;

namespace RideCaptureCli;

public sealed class SequenceIdGenerator
{
    private static readonly Regex CounterPattern = new(@"-s(?<num>\d+)$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public string CreateNext(string tokenId, IReadOnlyList<CaptureRecord> existingRecords, DateTimeOffset now)
    {
        var tokenRecords = existingRecords.Where(r => string.Equals(r.TokenId, tokenId, StringComparison.OrdinalIgnoreCase));
        var next = tokenRecords
            .Select(r => CounterPattern.Match(r.SequenceId))
            .Where(m => m.Success)
            .Select(m => int.Parse(m.Groups["num"].Value))
            .DefaultIfEmpty(0)
            .Max() + 1;

        var block1 = tokenId.Split('-', StringSplitOptions.RemoveEmptyEntries)[0];
        return $"{block1}-{now:yyyyMMdd-HHmmss}-s{next:00}";
    }
}
