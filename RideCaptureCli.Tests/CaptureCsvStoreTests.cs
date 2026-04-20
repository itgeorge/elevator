using System.Globalization;
using NUnit.Framework;

namespace RideCaptureCli.Tests;

public class CaptureCsvStoreTests
{
    [Test]
    public void Save_and_load_round_trip_preserves_values_and_quotes_paths()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var path = Path.Combine(tempDir, "captures.csv");
            var store = new CaptureCsvStore();
            var record = new CaptureRecord
            {
                Timestamp = DateTimeOffset.Parse("2026-04-20T16:36:21+03:00", CultureInfo.InvariantCulture).ToString("O", CultureInfo.InvariantCulture),
                TokenId = "D3FE005D-522BC69D-650432F5-650432F5",
                SequenceId = "D3FE005D-20260420-163621-s01",
                Status = CaptureStatus.Ok,
                Warnings = "WEAK_SIGNAL|MISSING_DUMP",
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
                CopiedDumpRelativePath = "dumps/2026-04-20/token with spaces.bin"
            };

            store.Save(path, [record]);

            var text = File.ReadAllText(path);
            Assert.That(text, Does.Contain("\"dumps/2026-04-20/token with spaces.bin\""));

            var loaded = store.Load(path);
            Assert.That(loaded, Has.Count.EqualTo(1));
            Assert.That(loaded[0].CopiedDumpRelativePath, Is.EqualTo(record.CopiedDumpRelativePath));
            Assert.That(loaded[0].RealRideCount, Is.EqualTo(24));
            Assert.That(loaded[0].TokenId, Is.EqualTo(record.TokenId));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }
}
