using NUnit.Framework;

namespace RideCaptureCli.Tests;

public class ProxmarkDumpLocatorTests
{
    [Test]
    public void LocateNewestMatchingBin_finds_recent_matching_dump_and_copy_returns_relative_path()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var searchDir = Path.Combine(tempDir, "search");
            var outputDir = Path.Combine(tempDir, "output");
            Directory.CreateDirectory(searchDir);

            var fileName = "lf-t55xx-D3FE005D-522BC69D-650432F5-650432F5-18120A99-18120A99-dump.bin";
            var filePath = Path.Combine(searchDir, fileName);
            File.WriteAllBytes(filePath, [0x01, 0x02, 0x03, 0x04]);
            File.SetLastWriteTimeUtc(filePath, new DateTime(2026, 4, 20, 13, 36, 22, DateTimeKind.Utc));

            var scan = new CaptureScanData
            {
                Timestamp = new DateTimeOffset(2026, 4, 20, 16, 36, 23, TimeSpan.FromHours(3)),
                SignalMv = 23976,
                WeakSignal = false,
                Blocks = ["00148040", "D3FE005D", "522BC69D", "650432F5", "650432F5", "18120A99", "18120A99", "00000000"]
            };

            var locator = new ProxmarkDumpLocator();
            var found = locator.LocateNewestMatchingBin(searchDir, scan, new DateTimeOffset(2026, 4, 20, 16, 36, 20, TimeSpan.FromHours(3)));
            Assert.That(found, Is.EqualTo(filePath));

            var relativePath = locator.CopyIntoDataset(filePath, new CapturePaths(outputDir), scan.Timestamp);
            Assert.That(relativePath.Replace('\\', '/'), Does.StartWith("dumps/2026-04-20/163623-"));
            Assert.That(File.Exists(Path.Combine(outputDir, relativePath)), Is.True);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }
}
