namespace RideCaptureCli;

public sealed class CaptureRecord
{
    public string Timestamp { get; set; } = string.Empty;
    public string TokenId { get; set; } = string.Empty;
    public string SequenceId { get; set; } = string.Empty;
    public CaptureStatus Status { get; set; }
    public string Warnings { get; set; } = string.Empty;
    public int SignalMv { get; set; }
    public bool WeakSignal { get; set; }
    public int TrackedCount { get; set; }
    public int? RealRideCount { get; set; }
    public bool ZeroAnchor { get; set; }
    public string Block0 { get; set; } = string.Empty;
    public string Block1 { get; set; } = string.Empty;
    public string Block2 { get; set; } = string.Empty;
    public string Block3 { get; set; } = string.Empty;
    public string Block4 { get; set; } = string.Empty;
    public string Block5 { get; set; } = string.Empty;
    public string Block6 { get; set; } = string.Empty;
    public string Block7 { get; set; } = string.Empty;
    public string CopiedDumpRelativePath { get; set; } = string.Empty;

    public string EncodedState => $"{Block5}-{Block6}";
}
