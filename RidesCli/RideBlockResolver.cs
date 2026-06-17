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
            return new RideReadResult(
                RideReadStatus.Success,
                rides5,
                block5,
                5,
                BlocksMatched: false,
                WarningMessage: rides5 == rides6
                    ? "Warning: blocks 5 and 6 differ; using block 5."
                    : $"Warning: blocks 5 and 6 differ; using block 5 ({rides5} rides).");
        }

        if (valid5)
        {
            return new RideReadResult(
                RideReadStatus.Success,
                rides5,
                block5,
                5,
                BlocksMatched: false,
                WarningMessage: "Warning: blocks 5 and 6 differ; using block 5.");
        }

        if (valid6)
        {
            return new RideReadResult(
                RideReadStatus.Success,
                rides6,
                block6,
                6,
                BlocksMatched: false,
                WarningMessage: "Warning: blocks 5 and 6 differ; using block 6.");
        }

        return ResolveFailure(block5, block6);
    }

    private static RideReadResult ResolveMatching(T55Block block)
    {
        if (!TokenBlockUtils.Families.TryGetFamilyFromBlock(block, out _))
        {
            return new RideReadResult(
                RideReadStatus.UnknownEncodingFamily,
                null,
                block,
                5,
                BlocksMatched: true,
                WarningMessage: null);
        }

        if (!TryValidate(block, out var rides))
        {
            return new RideReadResult(
                RideReadStatus.InvalidBlockFormat,
                null,
                block,
                5,
                BlocksMatched: true,
                WarningMessage: null);
        }

        return new RideReadResult(
            RideReadStatus.Success,
            rides,
            block,
            5,
            BlocksMatched: true,
            WarningMessage: null);
    }

    private static RideReadResult ResolveFailure(T55Block block5, T55Block block6)
    {
        var family5 = TokenBlockUtils.Families.TryGetFamilyFromBlock(block5, out _);
        var family6 = TokenBlockUtils.Families.TryGetFamilyFromBlock(block6, out _);

        if (!family5 || !family6)
        {
            return new RideReadResult(
                RideReadStatus.UnknownEncodingFamily,
                null,
                block5,
                5,
                BlocksMatched: false,
                WarningMessage: null);
        }

        return new RideReadResult(
            RideReadStatus.InvalidBlockFormat,
            null,
            block5,
            5,
            BlocksMatched: false,
            WarningMessage: null);
    }

    internal static bool TryValidate(T55Block block, out uint rides)
    {
        rides = 0;
        if (!TokenBlockUtils.Families.TryGetFamilyFromBlock(block, out _))
            return false;

        if (!TokenBlockUtils.TryDecode(block, out rides))
            return false;

        return rides <= MaxRides;
    }
}
