namespace RideCaptureCli;

public sealed class CaptureApplyResult
{
    public required IReadOnlyList<CaptureRecord> Records { get; init; }
    public required CaptureRecord AddedRecord { get; init; }
    public bool AutoNormalized { get; init; }
}
