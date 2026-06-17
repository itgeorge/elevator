using NUnit.Framework;
using Pm3UsbApi.Native.Protocol;

namespace Pm3UsbApi.Tests.Native;

[TestFixture]
public class Pm3CapabilitiesTests
{
    private static readonly byte[] GoldenV7 =
    [
        0x07,
        0x00, 0xC2, 0x01, 0x00,
        0x44, 0x94, 0x00, 0x00,
        0x82, 0x00, 0x00, 0x02,
    ];

    private static readonly byte[] DeviceV6 =
    [
        0x06,
        0x00, 0x00, 0x00, 0x00,
        0x44, 0x94, 0x00, 0x00,
        0x8E, 0xFF, 0xDF, 0x21,
    ];

    [Test]
    public void TryDecode_GoldenV7_MatchesExpectedFields()
    {
        Assert.That(Pm3Capabilities.TryDecode(GoldenV7, out var caps), Is.True);
        Assert.That(caps.Version, Is.EqualTo(7));
        Assert.That(caps.Baudrate, Is.EqualTo(115200u));
        Assert.That(caps.BigBufSize, Is.EqualTo(37956u));
        Assert.That(caps.ViaUsb, Is.True);
        Assert.That(caps.CompiledWithLf, Is.True);
        Assert.That(caps.IsRdv4, Is.True);
        Assert.That(caps.ViaFpc, Is.False);
    }

    [Test]
    public void T55SampleCount_ClampsToDefaultMax()
    {
        var caps = Pm3Capabilities.Decode(GoldenV7);
        Assert.That(caps.T55SampleCount, Is.EqualTo(12000));
    }

    [Test]
    public void T55SampleCount_UsesSmallerBigBufWhenNeeded()
    {
        var bytes = (byte[])GoldenV7.Clone();
        bytes[5] = 0x00;
        bytes[6] = 0x10; // 4096
        bytes[7] = 0x00;
        bytes[8] = 0x00;

        var caps = Pm3Capabilities.Decode(bytes);
        Assert.That(caps.BigBufSize, Is.EqualTo(4096u));
        Assert.That(caps.T55SampleCount, Is.EqualTo(4096));
    }

    [Test]
    public void TryDecode_DeviceV6_MatchesBigBufAndLf()
    {
        Assert.That(Pm3Capabilities.TryDecode(DeviceV6, out var caps), Is.True);
        Assert.That(caps.Version, Is.EqualTo(6));
        Assert.That(caps.Baudrate, Is.EqualTo(115200u));
        Assert.That(caps.BigBufSize, Is.EqualTo(37956u));
        Assert.That(caps.CompiledWithLf, Is.True);
    }

    [Test]
    public void TryDecode_WrongVersion_ReturnsFalse()
    {
        var bytes = (byte[])GoldenV7.Clone();
        bytes[0] = 5;
        Assert.That(Pm3Capabilities.TryDecode(bytes, out _), Is.False);
    }

    [Test]
    public void TryDecode_TooShort_ReturnsFalse()
    {
        Assert.That(Pm3Capabilities.TryDecode(GoldenV7.AsSpan(0, 8), out _), Is.False);
    }

    [Test]
    public void Decode_LfDisabled_ThrowsCapabilitiesException()
    {
        var bytes = (byte[])GoldenV7.Clone();
        bytes[9] = 0x02; // via_usb only, no compiled_with_lf bit

        var caps = Pm3Capabilities.Decode(bytes);
        Assert.That(caps.CompiledWithLf, Is.False);
    }
}
