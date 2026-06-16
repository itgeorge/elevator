using System.Buffers.Binary;

namespace Pm3UsbApi.Native.Protocol;

/// <summary>
/// Parsed PacketResponseNG frame from the device.
/// </summary>
internal sealed class Pm3ResponseFrame
{
    public required ushort Command { get; init; }
    public required sbyte Status { get; init; }
    public required sbyte Reason { get; init; }
    public required bool IsNg { get; init; }
    public required byte[] Data { get; init; }
}

/// <summary>
/// Serializes and parses Proxmark3 NG packets (doc/new_frame_format.md).
/// USB uses magic postamble placeholders instead of CRC.
/// </summary>
internal static class Pm3NgPacketCodec
{
    public static byte[] EncodeCommand(ushort command, ReadOnlySpan<byte> data, bool ng = true)
    {
        if (data.Length > Pm3CommandCodes.MaxDataSize)
            throw new ArgumentOutOfRangeException(nameof(data), "Payload exceeds PM3_CMD_DATA_SIZE.");

        var length = (ushort)data.Length;
        var buffer = new byte[8 + data.Length + 2];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(0, 4), Pm3CommandCodes.CommandPreambleMagic);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(4, 2), PackLengthNg(length, ng));
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(6, 2), command);
        if (!data.IsEmpty)
            data.CopyTo(buffer.AsSpan(8, data.Length));
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(8 + data.Length, 2), Pm3CommandCodes.CommandPostambleMagic);
        return buffer;
    }

    public static Pm3ResponseFrame DecodeResponse(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < 12)
            throw new InvalidOperationException($"Response frame too short ({frame.Length} bytes).");

        var magic = BinaryPrimitives.ReadUInt32LittleEndian(frame);
        if (magic != Pm3CommandCodes.ResponsePreambleMagic)
            throw new InvalidOperationException($"Unexpected response magic 0x{magic:X8}.");

        UnpackLengthNg(BinaryPrimitives.ReadUInt16LittleEndian(frame[4..]), out var length, out var ng);
        var status = unchecked((sbyte)frame[6]);
        var reason = unchecked((sbyte)frame[7]);
        var command = BinaryPrimitives.ReadUInt16LittleEndian(frame[8..]);

        var expectedLength = 10 + length + 2;
        if (frame.Length < expectedLength)
            throw new InvalidOperationException($"Incomplete response frame: expected {expectedLength}, got {frame.Length}.");

        var data = frame.Slice(10, length).ToArray();
        var crc = BinaryPrimitives.ReadUInt16LittleEndian(frame[(10 + length)..]);
        if (crc != Pm3CommandCodes.ResponsePostambleMagic)
            throw new InvalidOperationException($"Unexpected response postamble 0x{crc:X4}.");

        return new Pm3ResponseFrame
        {
            Command = command,
            Status = status,
            Reason = reason,
            IsNg = ng,
            Data = data,
        };
    }

    public static ushort PackLengthNg(ushort length, bool ng)
    {
        if (length > 0x7FFF)
            throw new ArgumentOutOfRangeException(nameof(length));
        return ng ? (ushort)(length | 0x8000) : length;
    }

    public static void UnpackLengthNg(ushort value, out ushort length, out bool ng)
    {
        ng = (value & 0x8000) != 0;
        length = (ushort)(value & 0x7FFF);
    }
}
