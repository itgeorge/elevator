using NUnit.Framework;

namespace RideCaptureCli.Tests;

public class RideCaptureConfigTests
{
    [Test]
    public void LoadOrCreate_creates_default_file_when_missing()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var path = Path.Combine(tempDir, "ride-capture-config.json");
            var config = RideCaptureConfig.LoadOrCreate(path);

            Assert.That(File.Exists(path), Is.True);
            Assert.That(config.MaxAcceptableSignalMv, Is.EqualTo(29000));
            Assert.That(config.OutputRootDirectory, Is.EqualTo("ride-capture-data"));
            Assert.That(config.ProxmarkDumpSearchDirectory, Is.EqualTo("proxmark-runs"));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }
}
