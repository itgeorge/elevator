using Tokens;

namespace RideCaptureCli;

public static class SeededTokenCatalog
{
    private sealed record SeededTokenState(string TokenId, string Block5, string Block6, int StartingRides);

    private static readonly SeededTokenState[] SeededStartingStates =
    [
        // Historical bootstrap anchors for captures that predate full decoder support.
        // Prefer decoding block 5/6 first; these fallbacks only apply when the exact
        // observed ride-state matches the original capture state.
        new("D3FE005D-522BC69D-650432F5-650432F5", "18120569", "18120569", 24),
        new("43FE0062-5BA494A3-D6D1C733-D6D1C733", "BBC7FD03", "BBC7FD03", 181),
        new("C3FE0031-20C60722-B6D14924-B6D14924", "4EC747AE", "4EC747AE", 14),
    ];

    private static readonly HashSet<string> SeededTokenIds = SeededStartingStates
        .Select(state => state.TokenId)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public static bool TryGetStartingRides(string tokenId, string block5, string block6, out int rides)
    {
        foreach (var state in SeededStartingStates)
        {
            if (string.Equals(state.TokenId, tokenId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(state.Block5, block5, StringComparison.OrdinalIgnoreCase)
                && string.Equals(state.Block6, block6, StringComparison.OrdinalIgnoreCase))
            {
                rides = state.StartingRides;
                return true;
            }
        }

        rides = 0;
        return false;
    }

    public static bool IsKnownTokenId(string tokenId) =>
        TokenIdentityProfiles.TryGetByTokenId(tokenId, out _)
        || SeededTokenIds.Contains(tokenId);
}
