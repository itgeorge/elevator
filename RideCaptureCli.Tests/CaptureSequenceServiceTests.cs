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
    public void First_decodable_token_scan_uses_decoded_real_count()
    {
        var service = new CaptureSequenceService();
        var scan = CreateScan("C3FE0031", "20C60722", "B6D14924", "B6D14924", "4EC7494E", "4EC7494E");

        var result = service.ApplyScan([], scan);

        Assert.That(result.AddedRecord.TrackedCount, Is.EqualTo(0));
        Assert.That(result.AddedRecord.RealRideCount, Is.EqualTo(0));
        Assert.That(result.AddedRecord.Warnings, Does.Not.Contain("UNKNOWN_TOKEN"));
    }

    [Test]
    public void First_variant_venus_identity_with_venus_ride_blocks_is_known_and_decodes_ride_count()
    {
        var service = new CaptureSequenceService();
        var scan = CreateScan("21FF0031", "5BA494A3", "D6D1C733", "D6D1C733", "BBC7C940", "BBC7C940");

        var result = service.ApplyScan([], scan);

        Assert.That(result.AddedRecord.TrackedCount, Is.EqualTo(128));
        Assert.That(result.AddedRecord.RealRideCount, Is.EqualTo(128));
        Assert.That(result.AddedRecord.Warnings, Does.Not.Contain("UNKNOWN_TOKEN"));
    }

    [Test]
    public void First_variant_earth_identity_with_earth_ride_blocks_is_known_and_decodes_ride_count()
    {
        var service = new CaptureSequenceService();
        var scan = CreateScan("D3FE005D", "A4578D3A", "650432F5", "650432F5", "18121218", "18121218");

        var result = service.ApplyScan([], scan);

        Assert.That(result.AddedRecord.TrackedCount, Is.EqualTo(0));
        Assert.That(result.AddedRecord.RealRideCount, Is.EqualTo(0));
        Assert.That(result.AddedRecord.Warnings, Does.Not.Contain("UNKNOWN_TOKEN"));
    }

    [Test]
    public void First_pluto_identity_with_pluto_ride_blocks_is_known_and_decodes_ride_count()
    {
        var service = new CaptureSequenceService();
        var scan = CreateScan("83FE002A", "F100C064", "A3045930", "A3045930", "1F12121F", "1F12121F");

        var result = service.ApplyScan([], scan);

        Assert.That(result.AddedRecord.TrackedCount, Is.EqualTo(0));
        Assert.That(result.AddedRecord.RealRideCount, Is.EqualTo(0));
        Assert.That(result.AddedRecord.Warnings, Does.Not.Contain("UNKNOWN_TOKEN"));
    }

    [Test]
    public void First_canonical_jupiter_scan_decodes_261_instead_of_the_stale_seed_count()
    {
        var service = new CaptureSequenceService();
        var scan = CreateScan("EBFE002A", "F100CC5B", "A5045936", "A5045936", "8C134C84", "8C134C84");

        var result = service.ApplyScan([], scan);

        Assert.That(result.AddedRecord.TrackedCount, Is.EqualTo(261));
        Assert.That(result.AddedRecord.RealRideCount, Is.EqualTo(261));
        Assert.That(result.AddedRecord.Warnings, Does.Not.Contain("UNKNOWN_TOKEN"));
    }

    [Test]
    public void Jupiter_scan_with_stale_ebfe_history_uses_the_decoded_count_and_starts_a_new_sequence()
    {
        var service = new CaptureSequenceService();
        var history = new List<CaptureRecord>
        {
            new()
            {
                Timestamp = "2026-04-20T16:36:21.0000000+03:00",
                TokenId = "EBFE002A-F100CC5B-A5045936-A5045936",
                SequenceId = "EBFE002A-legacy-s01",
                TrackedCount = 500,
                RealRideCount = 500,
                Block5 = "DEAD1234",
                Block6 = "DEAD1234"
            }
        };
        var scan = CreateScan("EBFE002A", "F100CC5B", "A5045936", "A5045936", "8C134C84", "8C134C84");

        var result = service.ApplyScan(history, scan);

        Assert.That(result.AddedRecord.SequenceId, Is.Not.EqualTo("EBFE002A-legacy-s01"));
        Assert.That(result.AddedRecord.TrackedCount, Is.EqualTo(261));
        Assert.That(result.AddedRecord.RealRideCount, Is.EqualTo(261));
    }

    [Test]
    public void Jupiter_capture_continues_across_the_changing_high_word_boundary()
    {
        var service = new CaptureSequenceService();
        var first = service.ApplyScan([], CreateScan("EBFE002A", "F100CC5B", "A5045936", "A5045936", "8C12C900", "8C12C900"));
        var second = service.ApplyScan(first.Records, CreateScan("EBFE002A", "F100CC5B", "A5045936", "A5045936", "7F1236FF", "7F1236FF", timestamp: first.AddedRecord.TimestampAsDateTimeOffset().AddMinutes(1)));

        Assert.That(first.AddedRecord.RealRideCount, Is.EqualTo(128));
        Assert.That(second.AddedRecord.SequenceId, Is.EqualTo(first.AddedRecord.SequenceId));
        Assert.That(second.AddedRecord.TrackedCount, Is.EqualTo(127));
    }

    [Test]
    public void First_known_token_with_unseeded_undecodable_state_does_not_use_historical_seed()
    {
        var service = new CaptureSequenceService();
        var scan = CreateScan("EBFE002A", "F100CC5B", "A5045936", "A5045936", "DEAD1234", "DEAD1234");

        var result = service.ApplyScan([], scan);

        Assert.That(result.AddedRecord.TrackedCount, Is.EqualTo(10000));
        Assert.That(result.AddedRecord.RealRideCount, Is.Null);
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
    public void First_unknown_identity_with_decodable_state_gets_count_but_keeps_unknown_token_warning()
    {
        var service = new CaptureSequenceService();
        var scan = CreateScan("AAAAAAAA", "BBBBBBBB", "CCCCCCCC", "DDDDDDDD", "4EC7494E", "4EC7494E");

        var result = service.ApplyScan([], scan);

        Assert.That(result.AddedRecord.TrackedCount, Is.EqualTo(0));
        Assert.That(result.AddedRecord.RealRideCount, Is.EqualTo(0));
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
    public void Sequence_only_exact_updates_latest_entry_and_backfills_sequence()
    {
        var service = new CaptureSequenceService();
        var first = service.ApplyScan([], CreateScan("AAAAAAAA", "BBBBBBBB", "CCCCCCCC", "DDDDDDDD", "11111111", "11111111"));
        var second = service.ApplyScan(first.Records, CreateScan("AAAAAAAA", "BBBBBBBB", "CCCCCCCC", "DDDDDDDD", "22222222", "22222222", timestamp: first.AddedRecord.TimestampAsDateTimeOffset().AddMinutes(1)));

        var updated = service.ApplyExactToLatestSequenceRecord(second.Records, first.AddedRecord.SequenceId, 238);
        var rows = updated.Records.Where(r => r.SequenceId == first.AddedRecord.SequenceId).ToList();

        Assert.That(updated.SequenceOnlyUpdate, Is.True);
        Assert.That(updated.ManualAnchorRideCount, Is.EqualTo(238));
        Assert.That(rows[0].RealRideCount, Is.EqualTo(239));
        Assert.That(rows[1].RealRideCount, Is.EqualTo(238));
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
