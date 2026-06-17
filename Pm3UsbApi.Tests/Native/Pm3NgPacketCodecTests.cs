using System.Buffers.Binary;
using NUnit.Framework;
using Pm3UsbApi.Native.Protocol;

namespace Pm3UsbApi.Tests.Native;

[TestFixture]
public class Pm3NgPacketCodecTests
{
    [Test]
    public void EncodeCommand_Ping_MatchesReferenceFrame()
    {
        var bytes = Pm3NgPacketCodec.EncodeCommand(Pm3CommandCodes.CmdPing, ReadOnlySpan<byte>.Empty);
        Assert.That(ToHex(bytes), Is.EqualTo("504D3361008009016133"));
    }

    [Test]
    public void DecodeResponse_Ping_MatchesReferenceFrame()
    {
        var frame = ParseHex("504D33620080000009016233");
        var response = Pm3NgPacketCodec.DecodeResponse(frame);

        Assert.That(response.Command, Is.EqualTo(Pm3CommandCodes.CmdPing));
        Assert.That(response.Status, Is.EqualTo(Pm3CommandCodes.Pm3Success));
        Assert.That(response.Reason, Is.EqualTo((sbyte)0));
        Assert.That(response.IsNg, Is.True);
        Assert.That(response.Data, Is.Empty);
    }

    [Test]
    public void RoundTrip_LfTuneInitPayload_EncodesLength()
    {
        var payload = new byte[] { 1, Pm3CommandCodes.LfDivisor125 };
        var bytes = Pm3NgPacketCodec.EncodeCommand(Pm3CommandCodes.CmdMeasureAntennaTuningLf, payload);

        Assert.That(bytes.Length, Is.EqualTo(8 + 2 + 2));
        Pm3NgPacketCodec.UnpackLengthNg(
            BitConverter.ToUInt16(bytes, 4),
            out var length,
            out var ng);
        Assert.That(length, Is.EqualTo(2));
        Assert.That(ng, Is.True);
        Assert.That(BitConverter.ToUInt16(bytes, 6), Is.EqualTo(Pm3CommandCodes.CmdMeasureAntennaTuningLf));
        Assert.That(bytes[8], Is.EqualTo(1));
        Assert.That(bytes[9], Is.EqualTo(Pm3CommandCodes.LfDivisor125));
    }

    [Test]
    public void EncodeMixCommand_DownloadBigBuf_UsesNgFalseLength()
    {
        var bytes = Pm3NgPacketCodec.EncodeMixCommand(
            Pm3CommandCodes.CmdDownloadBigBuf,
            arg0: 0,
            arg1: 12000,
            arg2: 0);

        Pm3NgPacketCodec.UnpackLengthNg(
            BitConverter.ToUInt16(bytes, 4),
            out var length,
            out var ng);

        Assert.That(ng, Is.False);
        Assert.That(length, Is.EqualTo(Pm3CommandCodes.MixArgBytes));
        Assert.That(BitConverter.ToUInt16(bytes, 6), Is.EqualTo(Pm3CommandCodes.CmdDownloadBigBuf));
    }

    [Test]
    public void DecodeResponse_MixFrame_ParsesOldArgAndPayload()
    {
        var mixPayload = new byte[Pm3CommandCodes.MixArgBytes + 4];
        BitConverter.TryWriteBytes(mixPayload.AsSpan(0), 512UL);
        BitConverter.TryWriteBytes(mixPayload.AsSpan(8), 4UL);
        mixPayload[24] = 0xAA;
        mixPayload[25] = 0xBB;
        mixPayload[26] = 0xCC;
        mixPayload[27] = 0xDD;

        var frame = BuildMixResponse(Pm3CommandCodes.CmdDownloadedBigBuf, mixPayload);
        var response = Pm3NgPacketCodec.DecodeResponse(frame);

        Assert.That(response.IsNg, Is.False);
        Assert.That(response.Command, Is.EqualTo(Pm3CommandCodes.CmdDownloadedBigBuf));
        Assert.That(response.OldArg[0], Is.EqualTo(512UL));
        Assert.That(response.OldArg[1], Is.EqualTo(4UL));
        Assert.That(Convert.ToHexString(response.Data), Is.EqualTo("AABBCCDD"));
    }

    private static byte[] BuildMixResponse(ushort command, byte[] mixPayload)
    {
        var frame = new byte[10 + mixPayload.Length + 2];
        BitConverter.TryWriteBytes(frame.AsSpan(0), Pm3CommandCodes.ResponsePreambleMagic);
        BitConverter.TryWriteBytes(frame.AsSpan(4), (ushort)mixPayload.Length);
        frame[6] = 0;
        frame[7] = 0;
        BitConverter.TryWriteBytes(frame.AsSpan(8), command);
        mixPayload.CopyTo(frame.AsSpan(10));
        BitConverter.TryWriteBytes(frame.AsSpan(10 + mixPayload.Length), Pm3CommandCodes.ResponsePostambleMagic);
        return frame;
    }

    [Test]
    public void DecodeOldResponse_DownloadedBigBuf_ParsesChunk()
    {
        var frame = new byte[Pm3CommandCodes.OldFrameSize];
        BinaryPrimitives.WriteUInt64LittleEndian(frame.AsSpan(0), Pm3CommandCodes.CmdDownloadedBigBuf);
        BinaryPrimitives.WriteUInt64LittleEndian(frame.AsSpan(8), 512);
        BinaryPrimitives.WriteUInt64LittleEndian(frame.AsSpan(16), 128);
        frame[32] = 0x42;
        frame[33] = 0x43;

        var response = Pm3NgPacketCodec.DecodeOldResponse(frame);
        Assert.That(response.Command, Is.EqualTo(Pm3CommandCodes.CmdDownloadedBigBuf));
        Assert.That(response.OldArg[0], Is.EqualTo(512UL));
        Assert.That(response.OldArg[1], Is.EqualTo(128UL));
        Assert.That(response.Data[0], Is.EqualTo(0x42));
        Assert.That(response.Data[1], Is.EqualTo(0x43));
    }

    private static string ToHex(byte[] bytes) => Convert.ToHexString(bytes);

    private static byte[] ParseHex(string hex) =>
        Convert.FromHexString(hex.Replace(" ", string.Empty));
}
