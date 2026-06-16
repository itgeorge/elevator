using NUnit.Framework;
using Pm3UsbApi.Native.Demod;

namespace Pm3UsbApi.Tests.Native;

[TestFixture]
public class Pm3LfDemodTests
{
    [Test]
    public void AskDemodExt_SyntheticCleanAskWave_RecoversConfigBlock()
    {
        var dataBits = BuildConfigBlockBits(0x00148040u, offset: 32);
        var samples = BuildCleanAskSamples(dataBits, clk: 64, high: 191, low: 65);
        var signal = new Pm3SignalProperties();
        signal.Compute(samples);

        var work = (byte[])samples.Clone();
        var bitLen = work.Length;
        var clk = 0;
        var invert = 0;
        var st = true;

        var err = Pm3LfDemod.AskDemodExt(work, ref bitLen, ref clk, ref invert, maxErr: 1, askType: 1, ref st, signal);

        Assert.That(err, Is.GreaterThanOrEqualTo(0));
        Assert.That(clk, Is.EqualTo(64));
        Assert.That(bitLen, Is.GreaterThan(64));

        var ok = Pm3BitUtils.TryFindConfigOffset(work.AsSpan(0, bitLen), Pm3BitUtils.DemodAsk, clk, out var offset, out var bitrate);
        Assert.That(ok, Is.True);
        Assert.That(offset, Is.EqualTo(32));
        Assert.That(Pm3BitUtils.PackBits(offset, 32, work.AsSpan(0, bitLen)), Is.EqualTo(0x00148040u));
        Assert.That(bitrate, Is.EqualTo(5));
    }

    private static byte[] BuildConfigBlockBits(uint block0, int offset)
    {
        var bits = new byte[offset + 64];
        for (var i = 0; i < 32; i++)
            bits[offset + i] = (byte)((block0 >> (31 - i)) & 1);
        return bits;
    }

    private static byte[] BuildCleanAskSamples(byte[] dataBits, int clk, byte high, byte low)
    {
        var halfBits = new List<byte>(dataBits.Length * 2);
        foreach (var bit in dataBits)
        {
            if (bit == 0)
            {
                halfBits.Add(0);
                halfBits.Add(1);
            }
            else
            {
                halfBits.Add(1);
                halfBits.Add(0);
            }
        }

        var halfClk = clk / 2;
        var samples = new List<byte>(halfBits.Count * halfClk + 4000);
        foreach (var half in halfBits)
        {
            var level = half == 1 ? high : low;
            for (var i = 0; i < halfClk; i++)
                samples.Add(level);
        }

        while (samples.Count < 4000)
            samples.Add(low);

        return samples.ToArray();
    }
}
