namespace RideCaptureCli;

public sealed class CaptureSequenceService
{
    private const int UnknownStartTrackedCount = 10_000;
    private readonly SequenceIdGenerator _sequenceIdGenerator;

    public CaptureSequenceService(SequenceIdGenerator? sequenceIdGenerator = null)
    {
        _sequenceIdGenerator = sequenceIdGenerator ?? new SequenceIdGenerator();
    }

    public CaptureApplyResult ApplyScan(IReadOnlyList<CaptureRecord> history, CaptureScanData scan, bool markZero)
    {
        var records = history.Select(Clone).ToList();
        var tokenHistory = records.Where(r => string.Equals(r.TokenId, scan.TokenId, StringComparison.OrdinalIgnoreCase)).ToList();
        var latestTokenRow = tokenHistory.LastOrDefault();
        var activeSequenceId = latestTokenRow?.SequenceId;
        var activeSequenceRows = activeSequenceId is null
            ? []
            : tokenHistory.Where(r => r.SequenceId == activeSequenceId).ToList();
        var lastActiveRow = activeSequenceRows.LastOrDefault();

        var warnings = BuildWarnings(scan, tokenHistory);
        var matchedHistoricalRow = tokenHistory.LastOrDefault(r => r.Block5 == scan.Blocks[5] && r.Block6 == scan.Blocks[6]);

        CaptureRecord added;
        var shouldStartNewSequence = lastActiveRow is null || (lastActiveRow.RealRideCount.HasValue && lastActiveRow.RealRideCount.Value == 0 && matchedHistoricalRow is null);

        if (lastActiveRow is not null && lastActiveRow.Block5 == scan.Blocks[5] && lastActiveRow.Block6 == scan.Blocks[6])
        {
            added = CreateRecord(scan, lastActiveRow.SequenceId, CaptureStatus.NoChange, warnings, lastActiveRow.TrackedCount, lastActiveRow.RealRideCount);
        }
        else if (shouldStartNewSequence)
        {
            var sequenceId = _sequenceIdGenerator.CreateNext(scan.TokenId, records, scan.Timestamp);
            if (tokenHistory.Count == 0 && SeededTokenCatalog.TryGetStartingRides(scan.TokenId, out var seededRides))
            {
                added = CreateRecord(scan, sequenceId, CaptureStatus.Ok, warnings, seededRides, seededRides);
            }
            else
            {
                added = CreateRecord(scan, sequenceId, CaptureStatus.Ok, warnings, UnknownStartTrackedCount, null);
            }
        }
        else
        {
            added = CreateRecord(
                scan,
                lastActiveRow!.SequenceId,
                CaptureStatus.Ok,
                warnings,
                lastActiveRow.TrackedCount - 1,
                lastActiveRow.RealRideCount.HasValue ? lastActiveRow.RealRideCount.Value - 1 : null);
        }

        records.Add(added);

        var autoNormalized = false;
        if (matchedHistoricalRow is not null
            && matchedHistoricalRow.RealRideCount.HasValue
            && matchedHistoricalRow.SequenceId != added.SequenceId)
        {
            NormalizeSequence(records, added.SequenceId, added.TrackedCount, matchedHistoricalRow.RealRideCount.Value, zeroAnchor: false);
            autoNormalized = true;
        }

        if (markZero)
        {
            NormalizeSequence(records, added.SequenceId, added.TrackedCount, 0, zeroAnchor: true);
        }

        return new CaptureApplyResult
        {
            Records = records,
            AddedRecord = records[^1],
            AutoNormalized = autoNormalized
        };
    }

    private static CaptureRecord Clone(CaptureRecord record) => new()
    {
        Timestamp = record.Timestamp,
        TokenId = record.TokenId,
        SequenceId = record.SequenceId,
        Status = record.Status,
        Warnings = record.Warnings,
        SignalMv = record.SignalMv,
        WeakSignal = record.WeakSignal,
        TrackedCount = record.TrackedCount,
        RealRideCount = record.RealRideCount,
        ZeroAnchor = record.ZeroAnchor,
        Block0 = record.Block0,
        Block1 = record.Block1,
        Block2 = record.Block2,
        Block3 = record.Block3,
        Block4 = record.Block4,
        Block5 = record.Block5,
        Block6 = record.Block6,
        Block7 = record.Block7,
        CopiedDumpRelativePath = record.CopiedDumpRelativePath
    };

    private static string BuildWarnings(CaptureScanData scan, IReadOnlyList<CaptureRecord> tokenHistory)
    {
        var warnings = new List<string>();
        if (scan.WeakSignal)
            warnings.Add("WEAK_SIGNAL");

        var tokenAlreadyKnown = SeededTokenCatalog.TryGetStartingRides(scan.TokenId, out _)
            || tokenHistory.Any(r => r.RealRideCount.HasValue);
        if (!tokenAlreadyKnown)
            warnings.Add("UNKNOWN_TOKEN");

        if (!scan.MirrorMatches)
            warnings.Add("MIRROR_MISMATCH");
        if (string.IsNullOrWhiteSpace(scan.CopiedDumpRelativePath))
            warnings.Add("MISSING_DUMP");
        return string.Join('|', warnings);
    }

    private static CaptureRecord CreateRecord(CaptureScanData scan, string sequenceId, CaptureStatus status, string warnings, int trackedCount, int? realRideCount) => new()
    {
        Timestamp = scan.Timestamp.ToString("O"),
        TokenId = scan.TokenId,
        SequenceId = sequenceId,
        Status = status,
        Warnings = warnings,
        SignalMv = scan.SignalMv,
        WeakSignal = scan.WeakSignal,
        TrackedCount = trackedCount,
        RealRideCount = realRideCount,
        ZeroAnchor = false,
        Block0 = scan.Blocks[0],
        Block1 = scan.Blocks[1],
        Block2 = scan.Blocks[2],
        Block3 = scan.Blocks[3],
        Block4 = scan.Blocks[4],
        Block5 = scan.Blocks[5],
        Block6 = scan.Blocks[6],
        Block7 = scan.Blocks[7],
        CopiedDumpRelativePath = scan.CopiedDumpRelativePath
    };

    private static void NormalizeSequence(List<CaptureRecord> records, string sequenceId, int anchorTrackedCount, int anchorRealRideCount, bool zeroAnchor)
    {
        var offset = anchorTrackedCount - anchorRealRideCount;
        foreach (var row in records.Where(r => r.SequenceId == sequenceId))
        {
            row.RealRideCount = row.TrackedCount - offset;
            if (zeroAnchor && row == records[^1])
                row.ZeroAnchor = true;
        }
    }
}
