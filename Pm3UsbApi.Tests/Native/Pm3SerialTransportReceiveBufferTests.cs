using NUnit.Framework;
using Pm3UsbApi.Native.Demod;
using Pm3UsbApi.Native.Protocol;
using Pm3UsbApi.Native.Transport;

namespace Pm3UsbApi.Tests.Native;

[TestFixture]
public class Pm3SerialTransportReceiveBufferTests
{
    [Test]
    public void TryExtractResponseFrame_PreservesTrailingBytesForSecondFrame()
    {
        var old1 = BuildOldResponseFrame(Pm3CommandCodes.CmdDownloadedBigBuf, 0, 4, [1, 2, 3, 4]);
        var old2 = BuildOldResponseFrame(Pm3CommandCodes.CmdDownloadedBigBuf, 4, 4, [5, 6, 7, 8]);
        var buffer = new List<byte>(old1.Concat(old2));

        Assert.That(Pm3SerialTransport.TryExtractResponseFrame(buffer, out var first), Is.True);
        Assert.That(first, Is.EqualTo(old1));
        Assert.That(buffer, Has.Count.EqualTo(old2.Length));

        Assert.That(Pm3SerialTransport.TryExtractResponseFrame(buffer, out var second), Is.True);
        Assert.That(second, Is.EqualTo(old2));
        Assert.That(buffer, Is.Empty);
    }

    private static byte[] BuildOldResponseFrame(ushort command, ulong arg0, ulong arg1, byte[] payload)
    {
        var frame = new byte[Pm3CommandCodes.OldFrameSize];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(frame.AsSpan(0), command);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(frame.AsSpan(8), arg0);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(frame.AsSpan(16), arg1);
        payload.AsSpan(0, Math.Min(payload.Length, Pm3CommandCodes.MaxDataSize)).CopyTo(frame.AsSpan(32));
        return frame;
    }
}
