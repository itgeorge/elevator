using NUnit.Framework;
using LfElevatorCaptureCli;

namespace LfElevatorCaptureCli.Tests;

[TestFixture]
public sealed class PassiveCaptureTests
{
    [TestCase("red", "red")]
    [TestCase("red/black", "red_black")]
    [TestCase(" .. dangerous label!? ", "dangerous_label")]
    public void Sanitize_ProducesSafeSingleComponent(string input, string expected)
    {
        Assert.That(CaptureLabel.Sanitize(input), Is.EqualTo(expected));
    }

    [Test]
    public void CommandBuilder_UsesOnlyPassiveSniffAndLocalSave()
    {
        var command = PassiveCapturePlan.BuildCommand(40_000, "/tmp/red-window");

        Assert.That(command, Is.EqualTo("lf sniff -s 40000; data save -f \"/tmp/red-window\""));
        Assert.That(PassiveCapturePlan.IsAllowed(command), Is.True);
        Assert.That(PassiveCapturePlan.IsAllowed(PassiveCapturePlan.BuildCommand(100, "/tmp/reader/write-simulation-red")), Is.True);
        foreach (var forbidden in new[] { "write", "password", "config", "tune", "dump", "read", "clone", "sim", "reset" })
            Assert.That(command, Does.Not.Contain(forbidden).IgnoreCase, forbidden);
    }

    [Test]
    public void FileLocator_FindsCollisionSuffixedPm3File()
    {
        var directory = Path.Combine(Path.GetTempPath(), "lf-capture-files-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var collision = Path.Combine(directory, "0001-red-20260826-001.pm3");
            File.WriteAllText(collision, "synthetic");
            Assert.That(CaptureFileLocator.Find(directory, "0001-red-20260826"), Is.EqualTo(collision));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Test]
    public void GracefulStop_DoesNotCancelInFlightBatch()
    {
        using var stop = new CaptureStopController();
        stop.RequestGracefulStop();

        Assert.That(stop.GracefulStopRequested, Is.True);
        Assert.That(stop.ImmediateStopRequested, Is.False);
        Assert.That(stop.ImmediateToken.IsCancellationRequested, Is.False);
    }

    [Test]
    public void ImmediateStop_CancelsInFlightBatch()
    {
        using var stop = new CaptureStopController();
        stop.RequestImmediateStop();

        Assert.That(stop.ImmediateStopRequested, Is.True);
        Assert.That(stop.ImmediateToken.IsCancellationRequested, Is.True);
    }

    [Test]
    public void CommandBuilder_RejectsCommandInjectionInSavePath()
    {
        Assert.Throws<ArgumentException>(() => PassiveCapturePlan.BuildCommand(100, "/tmp/a\"; lf tune"));
    }

    [Test]
    public void ManifestStore_WritesIncrementalJson()
    {
        var directory = Path.Combine(Path.GetTempPath(), "lf-capture-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var manifest = new CaptureManifest { Label = "red", SanitizedLabel = "red", SamplesPerWindow = 100, KeepWindows = 8, StartedUtc = DateTimeOffset.UtcNow.ToString("O") };
            manifest.Events.Add("created");
            var path = Path.Combine(directory, "manifest.json");
            CaptureManifestStore.Save(path, manifest);
            var json = File.ReadAllText(path);
            Assert.That(json, Does.Contain("\"red\""));
            Assert.That(json, Does.Contain("created"));
            Assert.That(File.Exists(path + ".tmp"), Is.False);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
