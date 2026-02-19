using System.Buffers.Binary;
using Tokens;

namespace TokenDumpsCli.IO;

public class BinBlockWriter : IBlockWriter
{
    public string FormatId => "bin";

    public void WriteBlocks(string path, IReadOnlyList<T55Block> blocks)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Path is required", nameof(path));
        if (blocks is null) throw new ArgumentNullException(nameof(blocks));

        var bytes = new byte[checked(blocks.Count * 4)];
        for (int i = 0; i < blocks.Count; i++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(i * 4, 4), blocks[i].Value);
        }
        File.WriteAllBytes(path, bytes);
    }
}


