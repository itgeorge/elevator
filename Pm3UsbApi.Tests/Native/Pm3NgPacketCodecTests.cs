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

    private static string ToHex(byte[] bytes) => Convert.ToHexString(bytes);

    private static byte[] ParseHex(string hex) =>
        Convert.FromHexString(hex.Replace(" ", string.Empty));
}
