using NUnit.Framework;
using Pm3UsbApi;
using Pm3UsbApi.Commands;
using Pm3UsbApi.Execution;

namespace Pm3UsbApi.Tests;

[TestFixture]
public class Pm3ProcessExecutorTests
{
    private static Pm3ProcessExecutor CreateExecutor() =>
        new(new Pm3Options { WorkingDirectory = Pm3Options.DevRunsDirectoryName });

    private static async Task<string?> ResolveDevicePortAsync()
    {
        var port = Environment.GetEnvironmentVariable("PM3_DEVICE_PORT");
        if (!string.IsNullOrWhiteSpace(port) && !port.Trim().Equals("auto", StringComparison.OrdinalIgnoreCase))
            return port.Trim();

        return await PortDiscovery.DiscoverFirstPortAsync(null);
    }

    [Test]
    public void LfTuneCaptureInterval_IsThreeSeconds()
    {
        Assert.That(Pm3ProcessExecutor.LfTuneCaptureInterval.TotalSeconds, Is.EqualTo(3));
    }

    [Test]
    [Category("Integration")]
    [Explicit("Requires Proxmark3 connected. Run manually with: dotnet test --filter 'Category=Integration'")]
    public async Task ExecuteAsync_LfTuneAlone_ReturnsAfterCaptureInterval()
    {
        var devicePort = await ResolveDevicePortAsync();
        Assert.That(devicePort, Is.Not.Null.And.Not.Empty, "No Proxmark3 port found. Set PM3_DEVICE_PORT or connect the device.");

        var options = new Pm3Options
        {
            DevicePort = devicePort,
            DefaultCommandTimeout = TimeSpan.FromSeconds(20),
            WorkingDirectory = Pm3Options.DevRunsDirectoryName,
        };
        await using var executor = new Pm3ProcessExecutor(options);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = await executor.ExecuteAsync([new LfTuneCommand()]);
        sw.Stop();
        Assert.That(sw.Elapsed, Is.LessThan(TimeSpan.FromSeconds(5)), "Should return within ~3s, not full timeout");
        Assert.That(result.OutputLines, Is.Not.Empty);
        Assert.That(result.RawOutput, Does.Contain("mV").Or.Contain("V "));
        Assert.That(result.Commands, Has.Count.EqualTo(1));
        Assert.That(result.Commands[0], Is.InstanceOf<LfTuneCommand>());
    }

    [Test]
    [Category("Integration")]
    [Explicit("Requires Proxmark3 connected with a readable T55xx tag.")]
    [NonParallelizable]
    public async Task DumpParsedAsync_ProcessExecutor_DoesNotCreateDumpFilesInCallerDirectory()
    {
        var devicePort = await ResolveDevicePortAsync();
        Assert.That(devicePort, Is.Not.Null.And.Not.Empty, "No Proxmark3 port found. Set PM3_DEVICE_PORT or connect the device.");

        var originalDirectory = Directory.GetCurrentDirectory();
        var callerDirectory = Path.Combine(Path.GetTempPath(), $"pm3-no-save-{Guid.NewGuid():N}");
        Directory.CreateDirectory(callerDirectory);
        try
        {
            Directory.SetCurrentDirectory(callerDirectory);
            var options = new Pm3Options
            {
                ExecutorKind = Pm3ExecutorKind.Process,
                DevicePort = devicePort,
                DefaultCommandTimeout = TimeSpan.FromSeconds(30),
                WorkingDirectory = null,
            };
            await using var pm3 = new Pm3(options);

            await pm3.ConnectAsync();
            var dump = await pm3.DumpParsedAsync();

            Assert.That(dump.Success, Is.True);
            Assert.That(dump.Blocks, Has.Count.GreaterThanOrEqualTo(8));
            Assert.That(Directory.EnumerateFiles(callerDirectory, "lf-t55xx-*"), Is.Empty);
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDirectory);
            Directory.Delete(callerDirectory, recursive: true);
        }
    }
}
