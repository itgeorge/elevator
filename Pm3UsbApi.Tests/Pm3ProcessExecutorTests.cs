using NUnit.Framework;
using Pm3UsbApi;
using Pm3UsbApi.Execution;

namespace Pm3UsbApi.Tests;

[TestFixture]
public class Pm3ProcessExecutorTests
{
    private static Pm3ProcessExecutor CreateExecutor() => new(new Pm3Options());

    [Test]
    public void ExecuteAsync_LfTuneCombinedWithOtherCommands_Throws()
    {
        var executor = CreateExecutor();
        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await executor.ExecuteAsync(["lf t55 detect", "lf tune"]));
    }

    [Test]
    public void ExecuteAsync_LfTuneWithHwVersion_Throws()
    {
        var executor = CreateExecutor();
        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await executor.ExecuteAsync(["lf tune", "hw version"]));
    }

    [Test]
    public void ExecuteAsync_LfTuneWithOptions_Combined_Throws()
    {
        var executor = CreateExecutor();
        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await executor.ExecuteAsync(["hw version", "lf tune"]));
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
        var options = new Pm3Options
        {
            DevicePort = "COM5",
            DefaultCommandTimeout = TimeSpan.FromSeconds(20),
        };
        await using var executor = new Pm3ProcessExecutor(options);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = await executor.ExecuteAsync(["lf tune"]);
        sw.Stop();
        Assert.That(sw.Elapsed, Is.LessThan(TimeSpan.FromSeconds(5)), "Should return within ~3s, not full timeout");
        Assert.That(result.OutputLines, Is.Not.Empty);
        Assert.That(result.RawOutput, Does.Contain("mV").Or.Contain("V "));
    }
}
