namespace RidesBridge;

/// <summary>
/// Deterministic in-process PM3 substitute for <c>--fake-pm3</c> launch mode. It never opens USB
/// and exposes mutable page-0 blocks with the same read/write semantics expected by
/// <see cref="Page0ConditionalWriter"/>.
/// </summary>
public sealed class FakePm3Device : IBridgePm3Device
{
    /// <summary>Venus sequence, 180 rides remaining (<c>EncodingSequences.Venus.Encode(180)</c>).</summary>
    public const string SeedBlock5Hex = "BBC7FD03";
    public const string SeedBlock6Hex = "BBC7FD03";
    public const string SeedBlock4Hex = "D6D1C733";
    public const string SeedSequenceName = "venus";
    public const int SeedRidesRemaining = 180;
    public const int SeedSignalMillivolts = 420;

    /// <summary>Unknown mirrors used by deterministic missing-block dump tests.</summary>
    public const string UnknownSeedBlock5Hex = "DEADBEEF";
    public const string UnknownSeedBlock6Hex = "FACECAFE";
    public const string UnknownSeedBlock4Hex = "00000004";

    private readonly object _sync = new();
    private readonly Dictionary<int, string> _blocks;
    private readonly int _signalMillivolts;
    private readonly bool _failTune;
    private readonly bool _failScanReads;
    private readonly int? _cancelMissingOnBlock;
    private readonly bool _noChip;
    private bool _disposed;

    private FakePm3Device(
        IReadOnlyDictionary<int, string> blocks,
        int signalMillivolts,
        bool failTune = false,
        bool failScanReads = false,
        int? cancelMissingOnBlock = null,
        bool noChip = false)
    {
        _blocks = blocks.ToDictionary(pair => pair.Key, pair => pair.Value);
        _signalMillivolts = signalMillivolts;
        _failTune = failTune;
        _failScanReads = failScanReads;
        _cancelMissingOnBlock = cancelMissingOnBlock;
        _noChip = noChip;
    }

    public static FakePm3Device CreateSeeded() => CreateKnownVenusSeeded();

    /// <summary>Empty antenna: chip-dependent reads throw <see cref="BridgeHardwareError.NoChip"/>.</summary>
    public static FakePm3Device CreateNoChip() => new(new Dictionary<int, string>(), signalMillivolts: 0, noChip: true);

    public static FakePm3Device CreateKnownVenusSeeded() => new(KnownVenusBlocks(), SeedSignalMillivolts);

    public static FakePm3Device CreateUnknownMirrorsSeeded() => new(UnknownMirrorsBlocks(), SeedSignalMillivolts);

    /// <summary>Venus identity with 180-ride mirrors; reset to 0 rides targets only blocks 5/6.</summary>
    public static FakePm3Device CreateVenusMirrorsOnlyResetSeeded() => CreateKnownVenusSeeded();

    /// <summary>Wrong block 1 with Venus mirrors; full blocks 1..6 reset is required.</summary>
    public static FakePm3Device CreateVenusIdentityMismatchSeeded() => new(VenusIdentityMismatchBlocks(), SeedSignalMillivolts);

    internal static FakePm3Device CreateFailingTune() => new(KnownVenusBlocks(), SeedSignalMillivolts, failTune: true);

    internal static FakePm3Device CreateFailingScanReads() => new(KnownVenusBlocks(), SeedSignalMillivolts, failScanReads: true);

    internal static FakePm3Device CreateCancelMissingOnBlock(int block) =>
        new(UnknownMirrorsBlocks(), SeedSignalMillivolts, cancelMissingOnBlock: block);

