using NUnit.Framework;
using Pm3UsbApi;
using Tokens;

namespace Pm3UsbApi.Tests.Integration;

[TestFixture]
[Category("Integration")]
[Category("IntegrationParity")]
[Explicit("Requires Proxmark3 connected with token on reader. Run: dotnet test --filter 'Category=IntegrationParity'")]
public class Pm3ExecutorParityTests
{
    private static async Task<uint> MeasureTunePeakAsync(Pm3ExecutorKind executorKind)
    {
        var options = IntegrationTestOptions.Create(TimeSpan.FromSeconds(20)) with { ExecutorKind = executorKind };
        await using var pm3 = new Pm3(options);
        await pm3.ConnectAsync();
        await pm3.StartLfTuneAsync();
        return await pm3.GetLfTuneLastMilliVoltsAsync();
    }

    [Test]
    public async Task Tune_ProcessAndNative_PeakWithinTolerance()
    {
        var processMv = await MeasureTunePeakAsync(Pm3ExecutorKind.Process);
        var nativeMv = await MeasureTunePeakAsync(Pm3ExecutorKind.Native);

        TestContext.WriteLine($"Process tune: {processMv} mV");
        TestContext.WriteLine($"Native tune:  {nativeMv} mV");

        Assert.That(Math.Abs((int)processMv - (int)nativeMv), Is.LessThan(3000),
            "Native and process executors should report similar LF tune peaks.");
    }

    [Test]
    public async Task TokenBaseline_ProcessExecutor_ReadsFiftyRides()
    {
        var options = IntegrationTestOptions.Create() with { ExecutorKind = Pm3ExecutorKind.Process };
        await using var pm3 = new Pm3(options);
        await pm3.ConnectAsync();
        await pm3.EnsureT55SessionActiveAsync();

        var block5 = T55Block.FromHex(await pm3.ReadPage0BlockAsync(5));
        var rides = TokenBlockUtils.Decode(block5);

        Assert.That(rides, Is.EqualTo(50u), "Expected the prepared testing baseline of 50 rides.");
    }
}
