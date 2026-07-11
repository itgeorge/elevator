using System.Buffers.Binary;
using System.Reflection;
using Tokens;

namespace RidesCli;

public static class ResetPage0BlocksLoader
{
    public static List<T55Block> Load(EncodingSequence sequence)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith(sequence.ResetImageFileName, StringComparison.OrdinalIgnoreCase));
        if (resourceName is null)
        {
            throw new InvalidOperationException(
                $"Embedded reset image '{sequence.ResetImageFileName}' not found for sequence '{sequence.FriendlyName}'.");
        }

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            throw new InvalidOperationException(
                $"Failed to load embedded reset image '{sequence.ResetImageFileName}'.");
        }

        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        var bytes = ms.ToArray();
        if (bytes.Length < 32 || bytes.Length % 4 != 0)
        {
            throw new InvalidDataException(
                $"{sequence.ResetImageFileName} must contain at least 8 blocks and be a multiple of 4 bytes.");
        }

        var blocks = new List<T55Block>(8);
        for (var i = 0; i < 8 * 4; i += 4)
        {
            var word = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(i, 4));
            blocks.Add(new T55Block(word));
        }

        return blocks;
    }
}
