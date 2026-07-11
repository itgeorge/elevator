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

    public int DumpCallCount { get; private set; }
    public int TuneCallCount { get; private set; }

    public static FakeRidesPm3Api WithRidesEncodedByFamily(uint rides, TokenBlockUtils.Family family)
    {
        var blocks = CreatePage0Blocks(0);
        var encoded = TokenBlockUtils.EncodeByFamily(rides, family);
        blocks[5] = encoded;
        blocks[6] = encoded;
        return new FakeRidesPm3Api(new T55xxImage(blocks));
    }

    public static FakeRidesPm3Api WithMismatchedRides(uint rides5, uint rides6)
    {
        var blocks = CreatePage0Blocks(0);
        blocks[5] = TokenBlockUtils.Encode(rides5, EncodingSequences.Mercury);
        blocks[6] = TokenBlockUtils.Encode(rides6, EncodingSequences.Mercury);
        return new FakeRidesPm3Api(new T55xxImage(blocks));
    }

    public static FakeRidesPm3Api WithBlocks5And6(T55Block block5, T55Block block6)
    {
        var blocks = CreatePage0Blocks(0);
        blocks[5] = block5;
        blocks[6] = block6;
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
        var encoded = TokenBlockUtils.Encode(rides, EncodingSequences.Mercury);
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
        DumpCallCount++;
        EnsureTokenPresent();
        return Task.FromResult(DumpResult);
    }

    public Task<(string Block5Hex, string Block6Hex)> ReadRideMirrorBlocksAsync(CancellationToken ct = default)
    {
        EnsureTokenPresent();
        return Task.FromResult((GetBlockHex(5), GetBlockHex(6)));
    }

    public async Task<bool> WriteRideMirrorBlocksAsync(T55Block data, CancellationToken ct = default)
    {
        EnsureTokenPresent();
        var expected = data.ToHex();
        for (var attempt = 0; attempt < 3; attempt++)
        {
            await WritePage0BlockAsync(5, data, ct);
            await WritePage0BlockAsync(6, data, ct);
            var read5 = await ReadPage0BlockAsync(5, ct);
            var read6 = await ReadPage0BlockAsync(6, ct);
            if (read5 == expected && read6 == expected)
                return true;
        }

        return false;
    }

    public async Task<bool> WriteAndVerifyPage0BlocksAsync(
        IReadOnlyList<T55Block> blocks,
        int firstBlock,
        int lastBlock,
        CancellationToken ct = default)
    {
        EnsureTokenPresent();
        var confirmed = new bool[lastBlock - firstBlock + 1];

        for (var attempt = 0; attempt < 3; attempt++)
        {
            for (var block = firstBlock; block <= lastBlock; block++)
            {
                var index = block - firstBlock;
                if (!confirmed[index])
                    await WritePage0BlockAsync((uint)block, blocks[block], ct);
            }

            for (var block = firstBlock; block <= lastBlock; block++)
            {
                var index = block - firstBlock;
                var readBack = await ReadPage0BlockAsync((uint)block, ct);
                confirmed[index] = readBack == blocks[block].ToHex();
            }

            if (confirmed.All(x => x))
                return true;
        }

        return false;
    }

    public Task<uint> GetSignalStrengthMvAsync(CancellationToken ct = default)
    {
        TuneCallCount++;
        return Task.FromResult(SignalStrengthMv);
    }

    public Task<string> RunLfTuneProbeAsync(
        string label,
        int? sampleCount = null,
        TimeSpan? timeout = null,
        string? outputDirectory = null,
        CancellationToken ct = default) =>
        Task.FromResult(Path.Combine(outputDirectory ?? "debug/lf-tune-probes", $"fake-{label}.json"));

    private void EnsureTokenPresent()
    {
        if (!TokenPresent)
            throw new InvalidOperationException("No T55xx chip detected. Place a tag on the reader.");
    }
}
