using Tokens;

namespace RidesCli.Tests;

/// <summary>
/// Functional fake that maintains a T55xxImage. Read/Write operate on the image state.
/// </summary>
public sealed class FakeRidesPm3Api : IRidesPm3Api
{
    private T55xxImage _image;

    public string DumpResult { get; set; } = "raw dump output";
    public uint SignalStrengthMv { get; set; } = 420;
    public bool TokenPresent { get; set; } = true;

    private FakeRidesPm3Api(T55xxImage image)
    {
        _image = image;
    }

    public FakeRidesPm3Api() : this(new T55xxImage(CreatePage0Blocks(0)))
    {
    }

    public static FakeRidesPm3Api WithRides(uint rides)
    {
        var blocks = CreatePage0Blocks(rides);
        return new FakeRidesPm3Api(new T55xxImage(blocks));
    }

    public static FakeRidesPm3Api WithInvalidBlock5()
    {
        var blocks = CreatePage0Blocks(0);
        var block = new T55Block(0xCCC70000); // known family header, invalid payload for TokenBlockUtils.Decode
        var image = new T55xxImage(blocks);
        image.SetBlock(0, 5, block);
        image.SetBlock(0, 6, block);
        return new FakeRidesPm3Api(image);
    }

    public static FakeRidesPm3Api WithUnknownFamilyBlock5()
    {
        var blocks = CreatePage0Blocks(0);
        var block = new T55Block(0xDEAD1234); // unknown high16, but still dumpable
        var image = new T55xxImage(blocks);
        image.SetBlock(0, 5, block);
        image.SetBlock(0, 6, block);
        return new FakeRidesPm3Api(image);
    }

    private static List<T55Block> CreatePage0Blocks(uint rides)
    {
        var encoded = TokenBlockUtils.Encode(rides);
        var blocks = new List<T55Block>();
        for (var i = 0; i < 8; i++)
            blocks.Add(i == 5 || i == 6 ? encoded : new T55Block(0));
        return blocks;
    }

    /// <summary>Get current rides from the fake token (for test assertions).</summary>
    public uint GetRides()
    {
        var block5 = _image.GetBlock(0, 5);
        return TokenBlockUtils.Decode(block5);
    }

    public string GetBlockHex(int block) => _image.GetBlock(0, block).ToHex();

    public void RemoveToken() => TokenPresent = false;

    public Task<bool> TryDetectTokenAsync(CancellationToken ct = default) =>
        Task.FromResult(TokenPresent);

    public Task<string> ReadPage0BlockAsync(uint block, CancellationToken ct = default)
    {
        EnsureTokenPresent();
        var b = _image.GetBlock(0, (int)block);
        return Task.FromResult(b.ToHex());
    }

    public Task WritePage0BlockAsync(uint block, T55Block data, CancellationToken ct = default)
    {
        EnsureTokenPresent();
        _image.SetBlock(0, (int)block, data);
        return Task.CompletedTask;
    }

    /// <summary>Simulate placing a new token on the reader with different ride count.</summary>
    public void SimulateNewToken(uint rides)
    {
        _image = new T55xxImage(CreatePage0Blocks(rides));
        TokenPresent = true;
    }

    public Task<string> DumpAsync(CancellationToken ct = default)
    {
        EnsureTokenPresent();
        return Task.FromResult(DumpResult);
    }

    public Task<uint> GetSignalStrengthMvAsync(CancellationToken ct = default) =>
        Task.FromResult(SignalStrengthMv);

    private void EnsureTokenPresent()
    {
        if (!TokenPresent)
            throw new InvalidOperationException("No T55xx chip detected. Place a tag on the reader.");
    }
}
