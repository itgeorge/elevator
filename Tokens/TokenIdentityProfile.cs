namespace Tokens;

/// <summary>
/// Fixed token identity blocks (1..4) and optional reset image metadata, associated with a ride encoding sequence.
/// </summary>
public sealed record TokenIdentityProfile(
    string FriendlyName,
    EncodingSequence RideSequence,
    T55Block Block1,
    T55Block Block2,
    T55Block Block3,
    T55Block Block4,
    string? ResetImageFileName = null)
{
    public string TokenId => $"{Block1.ToHex()}-{Block2.ToHex()}-{Block3.ToHex()}-{Block4.ToHex()}";

    public bool CanReset => !string.IsNullOrWhiteSpace(ResetImageFileName);
}

public static class TokenIdentityProfiles
{
    public static readonly TokenIdentityProfile Mercury = Create(
        "mercury",
        EncodingSequences.Mercury,
        "9BFE0062-5BA4A3DE-D5D1D713-D5D1D713",
        "default-500-rides.bin");

    public static readonly TokenIdentityProfile Venus = Create(
        "venus",
        EncodingSequences.Venus,
        "43FE0062-5BA494A3-D6D1C733-D6D1C733",
        "venus-0-rides.bin");

    public static readonly TokenIdentityProfile Earth = Create(
        "earth",
        EncodingSequences.Earth,
        "D3FE005D-522BC69D-650432F5-650432F5",
        "earth-0-rides.bin");

    public static readonly TokenIdentityProfile Pluto = Create(
        "pluto",
        EncodingSequences.Pluto,
        "83FE002A-F100C064-A3045930-A3045930",
        "pluto-0-rides.bin");

    public static readonly TokenIdentityProfile Mars = Create(
        "mars",
        EncodingSequences.Mars,
        "C3FE0031-20C60722-B6D14924-B6D14924",
        "mars-0-rides.bin");

    public static readonly TokenIdentityProfile Jupiter = Create(
        "jupiter",
        EncodingSequences.Jupiter,
        "EBFE002A-F100CC5B-A5045936-A5045936",
        "jupiter-0-rides.bin");

    public static readonly TokenIdentityProfile Saturn = Create(
        "saturn",
        EncodingSequences.Saturn,
        "23FE007B-D88CBD8A-5D04593D-5D04593D",
        "saturn-0-rides.bin");

    public static readonly TokenIdentityProfile Uranus = Create(
        "uranus",
        EncodingSequences.Uranus,
        "FBFE002A-F1003C92-F5D1D766-F5D1D766",
        "uranus-0-rides.bin");

    public static readonly TokenIdentityProfile Venus21Ff = Create(
        "venus21ff",
        EncodingSequences.Venus,
        "21FF0031-5BA494A3-D6D1C733-D6D1C733");

    public static readonly TokenIdentityProfile EarthA457 = Create(
        "earth-a457",
        EncodingSequences.Earth,
        "D3FE005D-A4578D3A-650432F5-650432F5");

    public static IReadOnlyList<TokenIdentityProfile> All { get; } =
        [Mercury, Venus, Earth, Pluto, Mars, Jupiter, Saturn, Uranus, Venus21Ff, EarthA457];

    public static IReadOnlyList<TokenIdentityProfile> Resettable { get; } =
        All.Where(profile => profile.CanReset).ToArray();

    private static readonly Dictionary<string, TokenIdentityProfile> ProfileByFriendlyName =
        All.ToDictionary(profile => profile.FriendlyName, profile => profile, StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, TokenIdentityProfile> ProfileByTokenId =
        All.ToDictionary(profile => profile.TokenId, profile => profile, StringComparer.OrdinalIgnoreCase);

    public static bool TryGetByFriendlyName(string friendlyName, out TokenIdentityProfile? profile)
    {
        profile = null;
        if (string.IsNullOrWhiteSpace(friendlyName))
        {
            return false;
        }

        return ProfileByFriendlyName.TryGetValue(friendlyName.Trim(), out profile);
    }

    public static bool TryGetByTokenId(string tokenId, out TokenIdentityProfile? profile)
    {
        profile = null;
        if (string.IsNullOrWhiteSpace(tokenId))
        {
            return false;
        }

        return ProfileByTokenId.TryGetValue(tokenId.Trim(), out profile);
    }

    public static string FormatKnownFriendlyNames() =>
        string.Join(", ", All.Select(profile => profile.FriendlyName));

    public static string FormatResettableFriendlyNames() =>
        string.Join(", ", Resettable.Select(profile => profile.FriendlyName));

    private static TokenIdentityProfile Create(
        string friendlyName,
        EncodingSequence rideSequence,
        string tokenId,
        string? resetImageFileName = null)
    {
        var parts = tokenId.Split('-');
        if (parts.Length != 4)
        {
            throw new ArgumentException($"Token id must contain four blocks: '{tokenId}'", nameof(tokenId));
        }

        return new TokenIdentityProfile(
            friendlyName,
            rideSequence,
            T55Block.FromHex(parts[0]),
            T55Block.FromHex(parts[1]),
            T55Block.FromHex(parts[2]),
            T55Block.FromHex(parts[3]),
            resetImageFileName);
    }
}
