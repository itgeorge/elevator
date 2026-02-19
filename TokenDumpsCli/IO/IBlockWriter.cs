using Tokens;

namespace TokenDumpsCli.IO;

public interface IBlockWriter
{
    string FormatId { get; }
    void WriteBlocks(string path, IReadOnlyList<T55Block> blocks);
}


