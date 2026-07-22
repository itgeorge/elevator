namespace Tokens;

/// <summary>Convenience operations over the registered generalized ride encodings.</summary>
public static class TokenBlockUtils
{
    public static T55Block Encode(uint ridesRemaining, EncodingSequence sequence)
    {
        ArgumentNullException.ThrowIfNull(sequence);
        return sequence.Encode(ridesRemaining);
    }

    public static T55Block EncodePreservingSequence(uint ridesRemaining, T55Block referenceBlock)
    {
        var sequence = EncodingSequences.GetSequenceFromBlock(referenceBlock);
        return sequence.Encode(ridesRemaining);
    }

    public static bool TryDecode(T55Block block, out uint ridesRemaining)
    {
        if (EncodingSequences.TryDecode(block, out _, out ridesRemaining))
            return true;

        ridesRemaining = 0;
        return false;
    }

    public static uint Decode(T55Block block)
    {
        if (EncodingSequences.TryDecode(block, out _, out var rides))
            return rides;

        throw new ArgumentException($"Block {block.ToHex()} does not match a registered encoding sequence.", nameof(block));
    }
}
