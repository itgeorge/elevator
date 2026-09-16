using System.Security.Cryptography;

namespace Tokens;

public static class ApartmentBlockCodec
{
    private static readonly byte[] VersionLabel = "apt-v1"u8.ToArray();
    private static readonly byte[] MaskLabel = "mask"u8.ToArray();
    private static readonly byte[] SealLabel = "seal"u8.ToArray();

    public static T55Block Encode(ReadOnlySpan<byte> secret, T55Block block3, byte building, byte apt)
    {
        var plaintext = PackPlaintext(building, apt);
        var mask = ComputeTrunc16(secret, MaskLabel, block3);
        var seal = ComputeTrunc16(secret, SealLabel, block3, plaintext);
        var obfuscated = (ushort)(plaintext ^ mask);
        return new T55Block(((uint)seal << 16) | obfuscated);
    }

    public static bool TryDecode(ReadOnlySpan<byte> secret, T55Block block3, T55Block block4, out ApartmentPayload payload)
    {
        payload = default;
        var seal = (ushort)(block4.Value >> 16);
        var obfuscated = (ushort)block4.Value;
        var mask = ComputeTrunc16(secret, MaskLabel, block3);
        var plaintext = (ushort)(obfuscated ^ mask);
        var expectedSeal = ComputeTrunc16(secret, SealLabel, block3, plaintext);
        if (seal != expectedSeal)
            return false;

        payload = UnpackPlaintext(plaintext);
        return true;
    }

    private static ushort PackPlaintext(byte building, byte apt) =>
        (ushort)((building << 8) | apt);

    private static ApartmentPayload UnpackPlaintext(ushort plaintext) =>
        new((byte)(plaintext >> 8), (byte)plaintext);

    private static ushort ComputeTrunc16(ReadOnlySpan<byte> secret, ReadOnlySpan<byte> label, T55Block block3)
    {
        Span<byte> message = stackalloc byte[label.Length + VersionLabel.Length + 4];
        WritePrefix(message, label, block3, out var length);
        return Trunc16Hmac(secret, message[..length]);
    }

    private static ushort ComputeTrunc16(ReadOnlySpan<byte> secret, ReadOnlySpan<byte> label, T55Block block3, ushort plaintext)
    {
        Span<byte> message = stackalloc byte[label.Length + VersionLabel.Length + 4 + 2];
        WritePrefix(message, label, block3, out var length);
        message[length++] = (byte)(plaintext >> 8);
        message[length++] = (byte)plaintext;
        return Trunc16Hmac(secret, message[..length]);
    }

    private static void WritePrefix(Span<byte> message, ReadOnlySpan<byte> label, T55Block block3, out int length)
    {
        label.CopyTo(message);
        VersionLabel.CopyTo(message[label.Length..]);
        WriteBlock3Bytes(block3, message[(label.Length + VersionLabel.Length)..]);
        length = label.Length + VersionLabel.Length + 4;
    }

    private static void WriteBlock3Bytes(T55Block block3, Span<byte> destination)
    {
        var value = block3.Value;
        destination[0] = (byte)(value >> 24);
        destination[1] = (byte)(value >> 16);
        destination[2] = (byte)(value >> 8);
        destination[3] = (byte)value;
    }

    private static ushort Trunc16Hmac(ReadOnlySpan<byte> secret, ReadOnlySpan<byte> message)
    {
        Span<byte> digest = stackalloc byte[32];
        HMACSHA256.HashData(secret, message, digest);
        return (ushort)((digest[0] << 8) | digest[1]);
    }
}
