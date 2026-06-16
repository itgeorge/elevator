using NUnit.Framework;
using Pm3UsbApi;

namespace Pm3UsbApi.Tests.Integration;

[TestFixture]
[Category("Integration")]
[Category("IntegrationNative")]
[Explicit("Requires Proxmark3 connected. Run: dotnet test --filter 'Category=IntegrationNative'")]
public class Pm3NativeIntegrationTests
{
    private static Pm3Options CreateNativeOptions() =>
        IntegrationTestOptions.Create() with { ExecutorKind = Pm3ExecutorKind.Native };

    [Test]
    public async Task DetectAndReadBlock5_ReturnsHex()
    {
        await using var pm3 = new Pm3(CreateNativeOptions());
        await pm3.ConnectAsync();
        await pm3.EnsureT55SessionActiveAsync();

        var hex = await pm3.ReadPage0BlockAsync(5);
        Assert.That(hex, Has.Length.EqualTo(8));
        Assert.That(hex, Does.Match("^[0-9A-F]+$"));
    }

    [Test]
    public async Task Connect_IsConnected_ReturnsTrue()
    {
        await using var pm3 = new Pm3(CreateNativeOptions());
        await pm3.ConnectAsync();
        Assert.That(await pm3.IsConnectedAsync(), Is.True);
    }

    [Test]
    public async Task Tune_ReturnsReasonablePeakMilliVolts()
    {
        await using var pm3 = new Pm3(CreateNativeOptions());
        await pm3.ConnectAsync();

        await pm3.StartLfTuneAsync();
        var peakMv = await pm3.GetLfTuneLastMilliVoltsAsync();

        Assert.That(peakMv, Is.GreaterThan(1000u));
        Assert.That(peakMv, Is.LessThan(100_000u));
    }

    [Test]
    public async Task ConnectThenTune_SequentialOperationsSucceed()
    {
        await using var pm3 = new Pm3(CreateNativeOptions());

        await pm3.ConnectAsync();
        Assert.That(await pm3.IsConnectedAsync(), Is.True);

        await pm3.StartLfTuneAsync();
        var first = await pm3.GetLfTuneLastMilliVoltsAsync();

        await pm3.StartLfTuneAsync();
        var second = await pm3.GetLfTuneLastMilliVoltsAsync();

        Assert.That(first, Is.GreaterThan(1000u));
        Assert.That(second, Is.GreaterThan(1000u));
        Assert.That(Math.Abs((int)first - (int)second), Is.LessThan(5000));
    }
}
