namespace RideCaptureCli;

public sealed class CaptureScanData
{
    public required DateTimeOffset Timestamp { get; init; }
    public required int SignalMv { get; init; }
    public required bool WeakSignal { get; init; }
    public required IReadOnlyList<string> Blocks { get; init; }
    public string CopiedDumpRelativePath { get; init; } = string.Empty;
    public string RawDumpOutput { get; init; } = string.Empty;

    public string TokenId => string.Join('-', Blocks.Skip(1).Take(4));
    public string EncodedState => $"{Blocks[5]}-{Blocks[6]}";
    public bool MirrorMatches => Blocks[5] == Blocks[6];
}
