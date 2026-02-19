using Tokens;

namespace TokenDumpsCli.IO;

public interface IBlockReader
{
    string FormatId { get; }
    IReadOnlyList<T55Block> ReadBlocks(string path);
}


