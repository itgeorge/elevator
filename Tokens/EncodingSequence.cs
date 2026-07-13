namespace Tokens;

/// <summary>
/// Canonical family instances. <see cref="EncodingSequences"/> is the source of truth for which families are registered.
/// </summary>
internal static class EncodingFamilyDefinitions
{
    internal static readonly TokenBlockUtils.Family Mercury0To127 = new(0xCCC7, 0x0000, 0);
    internal static readonly TokenBlockUtils.Family Mercury128To255 = new(0x3FC7, 0x8008, 128);
    internal static readonly TokenBlockUtils.Family Mercury256To383 = new(0xCCC6, 0x0010, 256);
    internal static readonly TokenBlockUtils.Family Mercury384To500 = new(0x3FC6, 0x8018, 384);

    // 43FE0062-5BA494A3-D6D1C733-D6D1C733 (Venus): captured 0..180, high range elevator-validated 2026-07-12
    internal static readonly TokenBlockUtils.Family Venus0To127 = new(0x48C7, 0x0084, 0);
    internal static readonly TokenBlockUtils.Family Venus128To255 = new(0xBBC7, 0x808C, 128);
    internal static readonly TokenBlockUtils.Family Venus256To383 = new(0x48C6, 0x0094, 256);
    internal static readonly TokenBlockUtils.Family Venus384To500 = new(0xBBC6, 0x809C, 384);

    // D3FE005D-522BC69D-650432F5-650432F5 (Earth): low range captured 0..23; 128 and 255 boundary starts elevator-validated 2026-07-12.
    internal static readonly TokenBlockUtils.Family Earth0To127 = new(0x1812, 0x5BD4, 0);
    internal static readonly TokenBlockUtils.Family Earth128To255 = new(0xEB12, 0xDBDC, 128);
}

/// <summary>
/// A ride-count range within an <see cref="EncodingSequence"/>, mapped to one encoding family.
/// </summary>
public sealed record EncodingSequenceSegment(uint MinRides, uint MaxRides, TokenBlockUtils.Family Family)
{
    public bool Contains(uint ridesRemaining) =>
        ridesRemaining >= MinRides && ridesRemaining <= MaxRides;
}

/// <summary>
/// A token ride-count encoding sequence: one or more families that cover ride ranges
/// and handle transitions between them (e.g. 0..127 and 128..255).
/// </summary>
public sealed class EncodingSequence
{
    private readonly EncodingSequenceSegment[] _segments;
    private readonly string _resetImageFileName;

    internal EncodingSequence(
        string friendlyName,
        string resetImageFileName,
        params EncodingSequenceSegment[] segments)
    {
        if (string.IsNullOrWhiteSpace(friendlyName))
            throw new ArgumentException("Friendly name is required.", nameof(friendlyName));
        if (string.IsNullOrWhiteSpace(resetImageFileName))
            throw new ArgumentException("Reset image file name is required.", nameof(resetImageFileName));
        if (segments is null || segments.Length == 0)
            throw new ArgumentException("At least one segment is required.", nameof(segments));

        foreach (var segment in segments)
        {
            if (segment.MinRides > segment.MaxRides)
            {
                throw new ArgumentException(
                    $"Segment min rides {segment.MinRides} exceeds max rides {segment.MaxRides}.",
                    nameof(segments));
            }
        }

        FriendlyName = friendlyName.Trim().ToLowerInvariant();
        _resetImageFileName = resetImageFileName.Trim();
        _segments = segments;
        MinRides = _segments.Min(segment => segment.MinRides);
        MaxRides = _segments.Max(segment => segment.MaxRides);
    }

    public string FriendlyName { get; }

    public string ResetImageFileName => _resetImageFileName;

    public uint MinRides { get; }

    public uint MaxRides { get; }

    public IReadOnlyList<EncodingSequenceSegment> Segments => _segments;

