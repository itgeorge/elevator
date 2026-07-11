namespace Tokens;

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
    }

    public string FriendlyName { get; }

    public string ResetImageFileName => _resetImageFileName;

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
        new EncodingSequenceSegment(0, 127, TokenBlockUtils.Families.Family0To127),
        new EncodingSequenceSegment(128, 255, TokenBlockUtils.Families.Family128To255),
        new EncodingSequenceSegment(256, 383, TokenBlockUtils.Families.Family256To383),
        new EncodingSequenceSegment(384, 500, TokenBlockUtils.Families.Family384To500));

    // 43FE0062-5BA494A3-D6D1C733-D6D1C733 (captured 0..180)
    public static readonly EncodingSequence Venus = new(
        "venus",
        "venus-0-rides.bin",
        new EncodingSequenceSegment(0, 127, TokenBlockUtils.Families.Family48C7_0To127),
        new EncodingSequenceSegment(128, 255, TokenBlockUtils.Families.FamilyBBC7_128To255));

    public static IReadOnlyList<EncodingSequence> All { get; } = [Mercury, Venus];

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
