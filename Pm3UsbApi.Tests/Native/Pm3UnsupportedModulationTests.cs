using NUnit.Framework;
using Pm3UsbApi;
using Pm3UsbApi.Native.Demod;
using Pm3UsbApi.Native.T55;

namespace Pm3UsbApi.Tests.Native;

[TestFixture]
public class Pm3UnsupportedModulationTests
{
    private static string FixturePath =>
        Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "Native", "t55-block0-samples.bin");

    [Test]
    public void TryFindPlausibleConfig_AskFixture_FindsAskModulation()
    {
        var work = DemodFixture(out var bitLen, out var clk);
        Assert.That(
            Pm3BitUtils.TryFindPlausibleConfig(work.AsSpan(0, bitLen), clk, out var offset, out _, out var modRead, out var block0),
            Is.True);
        Assert.That(offset, Is.EqualTo(32));
        Assert.That(modRead, Is.EqualTo(Pm3BitUtils.DemodAsk));
        Assert.That(block0, Is.EqualTo(0x00148040u));
    }

    [Test]
    public void TryDetectUnsupportedModulation_PskConfigInAskFixture_ReturnsPsk1()
    {
        var work = DemodFixture(out var bitLen, out var clk);
        var pskBlock0 = (0x00148040u & ~0x1F000u) | ((uint)Pm3BitUtils.DemodPsk1 << 12);
        WriteBlockBits(work, offset: 32, pskBlock0);

        Assert.That(
            Pm3T55ModulationScanner.TryDetectUnsupportedModulation(work.AsSpan(0, bitLen), clk, out var info),
            Is.True);
        Assert.That(info.Modulation, Is.EqualTo(Pm3BitUtils.DemodPsk1));
        Assert.That(info.Block0, Is.EqualTo(pskBlock0));
    }

    [Test]
    public void TryDetectUnsupportedModulation_UnmodifiedAskFixture_ReturnsFalse()
    {
        var work = DemodFixture(out var bitLen, out var clk);
        Assert.That(
            Pm3T55ModulationScanner.TryDetectUnsupportedModulation(work.AsSpan(0, bitLen), clk, out _),
            Is.False);
    }

    [Test]
    public void TryDetectUnsupportedModulation_GarbageBits_ReturnsFalse()
    {
        var work = new byte[512];
        Random.Shared.NextBytes(work);
        Assert.That(
            Pm3T55ModulationScanner.TryDetectUnsupportedModulation(work, 64, out _),
            Is.False);
    }

    [Test]
    public void Pm3UnsupportedModulationException_ContainsProcessFallbackGuidance()
    {
        var ex = new Pm3UnsupportedModulationException(Pm3BitUtils.DemodPsk1, 0x00141040u);
        Assert.That(ex.Message, Does.Contain("PM3_EXECUTOR=process"));
        Assert.That(ex.Message, Does.Contain("PSK1"));
        Assert.That(ex.Modulation, Is.EqualTo(Pm3BitUtils.DemodPsk1));
        Assert.That(ex.Block0, Is.EqualTo(0x00141040u));
    }

  private static byte[] DemodFixture(out int bitLen, out int clk)
    {
        if (!File.Exists(FixturePath))
            Assert.Ignore($"Captured fixture not found at {FixturePath}.");

        var raw = File.ReadAllBytes(FixturePath);
        var graph = new Pm3GraphState();
        graph.LoadSamples(raw);
        var work = new byte[raw.Length];
        var len = graph.CopyToByteSamples(work);
        graph.Signal.Compute(work.AsSpan(0, len));

        bitLen = len;
        clk = 0;
        var invert = 0;
        var st = true;
        var err = Pm3LfDemod.AskDemodExt(work, ref bitLen, ref clk, ref invert, maxErr: 1, askType: 1, ref st, graph.Signal);
        Assert.That(err, Is.InRange(0, 1));
        Assert.That(clk, Is.EqualTo(Pm3LfDemod.TokenClock));
        return work;
    }

    private static void WriteBlockBits(byte[] work, int offset, uint blockValue)
    {
        for (var i = 0; i < 32; i++)
            work[offset + i] = (byte)((blockValue >> (31 - i)) & 1);
    }
}
