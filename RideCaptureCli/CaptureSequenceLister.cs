namespace RideCaptureCli;

public sealed class CaptureSequenceLister
{
    public IReadOnlyList<CaptureSequenceSummary> Build(IReadOnlyList<CaptureRecord> records)
    {
        return records
            .GroupBy(r => r.SequenceId, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var ordered = group
                    .OrderBy(r => DateTimeOffset.Parse(r.Timestamp))
                    .ToList();
                var first = ordered[0];
                var last = ordered[^1];
                return new CaptureSequenceSummary
                {
                    SequenceId = last.SequenceId,
                    TokenId = last.TokenId,
                    RowCount = ordered.Count,
                    FirstTimestamp = first.Timestamp,
                    LastTimestamp = last.Timestamp,
                    LatestTrackedCount = last.TrackedCount,
                    LatestRealRideCount = last.RealRideCount,
                    LatestState = last.EncodedState,
                    LatestWarnings = last.Warnings
                };
            })
            .OrderByDescending(x => DateTimeOffset.Parse(x.LastTimestamp))
            .ToList();
    }

    public void Print(IReadOnlyList<CaptureRecord> records)
    {
        var summaries = Build(records);
        if (summaries.Count == 0)
        {
            Console.WriteLine("No sequences in CSV.");
            Console.WriteLine();
            return;
        }

        foreach (var summary in summaries)
        {
            Console.WriteLine(summary.SequenceId);
            Console.WriteLine($"  token:   {summary.TokenId}");
            Console.WriteLine($"  rows:    {summary.RowCount}");
            Console.WriteLine($"  tracked: {summary.LatestTrackedCount}");
            Console.WriteLine($"  real:    {(summary.LatestRealRideCount.HasValue ? summary.LatestRealRideCount.Value : "<unknown>")}");
            Console.WriteLine($"  state:   {summary.LatestState}");
            Console.WriteLine($"  last:    {summary.LastTimestamp}");
            if (!string.IsNullOrWhiteSpace(summary.LatestWarnings))
                Console.WriteLine($"  warn:    {summary.LatestWarnings}");
            Console.WriteLine();
        }
    }
}
