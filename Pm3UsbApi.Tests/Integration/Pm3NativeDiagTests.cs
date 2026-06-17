using NUnit.Framework;
using Pm3UsbApi;

namespace Pm3UsbApi.Tests.Integration;

[TestFixture]
[Category("Integration")]
[Explicit("Hardware diagnostic for native T55 path")]
[NonParallelizable]
public class Pm3NativeDiagTests
{
    private static Pm3Options CreateOptions() =>
        IntegrationTestOptions.Create(Pm3ExecutorKind.Native, TimeSpan.FromSeconds(20));

    [Test]
    public async Task Native_DetectOnly_Succeeds()
    {
        await using var pm3 = new Pm3(CreateOptions());
        await pm3.ConnectAsync();
        await pm3.EnsureT55SessionActiveAsync();
    }

    [Test]
    public async Task Native_TuneThenDetect_MatchesRidesCliReadFlow()
    {
        await using var pm3 = new Pm3(CreateOptions());
        await pm3.ConnectAsync();
        await pm3.StartLfTuneAsync();
        var mv = await pm3.GetLfTuneLastMilliVoltsAsync();
        TestContext.WriteLine($"tune={mv} mV");
        await pm3.EnsureT55SessionActiveAsync();
    }

    [Test]
    public async Task Native_TuneThenDump_MatchesRidesCliReadFlow()
    {
        await using var pm3 = new Pm3(CreateOptions());
        await pm3.ConnectAsync();
        await pm3.StartLfTuneAsync();
        _ = await pm3.GetLfTuneLastMilliVoltsAsync();
        var dump = await pm3.DumpAsync();
        Assert.That(dump, Does.Contain("00148040").Or.Contain("blk"));
    }
}
