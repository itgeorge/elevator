namespace RideCaptureCli;

public static class SeededTokenCatalog
{
    private static readonly IReadOnlyDictionary<string, int> SeededStartingRides = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["D3FE005D-522BC69D-650432F5-650432F5"] = 24,
        ["43FE0062-5BA494A3-D6D1C733-D6D1C733"] = 181,
        ["EBFE002A-F100CC5B-A5045936-A5045936"] = 262,
    };

    public static bool TryGetStartingRides(string tokenId, out int rides) =>
        SeededStartingRides.TryGetValue(tokenId, out rides);
}
