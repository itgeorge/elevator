using NUnit.Framework;

namespace RideCaptureCli.Tests;

public class OtherCaptureCsvStoreTests
{
    [Test]
    public void CreateRecord_from_scan_records_blocks_and_warnings_without_sequence_data()
    {
        var store = new OtherCaptureCsvStore();
        var scan = new CaptureScanData
        {
            Timestamp = new DateTimeOffset(2026, 4, 20, 16, 36, 21, TimeSpan.FromHours(3)),
            SignalMv = 30001,
            WeakSignal = true,
            Blocks = ["00148040", "TOKEN001", "TOKEN002", "TOKEN003", "TOKEN004", "AAAA0001", "BBBB0002", "FFFFFFFF"],
            CopiedDumpRelativePath = string.Empty
        };

        var record = store.CreateRecord(scan);

        Assert.That(record.TokenId, Is.EqualTo("TOKEN001-TOKEN002-TOKEN003-TOKEN004"));
        Assert.That(record.EncodedState, Is.EqualTo("AAAA0001-BBBB0002"));
        Assert.That(record.Warnings, Does.Contain("WEAK_SIGNAL"));
        Assert.That(record.Warnings, Does.Contain("MIRROR_MISMATCH"));
        Assert.That(record.Warnings, Does.Contain("MISSING_DUMP"));
    }

    [Test]
    public void Save_and_load_round_trip_preserves_other_capture_values()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var path = Path.Combine(tempDir, "other-captures.csv");
            var store = new OtherCaptureCsvStore();
            var record = new OtherCaptureRecord
            {
                Timestamp = "2026-04-20T16:36:21.0000000+03:00",
                TokenId = "D3FE005D-522BC69D-650432F5-650432F5",
                Warnings = "MISSING_DUMP",
                SignalMv = 23976,
                WeakSignal = false,
                Block0 = "00148040",
                Block1 = "D3FE005D",
                Block2 = "522BC69D",
                Block3 = "650432F5",
                Block4 = "650432F5",
                Block5 = "18120A99",
                Block6 = "18120A99",
                Block7 = "00000000",
                CopiedDumpRelativePath = "dumps/2026-04-20/token with spaces.bin"
            };

            store.Save(path, [record]);

            var text = File.ReadAllText(path);
            Assert.That(text, Does.Contain("\"dumps/2026-04-20/token with spaces.bin\""));

            var loaded = store.Load(path);
            Assert.That(loaded, Has.Count.EqualTo(1));
            Assert.That(loaded[0].TokenId, Is.EqualTo(record.TokenId));
            Assert.That(loaded[0].EncodedState, Is.EqualTo("18120A99-18120A99"));
            Assert.That(loaded[0].CopiedDumpRelativePath, Is.EqualTo(record.CopiedDumpRelativePath));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }
}
