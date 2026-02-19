using System.Buffers.Binary;
using Tokens;

namespace TokenDumpsCli.IO;

public class BinBlockReader : IBlockReader
{
    public string FormatId => "bin";

    public IReadOnlyList<T55Block> ReadBlocks(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Path is required", nameof(path));
        if (!File.Exists(path)) throw new FileNotFoundException("File not found", path);

        var bytes = File.ReadAllBytes(path);
        if (bytes.Length % 4 != 0)
        {
            throw new InvalidDataException(".bin length must be a multiple of 4 bytes");
        }

        var blocks = new List<T55Block>(bytes.Length / 4);
        for (var i = 0; i < bytes.Length; i += 4)
        {
            var word = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(i, 4));
            blocks.Add(new T55Block(word));
        }
        return blocks;
    }
}


