namespace RideCaptureCli;

public sealed class CaptureScanner
{
    private readonly IRideCapturePm3Api _pm3;
    private readonly RideCaptureConfig _config;
    private readonly CapturePaths _paths;
    private readonly ProxmarkDumpLocator _dumpLocator;

    public CaptureScanner(IRideCapturePm3Api pm3, RideCaptureConfig config, CapturePaths paths, ProxmarkDumpLocator? dumpLocator = null)
    {
        _pm3 = pm3 ?? throw new ArgumentNullException(nameof(pm3));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _dumpLocator = dumpLocator ?? new ProxmarkDumpLocator();
    }

    public async Task<CaptureScanData> ScanAsync(CancellationToken ct = default)
    {
        if (!await _pm3.TryDetectTokenAsync(ct).ConfigureAwait(false))
            throw new InvalidOperationException("No T55xx chip detected. Place a tag on the reader.");

        var signalMv = (int)await _pm3.GetSignalStrengthMvAsync(ct).ConfigureAwait(false);
        var weakSignal = signalMv > _config.MaxAcceptableSignalMv;

        var dumpStartedAt = DateTimeOffset.Now;
        var parsedDump = await _pm3.DumpParsedAsync(ct).ConfigureAwait(false);
        if (!parsedDump.Success || parsedDump.Blocks.Count < 8)
            throw new InvalidOperationException("Failed to parse at least 8 page 0 blocks from dump output.");

        var blocks = parsedDump.Blocks.Take(8).Select(b => b.ToHex()).ToList();

        var scan = new CaptureScanData
        {
            Timestamp = DateTimeOffset.Now,
            SignalMv = signalMv,
            WeakSignal = weakSignal,
            Blocks = blocks,
            RawDumpOutput = parsedDump.RawOutput,
            CopiedDumpRelativePath = string.Empty
        };

        var dumpPath = _dumpLocator.LocateNewestMatchingBin(_config.ProxmarkDumpSearchDirectory, scan, dumpStartedAt);
        if (dumpPath is not null)
        {
            var copiedRelativePath = _dumpLocator.CopyIntoDataset(dumpPath, _paths, scan.Timestamp);
            scan = new CaptureScanData
            {
                Timestamp = scan.Timestamp,
                SignalMv = scan.SignalMv,
                WeakSignal = scan.WeakSignal,
                Blocks = scan.Blocks,
                RawDumpOutput = scan.RawDumpOutput,
                CopiedDumpRelativePath = copiedRelativePath
            };
        }

        return scan;
    }
}