    public Task StartAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        return Task.CompletedTask;
    }

    public Task<string> ReadPage0Block5Async(CancellationToken ct = default) => ReadBlockAsync(5, ct);

    public Task<string> ReadPage0Block6Async(CancellationToken ct = default) => ReadBlockAsync(6, ct);

    public async Task<(string Block5Hex, string Block6Hex)> ReadPage0MirrorAsync(CancellationToken ct = default)
    {
        var block5 = await ReadPage0Block5Async(ct).ConfigureAwait(false);
        var block6 = await ReadPage0Block6Async(ct).ConfigureAwait(false);
        return (block5, block6);
    }

    public Task<Page0ScanReadResult> ScanPage0Async(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_sync)
        {
            ThrowIfDisposed();
            ThrowIfNoChip();
            if (_failTune)
                throw new BridgeHardwareException(BridgeHardwareError.TuneFailed, "Fake PM3 tune failed.");
            if (_failScanReads)
                throw new BridgeHardwareException(BridgeHardwareError.ReadFailed, "Fake PM3 scan read failed.");

            return Task.FromResult(new Page0ScanReadResult(
                _blocks[4],
                _blocks[5],
                _blocks[6],
                _signalMillivolts));
        }
    }

    public Task<IReadOnlyList<Page0BlockReadResult>> ReadPage0MissingBlocksAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_sync)
        {
            ThrowIfDisposed();
            ThrowIfNoChip();
            var results = new List<Page0BlockReadResult>(Page0MissingBlocks.Allowlist.Length);
            foreach (var block in Page0MissingBlocks.Allowlist)
            {
                if (_cancelMissingOnBlock == block)
                    throw new OperationCanceledException();
                results.Add(new Page0BlockReadResult(block, _blocks[block]));
            }
            return Task.FromResult((IReadOnlyList<Page0BlockReadResult>)results);
        }
    }

    public Task WritePage0Block5Async(string value, CancellationToken ct = default) => WriteBlockAsync(5, value, ct);

    public Task WritePage0Block6Async(string value, CancellationToken ct = default) => WriteBlockAsync(6, value, ct);

    public Task<string> ReadPage0Block1To6Async(int block, CancellationToken ct = default)
    {
        if (block is < 1 or > 6)
            throw new ArgumentOutOfRangeException(nameof(block), "Only page-0 blocks 1 through 6 can be read.");
        return ReadBlockAsync(block, ct);
    }

    public Task WritePage0Block1To6Async(int block, string value, CancellationToken ct = default)
    {
        if (block is < 1 or > 6)
            throw new ArgumentOutOfRangeException(nameof(block), "Only page-0 blocks 1 through 6 can be written.");
        return WriteBlockAsync(block, value, ct);
    }

    public Task<IReadOnlyList<Page0BlockReadResult>> ReadPage0Blocks1To6Async(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_sync)
        {
            ThrowIfDisposed();
            ThrowIfNoChip();
            var results = new List<Page0BlockReadResult>(Page0Blocks1To6.Allowlist.Length);
            foreach (var block in Page0Blocks1To6.Allowlist)
                results.Add(new Page0BlockReadResult(block, _blocks[block]));
            return Task.FromResult((IReadOnlyList<Page0BlockReadResult>)results);
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_sync)
            _disposed = true;
        return ValueTask.CompletedTask;
    }

    private Task<string> ReadBlockAsync(int block, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_sync)
        {
            ThrowIfDisposed();
            ThrowIfNoChip();
            return Task.FromResult(_blocks[block]);
        }
    }

    private Task WriteBlockAsync(int block, string value, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_sync)
        {
            ThrowIfDisposed();
            ThrowIfNoChip();
            _blocks[block] = NormalizeBlockHex(value);
        }
        return Task.CompletedTask;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(FakePm3Device));
    }

    private void ThrowIfNoChip()
    {
        if (_noChip)
            throw new BridgeHardwareException(BridgeHardwareError.NoChip, "No supported T55xx chip is present.");
    }

    private static string NormalizeBlockHex(string value)
    {
        if (!Page0MutationValidator.TryNormalizeBlockHex(value, out var normalized))
            throw new BridgeHardwareException(BridgeHardwareError.MalformedResponse, "Fake PM3 received a malformed block value.");
        return normalized;
    }

    private static Dictionary<int, string> KnownVenusBlocks() => new()
    {
        [0] = "00148040",
        [1] = "43FE0062",
        [2] = "5BA494A3",
        [3] = "D6D1C733",
        [4] = SeedBlock4Hex,
        [5] = SeedBlock5Hex,
        [6] = SeedBlock6Hex,
        [7] = "00000000",
    };

    private static Dictionary<int, string> UnknownMirrorsBlocks() => new()
    {
        [0] = "00148040",
        [1] = "00000001",
        [2] = "00000002",
        [3] = "00000003",
        [4] = UnknownSeedBlock4Hex,
        [5] = UnknownSeedBlock5Hex,
        [6] = UnknownSeedBlock6Hex,
        [7] = "00000000",
    };

    private static Dictionary<int, string> VenusIdentityMismatchBlocks() => new()
    {
        [0] = "00148040",
        [1] = "21FF0031",
        [2] = "5BA494A3",
        [3] = "D6D1C733",
        [4] = SeedBlock4Hex,
        [5] = SeedBlock5Hex,
        [6] = SeedBlock6Hex,
        [7] = "00000000",
    };
}
