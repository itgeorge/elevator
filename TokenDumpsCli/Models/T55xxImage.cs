namespace TokenDumpsCli.Models;

public class T55xxImage
{
    private readonly List<uint> _blocks;

    public T55xxImage(IEnumerable<uint> blocks, string? sourcePath = null)
    {
        _blocks = new List<uint>(blocks);
        SourcePath = sourcePath;
    }

    public IReadOnlyList<uint> Blocks => _blocks;

    public string? SourcePath { get; set; }

    public bool IsDirty { get; private set; }

    public int BlockCount => _blocks.Count;

    public int PageCount => (BlockCount + 7) / 8;

    public static int GetIndex(int page, int block)
    {
        if (page < 0) throw new ArgumentOutOfRangeException(nameof(page), "Page must be >= 0");
        if (block < 0 || block > 7) throw new ArgumentOutOfRangeException(nameof(block), "Block must be between 0 and 7");
        return checked(page * 8 + block);
    }

    public uint GetBlock(int page, int block)
    {
        var index = GetIndex(page, block);
        if (index < 0 || index >= _blocks.Count) throw new ArgumentOutOfRangeException("Requested block is out of range for this image");
        return _blocks[index];
    }

    public void SetBlock(int page, int block, uint value)
    {
        var index = GetIndex(page, block);
        if (index < 0 || index >= _blocks.Count) throw new ArgumentOutOfRangeException("Requested block is out of range for this image");
        if (_blocks[index] != value)
        {
            _blocks[index] = value;
            IsDirty = true;
        }
    }

    public void ReplaceAllBlocks(IEnumerable<uint> newBlocks, string? sourcePath = null)
    {
        _blocks.Clear();
        _blocks.AddRange(newBlocks);
        SourcePath = sourcePath;
        IsDirty = false;
    }

    public void MarkSaved()
    {
        IsDirty = false;
    }
}

