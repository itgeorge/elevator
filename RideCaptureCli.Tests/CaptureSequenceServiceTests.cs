using NUnit.Framework;

namespace RideCaptureCli.Tests;

public class CaptureSequenceServiceTests
{
    private static CaptureScanData CreateScan(
        string block1,
        string block2,
        string block3,
        string block4,
        string block5,
        string block6,
        DateTimeOffset? timestamp = null,
        int signalMv = 23976,
        bool weakSignal = false,
        string copiedDumpRelativePath = "dumps/2026-04-20/sample.bin") =>
        new()
        {
            Timestamp = timestamp ?? new DateTimeOffset(2026, 4, 20, 16, 36, 21, TimeSpan.FromHours(3)),
            SignalMv = signalMv,
            WeakSignal = weakSignal,
            Blocks = ["00148040", block1, block2, block3, block4, block5, block6, "00000000"],
            CopiedDumpRelativePath = copiedDumpRelativePath
        };

    [Test]
    public void First_seeded_token_scan_uses_seeded_real_count()
    {
        var service = new CaptureSequenceService();
        var scan = CreateScan("D3FE005D", "522BC69D", "650432F5", "650432F5", "18120A99", "18120A99");

        var result = service.ApplyScan([], scan);

        Assert.That(result.AddedRecord.TrackedCount, Is.EqualTo(24));
        Assert.That(result.AddedRecord.RealRideCount, Is.EqualTo(24));
        Assert.That(result.AddedRecord.Warnings, Does.Not.Contain("UNKNOWN_TOKEN"));
    }

    [Test]
    public void First_unknown_token_scan_starts_at_10000()
    {
        var service = new CaptureSequenceService();
        var scan = CreateScan("AAAAAAAA", "BBBBBBBB", "CCCCCCCC", "DDDDDDDD", "11111111", "11111111");

        var result = service.ApplyScan([], scan);

        Assert.That(result.AddedRecord.TrackedCount, Is.EqualTo(10000));
        Assert.That(result.AddedRecord.RealRideCount, Is.Null);
        Assert.That(result.AddedRecord.Warnings, Does.Contain("UNKNOWN_TOKEN"));
    }

    [Test]
    public void Duplicate_scan_is_recorded_as_no_change()
    {
        var service = new CaptureSequenceService();
        var first = service.ApplyScan([], CreateScan("AAAAAAAA", "BBBBBBBB", "CCCCCCCC", "DDDDDDDD", "11111111", "11111111"));

        var duplicate = service.ApplyScan(first.Records, CreateScan("AAAAAAAA", "BBBBBBBB", "CCCCCCCC", "DDDDDDDD", "11111111", "11111111", timestamp: first.AddedRecord.TimestampAsDateTimeOffset().AddMinutes(1)));

        Assert.That(duplicate.AddedRecord.Status, Is.EqualTo(CaptureStatus.NoChange));
        Assert.That(duplicate.AddedRecord.TrackedCount, Is.EqualTo(10000));
        Assert.That(duplicate.AddedRecord.SequenceId, Is.EqualTo(first.AddedRecord.SequenceId));
    }

    [Test]
    public void New_changed_unknown_scan_continues_current_sequence()
    {
        var service = new CaptureSequenceService();
        var first = service.ApplyScan([], CreateScan("AAAAAAAA", "BBBBBBBB", "CCCCCCCC", "DDDDDDDD", "11111111", "11111111"));

        var second = service.ApplyScan(first.Records, CreateScan("AAAAAAAA", "BBBBBBBB", "CCCCCCCC", "DDDDDDDD", "22222222", "22222222", timestamp: first.AddedRecord.TimestampAsDateTimeOffset().AddMinutes(1)));

        Assert.That(second.AddedRecord.SequenceId, Is.EqualTo(first.AddedRecord.SequenceId));
        Assert.That(second.AddedRecord.TrackedCount, Is.EqualTo(9999));
    }

