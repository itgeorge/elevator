namespace Tokens;

/// <summary>A registered ride-count encoding defined by one zero block and counter layout.</summary>
public sealed class EncodingSequence
{
    public EncodingSequence(string friendlyName, T55Block zeroBlock, byte rotation, uint minRides, uint maxRides)
    {
        if (string.IsNullOrWhiteSpace(friendlyName))
            throw new ArgumentException("Friendly name is required.", nameof(friendlyName));
        if (rotation > 7)
            throw new ArgumentOutOfRangeException(nameof(rotation), rotation, "Rotation must be in [0, 7].");
        if (minRides > maxRides)
            throw new ArgumentException("Minimum rides cannot exceed maximum rides.", nameof(minRides));
        if (maxRides > RideCounterCodec.MaxCounter)
            throw new ArgumentOutOfRangeException(nameof(maxRides), maxRides, $"Maximum rides must be in [0, {RideCounterCodec.MaxCounter}].");

        FriendlyName = friendlyName.Trim().ToLowerInvariant();
        ZeroBlock = zeroBlock;
        Rotation = rotation;
        MinRides = minRides;
        MaxRides = maxRides;
    }

    public string FriendlyName { get; }
    public T55Block ZeroBlock { get; }
    public byte Rotation { get; }
    public uint MinRides { get; }
    public uint MaxRides { get; }

    public T55Block Encode(uint rides)
    {
        if (rides < MinRides || rides > MaxRides)
            throw new ArgumentOutOfRangeException(nameof(rides), rides,
                $"Rides remaining {rides} is out of range [{MinRides}, {MaxRides}] for encoding sequence '{FriendlyName}'.");

        return RideCounterCodec.Encode(ZeroBlock, Rotation, rides);
    }

    public bool TryDecode(T55Block block, out uint rides)
    {
        if (!RideCounterCodec.TryDecode(ZeroBlock, Rotation, block, out rides))
            return false;

        if (rides < MinRides || rides > MaxRides)
        {
            rides = 0;
            return false;
        }

        return true;
    }
}

public static class EncodingSequences
{
    public static readonly EncodingSequence Mercury = new("mercury", new T55Block(0xCCC749CC), 4, 0, 500);
    public static readonly EncodingSequence Venus = new("venus", new T55Block(0x48C74948), 4, 0, 500);
    public static readonly EncodingSequence Earth = new("earth", new T55Block(0x18121218), 4, 0, 500);
    public static readonly EncodingSequence Pluto = new("pluto", new T55Block(0x1F12121F), 4, 0, 500);
    public static readonly EncodingSequence Mars = new("mars", new T55Block(0x4EC7494E), 4, 0, 500);
    public static readonly EncodingSequence Jupiter = new("jupiter", new T55Block(0x8C124980), 0, 0, 500);
    public static readonly EncodingSequence Saturn = new("saturn", new T55Block(0x8B1249F0), 0, 0, 500);

    public static IReadOnlyList<EncodingSequence> All { get; } = BuildRegistry([Mercury, Venus, Earth, Pluto, Mars, Jupiter, Saturn]);

    private static IReadOnlyList<EncodingSequence> BuildRegistry(IReadOnlyList<EncodingSequence> sequences)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var blocks = new Dictionary<uint, (EncodingSequence Sequence, uint Rides)>();
        foreach (var sequence in sequences)
        {
            if (!names.Add(sequence.FriendlyName))
                throw new InvalidOperationException($"Duplicate encoding sequence name '{sequence.FriendlyName}'.");

            for (var rides = sequence.MinRides; rides <= sequence.MaxRides; rides++)
            {
                var block = sequence.Encode(rides).Value;
                if (!blocks.TryAdd(block, (sequence, rides)))
                {
                    var other = blocks[block];
                    throw new InvalidOperationException(
                        $"Encoding collision: {sequence.FriendlyName}/{rides} and {other.Sequence.FriendlyName}/{other.Rides} encode as {block:X8}.");
                }
            }
        }

        return sequences;
    }

    public static bool TryGetByFriendlyName(string friendlyName, out EncodingSequence? sequence)
    {
        sequence = null;
        if (string.IsNullOrWhiteSpace(friendlyName))
            return false;

        sequence = All.SingleOrDefault(candidate =>
            string.Equals(candidate.FriendlyName, friendlyName.Trim(), StringComparison.OrdinalIgnoreCase));
        return sequence is not null;
    }

    /// <summary>Matches a block against every registered sequence using complete structural validation.</summary>
    public static bool TryDecode(T55Block block, out EncodingSequence? sequence, out uint rides)
    {
        sequence = null;
        rides = 0;
        foreach (var candidate in All)
        {
            if (!candidate.TryDecode(block, out var candidateRides))
                continue;

            if (sequence is not null)
                throw new InvalidOperationException($"Block {block.ToHex()} ambiguously matches '{sequence.FriendlyName}' and '{candidate.FriendlyName}'.");

            sequence = candidate;
            rides = candidateRides;
        }

        return sequence is not null;
    }

    public static bool TryGetSequenceFromBlock(T55Block block, out EncodingSequence? sequence) =>
        TryDecode(block, out sequence, out _);


    public static EncodingSequence GetSequenceFromBlock(T55Block block)
    {
        if (TryGetSequenceFromBlock(block, out var sequence) && sequence is not null)
            return sequence;

        throw new ArgumentException($"Block {block.ToHex()} does not match a registered encoding sequence.", nameof(block));
    }

    public static string FormatKnownFriendlyNames() =>
        string.Join(", ", All.Select(sequence => sequence.FriendlyName));
}
