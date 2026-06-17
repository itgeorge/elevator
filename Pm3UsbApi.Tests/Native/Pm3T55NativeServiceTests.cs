using System.Buffers.Binary;
using NUnit.Framework;
using Pm3UsbApi.Native.T55;

namespace Pm3UsbApi.Tests.Native;

[TestFixture]
public class Pm3T55NativeServiceTests
{
    [Test]
    public void BuildWriteFlags_Page0NoPassword_UsesDownlinkModeInHighBits()
    {
        Assert.That(Pm3T55NativeService.BuildWriteFlags(false, false, false, 0), Is.EqualTo(0));
        Assert.That(Pm3T55NativeService.BuildWriteFlags(false, false, false, 2), Is.EqualTo(0x10));
    }

    [Test]
    public void BuildWriteFlags_OptionalBits_MatchProxmark3Layout()
    {
        Assert.That(Pm3T55NativeService.BuildWriteFlags(true, true, true, 1), Is.EqualTo(0x0F));
    }

    [Test]
    public void BuildWriteBlockPayload_Page0Write_MatchesLittleEndianLayout()
    {
        var payload = Pm3T55NativeService.BuildWriteBlockPayload(
            data: 0xDEADBEEF,
            password: 0,
            block: 5,
            usePassword: false,
            page1: false,
            testMode: false,
            downlinkMode: 2);

        Assert.That(payload, Has.Length.EqualTo(10));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(0)), Is.EqualTo(0xDEADBEEFu));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(4)), Is.EqualTo(0u));
        Assert.That(payload[8], Is.EqualTo(5));
        Assert.That(payload[9], Is.EqualTo(0x10));
    }
}
