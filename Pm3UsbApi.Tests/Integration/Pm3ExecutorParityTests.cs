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
        var options = IntegrationTestOptions.Create(executorKind, TimeSpan.FromSeconds(20));
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
}