    public TokenBlockUtils.Family GetFamilyForRides(uint ridesRemaining)
    {
        foreach (var segment in _segments)
        {
            if (segment.Contains(ridesRemaining))
            {
                return segment.Family;
            }
        }

        throw new ArgumentException(
            $"Rides remaining {ridesRemaining} is out of range for encoding sequence '{FriendlyName}'");
    }

    public T55Block Encode(uint ridesRemaining) =>
        TokenBlockUtils.EncodeByFamily(ridesRemaining, GetFamilyForRides(ridesRemaining));

    internal IEnumerable<TokenBlockUtils.Family> EnumerateFamilies()
    {
        foreach (var segment in _segments)
        {
            yield return segment.Family;
        }
    }
}

public static class EncodingSequences
{
    public static readonly EncodingSequence Mercury = new(
        "mercury",
        "default-500-rides.bin",
        new EncodingSequenceSegment(0, 127, EncodingFamilyDefinitions.Mercury0To127),
        new EncodingSequenceSegment(128, 255, EncodingFamilyDefinitions.Mercury128To255),
        new EncodingSequenceSegment(256, 383, EncodingFamilyDefinitions.Mercury256To383),
        new EncodingSequenceSegment(384, 500, EncodingFamilyDefinitions.Mercury384To500));

    public static readonly EncodingSequence Venus = new(
        "venus",
        "venus-0-rides.bin",
        new EncodingSequenceSegment(0, 127, EncodingFamilyDefinitions.Venus0To127),
        new EncodingSequenceSegment(128, 255, EncodingFamilyDefinitions.Venus128To255),
        new EncodingSequenceSegment(256, 383, EncodingFamilyDefinitions.Venus256To383),
        new EncodingSequenceSegment(384, 500, EncodingFamilyDefinitions.Venus384To500));

    public static readonly EncodingSequence Earth = new(
        "earth",
        "earth-0-rides.bin",
        new EncodingSequenceSegment(0, 127, EncodingFamilyDefinitions.Earth0To127),
        new EncodingSequenceSegment(128, 255, EncodingFamilyDefinitions.Earth128To255));

    public static IReadOnlyList<EncodingSequence> All { get; } = [Mercury, Venus, Earth];

    private static readonly Dictionary<uint, EncodingSequence> SequenceByHigh16 = BuildSequenceByHigh16();

    private static Dictionary<uint, EncodingSequence> BuildSequenceByHigh16()
    {
        var map = new Dictionary<uint, EncodingSequence>();
        foreach (var sequence in All)
        {
            foreach (var family in sequence.EnumerateFamilies())
            {
                map[family.High16] = sequence;
            }
        }

        return map;
    }

    public static bool TryGetByFriendlyName(string friendlyName, out EncodingSequence? sequence)
    {
        sequence = null;
        if (string.IsNullOrWhiteSpace(friendlyName))
        {
            return false;
        }

        var normalized = friendlyName.Trim().ToLowerInvariant();
        foreach (var candidate in All)
        {
            if (candidate.FriendlyName == normalized)
            {
                sequence = candidate;
                return true;
            }
        }

        return false;
    }

    public static bool TryGetSequenceFromBlock(T55Block block, out EncodingSequence? sequence)
    {
        uint high16 = block.Value >> 16;
        if (SequenceByHigh16.TryGetValue(high16, out var found))
        {
            sequence = found;
            return true;
        }

        sequence = null;
        return false;
    }

    public static EncodingSequence GetSequenceFromBlock(T55Block block)
    {
        if (TryGetSequenceFromBlock(block, out var sequence) && sequence is not null)
        {
            return sequence;
        }

        uint high16 = block.Value >> 16;
        throw new ArgumentException($"Block {block.Value:X8} uses an unknown encoding sequence (high 16 {high16:X4})");
    }

    public static string FormatKnownFriendlyNames() =>
        string.Join(", ", All.Select(s => s.FriendlyName));
}
