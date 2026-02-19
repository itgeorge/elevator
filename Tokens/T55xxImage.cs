using System.Globalization;

namespace Tokens;

public readonly struct T55Block
{
    public uint Value { get; }

    public T55Block(uint value) => Value = value;

    public string ToHex(bool addPrefix0x = false) =>
        addPrefix0x ? $"0x{Value:X8}" : Value.ToString("X8");

    public static T55Block FromHex(string hex)
    {
        var s = hex.AsSpan().Trim();
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            s = s[2..];
        if (s.Length != 8 || !uint.TryParse(s.ToString(), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v))
            throw new ArgumentException($"Invalid hex block: '{hex}' (expected 8 hex digits)", nameof(hex));
        return new T55Block(v);
    }

    public string ToBin() => Convert.ToString(Value, 2).PadLeft(32, '0');

    public static T55Block FromBin(string bin)
    {
        var s = bin.AsSpan().Trim();
        if (s.Length != 32)
            throw new ArgumentException($"Invalid binary block: '{bin}' (expected 32 bits)", nameof(bin));
        uint v = 0;
        for (int i = 0; i < 32; i++)
        {
            char c = s[i];
            if (c != '0' && c != '1')
                throw new ArgumentException($"Invalid binary block: '{bin}' (expected only 0/1)", nameof(bin));
            v = (v << 1) | (uint)(c - '0');
        }
        return new T55Block(v);
    }
}

public class T55xxImage
{
    private readonly List<T55Block> _blocks;

    public T55xxImage(IEnumerable<T55Block> blocks, string? sourcePath = null)
    {
        _blocks = new List<T55Block>(blocks);
        SourcePath = sourcePath;
    }

    public T55xxImage(IEnumerable<uint> blocks, string? sourcePath = null)
    {
        _blocks = blocks.Select(u => new T55Block(u)).ToList();
        SourcePath = sourcePath;
    }

    public IReadOnlyList<T55Block> Blocks => _blocks;

    public string? SourcePath { get; set; }

    public int BlockCount => _blocks.Count;

    public int PageCount => (BlockCount + 7) / 8;

    public static int GetIndex(int page, int block)
    {
        if (page < 0) throw new ArgumentOutOfRangeException(nameof(page), "Page must be >= 0");
        if (block < 0 || block > 7) throw new ArgumentOutOfRangeException(nameof(block), "Block must be between 0 and 7");
        return checked(page * 8 + block);
    }

    public T55Block GetBlock(int page, int block)
    {
        var index = GetIndex(page, block);
        if (index < 0 || index >= _blocks.Count) throw new ArgumentOutOfRangeException("Requested block is out of range for this image");
        return _blocks[index];
    }

    public void SetBlock(int page, int block, T55Block value)
    {
        var index = GetIndex(page, block);
        if (index < 0 || index >= _blocks.Count) throw new ArgumentOutOfRangeException("Requested block is out of range for this image");
        if (_blocks[index].Value != value.Value)
        {
            _blocks[index] = value;
        }
    }
}
