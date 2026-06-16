using NUnit.Framework;
using Pm3UsbApi.Native.Demod;

namespace Pm3UsbApi.Tests.Native;

[TestFixture]
public class Pm3BitUtilsTests
{
    [Test]
    public void PackBits_KnownPattern_MatchesUInt()
    {
        var bits = new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 1, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0 };
        var value = Pm3BitUtils.PackBits(0, 32, bits);
        Assert.That(value, Is.EqualTo(0x00148040u));
    }

    [Test]
    public void TryFindConfigOffset_EmUniqueBlock_FindsOffset()
    {
        var bits = BuildConfigBlockBits(0x00148040u, offset: 32);
        var ok = Pm3BitUtils.TryFindConfigOffset(bits, Pm3BitUtils.DemodAsk, clk: 64, out var offset, out var bitrate);
        Assert.That(ok, Is.True);
        Assert.That(offset, Is.EqualTo(32));
        Assert.That(bitrate, Is.EqualTo(5));
    }

    private static byte[] BuildConfigBlockBits(uint block0, int offset)
    {
        var bits = new byte[96];
        for (var i = 0; i < 32; i++)
        {
            var bit = (block0 >> (31 - i)) & 1;
            bits[offset + i] = (byte)bit;
        }

        return bits;
    }
}
