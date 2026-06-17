using System.Text.Json;
using NUnit.Framework;
using Pm3UsbApi;
using Pm3UsbApi.Native.Demod;
using Pm3UsbApi.Native.T55;

namespace Pm3UsbApi.Tests.Native;

[TestFixture]
public class Pm3UnsupportedChipTypeTests
{
    private const ulong KnownEm410xId = 0x1400711C5DuL;

    private static string FixturePath =>
        Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "Native", "em410x-samples.bin");

    private static string MetaPath =>
        Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "Native", "em410x-samples.json");

    [Test]
    public void BuildSyntheticEm410xBits_HasExpectedLength()
    {
        var bits = Pm3LfEm410x.BuildSyntheticDemodBits(KnownEm410xId);
        Assert.That(bits.Length, Is.EqualTo(65));
        Assert.That(bits.Take(10), Is.EqualTo(new byte[] { 0, 1, 1, 1, 1, 1, 1, 1, 1, 1 }));
    }

    [Test]
    public void TryDetectNonT55FromModulationScan_Em410xFixture_ReturnsNonT55Lf()
    {
        var (samples, signal) = LoadFixture();
        var work = DemodAskInverted(samples, signal, out var bitLen, out var clk);

        Assert.That(
            Pm3T55ModulationScanner.TryDetectUnsupportedModulation(work.AsSpan(0, bitLen), clk, out var modInfo),
            Is.True);
        Assert.That(modInfo.Modulation, Is.EqualTo(0x05));
        Assert.That(
            Pm3BitUtils.IsKnownConfigModulationVariant(modInfo.Block0),
            Is.False);
    }

    public void TryDetectUnsupportedModulation_AskFixture_WithKnownPskMutation_StillModulation()
    {
        var work = DemodAskFixture(out var bitLen, out var clk);
        var pskBlock0 = (0x00148040u & ~0x1F000u) | ((uint)Pm3BitUtils.DemodPsk1 << 12);
        WriteBlockBits(work, offset: 32, pskBlock0);

        Assert.That(
            Pm3T55ModulationScanner.TryDetectUnsupportedModulation(work.AsSpan(0, bitLen), clk, out var info),
            Is.True);
        Assert.That(info.Block0, Is.EqualTo(pskBlock0));
        Assert.That(Pm3BitUtils.IsKnownConfigModulationVariant(info.Block0), Is.True);
    }

    [Test]
    public void Pm3UnsupportedChipTypeException_Em410x_ContainsId()
    {
        var ex = new Pm3UnsupportedChipTypeException(Pm3LfChipFamily.Em410x, KnownEm410xId, 64);
        Assert.That(ex.Message, Does.Contain("EM410x"));
        Assert.That(ex.Message, Does.Contain("1400711C5D"));
        Assert.That(ex.Message, Does.Not.Contain("PM3_EXECUTOR=process"));
    }

    [Test]
    public void Pm3UnsupportedChipTypeException_NonT55Lf_DoesNotMentionProcessFallback()
    {
        var ex = new Pm3UnsupportedChipTypeException(Pm3LfChipFamily.NonT55Lf, 0, 64);
        Assert.That(ex.Message, Does.Contain("non-T55 LF"));
        Assert.That(ex.Message, Does.Not.Contain("PM3_EXECUTOR=process"));
        Assert.That(ex.Message, Does.Not.Contain("FSK2"));
    }

    [Test]
    public void Em410xFixtureMetadata_DocumentsPm3ReaderId()
    {
        if (!File.Exists(MetaPath))
            Assert.Ignore($"Metadata not found at {MetaPath}.");

        using var doc = JsonDocument.Parse(File.ReadAllText(MetaPath));
        Assert.That(doc.RootElement.GetProperty("cardIdHex").GetString(), Is.EqualTo("1400711C5D"));
        Assert.That(doc.RootElement.GetProperty("chipFamily").GetString(), Is.EqualTo("EM410x"));
    }

    private static (byte[] Samples, Pm3SignalProperties Signal) LoadFixture()
    {
        if (!File.Exists(FixturePath))
            Assert.Ignore($"Captured fixture not found at {FixturePath}.");

        var raw = File.ReadAllBytes(FixturePath);
        var graph = new Pm3GraphState();
        graph.LoadSamples(raw);
        var bytes = new byte[raw.Length];
        var len = graph.CopyToByteSamples(bytes);
        graph.Signal.Compute(bytes.AsSpan(0, len));
        return (bytes.AsSpan(0, len).ToArray(), graph.Signal);
    }

    private static byte[] DemodAskInverted(byte[] samples, Pm3SignalProperties signal, out int bitLen, out int clk)
    {
        var work = (byte[])samples.Clone();
        bitLen = work.Length;
        clk = 0;
        var invert = 1;
        var st = true;
        var err = Pm3LfDemod.AskDemodExt(work, ref bitLen, ref clk, ref invert, maxErr: 1, askType: 1, ref st, signal);
        Assert.That(err, Is.EqualTo(0));
        Assert.That(clk, Is.EqualTo(64));
        return work;
    }

    private static byte[] DemodAskFixture(out int bitLen, out int clk)
    {
        var fixturePath = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "Native", "t55-block0-samples.bin");
        if (!File.Exists(fixturePath))
            Assert.Ignore($"Captured fixture not found at {fixturePath}.");

        var raw = File.ReadAllBytes(fixturePath);
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
        return work;
    }

    private static void WriteBlockBits(byte[] work, int offset, uint blockValue)
    {
        for (var i = 0; i < 32; i++)
            work[offset + i] = (byte)((blockValue >> (31 - i)) & 1);
    }
}
