namespace ElevatorCli.IO;

public interface IBlockReader
{
    string FormatId { get; }
    IReadOnlyList<uint> ReadBlocks(string path);
}


