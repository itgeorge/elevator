using NUnit.Framework;
using Pm3UsbApi.Native.Demod;
using Pm3UsbApi.Native.T55;

namespace Pm3UsbApi.Tests.Native;

[TestFixture]
public class Pm3T55NativeOfflineTests
{
    private static string FixturePath =>
        Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "Native", "t55-block0-samples.bin");

    [Test]
    public void CapturedBlock0Samples_DetectElevatorTokenOffline()
    {
        if (!File.Exists(FixturePath))
            Assert.Ignore($"Captured fixture not found at {FixturePath}. Run NativeT55Probe --capture on hardware.");

        var raw = File.ReadAllBytes(FixturePath);
        Assert.That(raw, Has.Length.EqualTo(Pm3GraphState.MaxGraphSamples));

        var graph = new Pm3GraphState();
        graph.LoadSamples(raw);
        var bytes = new byte[raw.Length];
        var len = graph.CopyToByteSamples(bytes);
        graph.Signal.Compute(bytes.AsSpan(0, len));

        Assert.That(graph.Signal.IsNoise, Is.False, "Captured fixture should not be classified as noise.");

        var bitLen = len;
        var clk = 0;
        var invert = 0;
        var st = true;
        var work = bytes.ToArray();
        var err = Pm3LfDemod.AskDemodExt(work, ref bitLen, ref clk, ref invert, maxErr: 1, askType: 1, ref st, graph.Signal);

        Assert.That(err, Is.InRange(0, 1));
        Assert.That(bitLen, Is.GreaterThan(64));
        Assert.That(clk, Is.EqualTo(Pm3LfDemod.TokenClock));

        Assert.That(
            Pm3BitUtils.TryFindConfigOffset(work.AsSpan(0, bitLen), Pm3BitUtils.DemodAsk, clk, out var offset, out _),
            Is.True);
        Assert.That(offset, Is.EqualTo(32));

        var block0 = Pm3BitUtils.PackBits(offset, 32, work.AsSpan(0, bitLen));
        Assert.That(block0, Is.EqualTo(0x00148040u));
    }
}
