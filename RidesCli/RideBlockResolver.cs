using Tokens;

namespace RidesCli;

public enum RideReadStatus
{
    Success,
    UnknownEncodingFamily,
    InvalidBlockFormat,
}

public sealed record RideReadResult(
    RideReadStatus Status,
    uint? Rides,
    T55Block? SourceBlock,
    int? SourceBlockNumber,
    bool BlocksMatched,
    string? WarningMessage);

/// <summary>
/// Resolves ride count from mirrored page-0 blocks 5 and 6.
/// When both blocks decode but differ, block 6 is preferred — confirmed elevator behavior (2026-07-20 hardware test).
/// </summary>
public static class RideBlockResolver
{
    public const uint MaxRides = 500;

    public static RideReadResult Resolve(T55Block block5, T55Block block6)
    {
        if (block5.Value == block6.Value)
            return ResolveMatching(block5);

        var valid5 = TryValidate(block5, out var rides5);
        var valid6 = TryValidate(block6, out var rides6);

        if (valid5 && valid6)
        {
            return new RideReadResult(RideReadStatus.Success, rides6, block6, 6, false,
                rides5 == rides6
                    ? "Warning: blocks 5 and 6 differ; using block 6."
                    : $"Warning: blocks 5 and 6 differ; using block 6 ({rides6} rides).");
        }

        if (valid5)
            return new RideReadResult(RideReadStatus.Success, rides5, block5, 5, false,
                "Warning: blocks 5 and 6 differ; using block 5.");

        if (valid6)
            return new RideReadResult(RideReadStatus.Success, rides6, block6, 6, false,
                "Warning: blocks 5 and 6 differ; using block 6.");

        return Failure(block5, block6, blocksMatched: false);
    }

    private static RideReadResult ResolveMatching(T55Block block)
    {
        if (TryValidate(block, out var rides))
            return new RideReadResult(RideReadStatus.Success, rides, block, 5, true, null);

        return Failure(block, block, blocksMatched: true);
    }

    private static RideReadResult Failure(T55Block block5, T55Block block6, bool blocksMatched)
    {
        // A failed full registry match is unknown. Do not use visible high-word patterns to
        // guess a family: unregistered rotation-0 candidates must remain unknown as well.
        return new RideReadResult(RideReadStatus.UnknownEncodingFamily, null, block5, 5, blocksMatched, null);
    }

    internal static bool TryValidate(T55Block block, out uint rides)
    {
        if (!EncodingSequences.TryDecode(block, out _, out rides))
            return false;

        return rides <= MaxRides;
    }
}
