namespace RideCaptureCli;

public sealed class CaptureSequenceSummary
{
    public string SequenceId { get; init; } = string.Empty;
    public string TokenId { get; init; } = string.Empty;
    public int RowCount { get; init; }
    public string FirstTimestamp { get; init; } = string.Empty;
    public string LastTimestamp { get; init; } = string.Empty;
    public int LatestTrackedCount { get; init; }
    public int? LatestRealRideCount { get; init; }
    public string LatestState { get; init; } = string.Empty;
    public string LatestWarnings { get; init; } = string.Empty;
}