    [Test]
    public void Automatic_normalization_backfills_current_sequence_when_state_matches_known_historical_real_value()
    {
        var service = new CaptureSequenceService();
        var history = new List<CaptureRecord>
        {
            new()
            {
                Timestamp = "2026-04-20T16:36:21.0000000+03:00",
                TokenId = "D3FE005D-522BC69D-650432F5-650432F5",
                SequenceId = "D3FE005D-20260420-163621-s01",
                Status = CaptureStatus.Ok,
                Warnings = string.Empty,
                SignalMv = 23976,
                WeakSignal = false,
                TrackedCount = 24,
                RealRideCount = 24,
                ZeroAnchor = false,
                Block0 = "00148040",
                Block1 = "D3FE005D",
                Block2 = "522BC69D",
                Block3 = "650432F5",
                Block4 = "650432F5",
                Block5 = "18120A99",
                Block6 = "18120A99",
                Block7 = "00000000",
                CopiedDumpRelativePath = "dumps/2026-04-20/known.bin"
            },
            new()
            {
                Timestamp = "2026-04-20T16:37:21.0000000+03:00",
                TokenId = "D3FE005D-522BC69D-650432F5-650432F5",
                SequenceId = "D3FE005D-20260420-163621-s01",
                Status = CaptureStatus.Ok,
                Warnings = string.Empty,
                SignalMv = 23976,
                WeakSignal = false,
                TrackedCount = 0,
                RealRideCount = 0,
                ZeroAnchor = true,
                Block0 = "00148040",
                Block1 = "D3FE005D",
                Block2 = "522BC69D",
                Block3 = "650432F5",
                Block4 = "650432F5",
                Block5 = "00000001",
                Block6 = "00000001",
                Block7 = "00000000",
                CopiedDumpRelativePath = "dumps/2026-04-20/zero.bin"
            }
        };

        var newSequenceStart = service.ApplyScan(history, CreateScan("D3FE005D", "522BC69D", "650432F5", "650432F5", "99990000", "99990000", timestamp: new DateTimeOffset(2026, 4, 21, 16, 36, 21, TimeSpan.FromHours(3))));
        var newSequenceNext = service.ApplyScan(newSequenceStart.Records, CreateScan("D3FE005D", "522BC69D", "650432F5", "650432F5", "18120A99", "18120A99", timestamp: newSequenceStart.AddedRecord.TimestampAsDateTimeOffset().AddMinutes(1)));

        var currentSequenceRows = newSequenceNext.Records.Where(r => r.SequenceId == newSequenceStart.AddedRecord.SequenceId).ToList();
        Assert.That(newSequenceNext.AutoNormalized, Is.True);
        Assert.That(currentSequenceRows[0].RealRideCount, Is.EqualTo(25));
        Assert.That(currentSequenceRows[1].RealRideCount, Is.EqualTo(24));
    }

    [Test]
    public void Zero_command_backfills_sequence_to_real_zero()
    {
        var service = new CaptureSequenceService();
        var first = service.ApplyScan([], CreateScan("AAAAAAAA", "BBBBBBBB", "CCCCCCCC", "DDDDDDDD", "11111111", "11111111"));
        var second = service.ApplyScan(first.Records, CreateScan("AAAAAAAA", "BBBBBBBB", "CCCCCCCC", "DDDDDDDD", "22222222", "22222222", timestamp: first.AddedRecord.TimestampAsDateTimeOffset().AddMinutes(1)), exactRideCount: 0);

        var rows = second.Records.Where(r => r.SequenceId == first.AddedRecord.SequenceId).ToList();
        Assert.That(rows[0].RealRideCount, Is.EqualTo(1));
        Assert.That(rows[1].RealRideCount, Is.EqualTo(0));
        Assert.That(rows[1].ZeroAnchor, Is.True);
        Assert.That(second.ManualAnchorRideCount, Is.EqualTo(0));
    }

    [Test]
    public void Exact_command_backfills_sequence_to_given_real_count()
    {
        var service = new CaptureSequenceService();
        var first = service.ApplyScan([], CreateScan("AAAAAAAA", "BBBBBBBB", "CCCCCCCC", "DDDDDDDD", "11111111", "11111111"));
        var second = service.ApplyScan(first.Records, CreateScan("AAAAAAAA", "BBBBBBBB", "CCCCCCCC", "DDDDDDDD", "22222222", "22222222", timestamp: first.AddedRecord.TimestampAsDateTimeOffset().AddMinutes(1)), exactRideCount: 137);

        var rows = second.Records.Where(r => r.SequenceId == first.AddedRecord.SequenceId).ToList();
        Assert.That(rows[0].RealRideCount, Is.EqualTo(138));
        Assert.That(rows[1].RealRideCount, Is.EqualTo(137));
        Assert.That(rows[1].ZeroAnchor, Is.False);
        Assert.That(second.ManualAnchorRideCount, Is.EqualTo(137));
    }

    [Test]
    public void Token_stops_being_marked_unknown_once_real_count_is_known()
    {
        var service = new CaptureSequenceService();
        var first = service.ApplyScan([], CreateScan("AAAAAAAA", "BBBBBBBB", "CCCCCCCC", "DDDDDDDD", "11111111", "11111111"));
        var anchored = service.ApplyScan(first.Records, CreateScan("AAAAAAAA", "BBBBBBBB", "CCCCCCCC", "DDDDDDDD", "22222222", "22222222", timestamp: first.AddedRecord.TimestampAsDateTimeOffset().AddMinutes(1)), exactRideCount: 0);
        var nextSequence = service.ApplyScan(anchored.Records, CreateScan("AAAAAAAA", "BBBBBBBB", "CCCCCCCC", "DDDDDDDD", "33333333", "33333333", timestamp: anchored.AddedRecord.TimestampAsDateTimeOffset().AddDays(1)));

        Assert.That(nextSequence.AddedRecord.Warnings, Does.Not.Contain("UNKNOWN_TOKEN"));
    }
}

internal static class CaptureRecordTestExtensions
{
    public static DateTimeOffset TimestampAsDateTimeOffset(this CaptureRecord record) => DateTimeOffset.Parse(record.Timestamp);
}
