namespace Tokens;

/// <summary>Encodes the shared nine-bit ride counter independently of a token sequence.</summary>
internal static class RideCounterCodec
{
    internal const uint MaxCounter = 511;

    internal static uint BuildDelta(uint rides, byte rotation)
    {
        ValidateRotation(rotation);
        if (rides > MaxCounter)
            throw new ArgumentOutOfRangeException(nameof(rides), rides, $"Ride counter must be in [0, {MaxCounter}].");

        var r = (byte)rides;
        var h = (byte)(rides >> 8);
        var payload = (byte)(RotateLeft(r, rotation) ^ (h << rotation));
        var firstByte = (payload & 0x08) != 0 ? 0xF3u : 0u;

        return (firstByte << 24) | ((uint)h << 16) | ((uint)r << 8) | payload;
    }

    internal static T55Block Encode(T55Block zeroBlock, byte rotation, uint rides) =>
        new(zeroBlock.Value ^ BuildDelta(rides, rotation));

    internal static bool TryDecode(T55Block zeroBlock, byte rotation, T55Block block, out uint rides)
    {
        ValidateRotation(rotation);
        var delta = block.Value ^ zeroBlock.Value;
        var high = (delta >> 16) & 0xFF;
        if (high > 1)
        {
            rides = 0;
            return false;
        }

        rides = (high << 8) | ((delta >> 8) & 0xFF);
        return BuildDelta(rides, rotation) == delta
            && Encode(zeroBlock, rotation, rides).Value == block.Value;
    }

    private static byte RotateLeft(byte value, byte rotation)
    {
        if (rotation == 0)
            return value;

        return (byte)((value << rotation) | (value >> (8 - rotation)));
    }

    private static void ValidateRotation(byte rotation)
    {
        if (rotation > 7)
            throw new ArgumentOutOfRangeException(nameof(rotation), rotation, "Rotation must be in [0, 7].");
    }
}
