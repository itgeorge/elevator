using System.Buffers.Binary;

namespace Pm3UsbApi.Native.Protocol;

/// <summary>
/// Parsed Proxmark3 response frame from the device.
/// </summary>
internal sealed class Pm3ResponseFrame
{
    public required ushort Command { get; init; }
    public required sbyte Status { get; init; }
    public required sbyte Reason { get; init; }
    public required bool IsNg { get; init; }
    public required byte[] Data { get; init; }
    public ulong[] OldArg { get; init; } = [0, 0, 0];
}

/// <summary>
/// Serializes and parses Proxmark3 NG/MIX/OLD packets (doc/new_frame_format.md).
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

    public static byte[] EncodeMixCommand(ushort command, ulong arg0, ulong arg1, ulong arg2, ReadOnlySpan<byte> extra = default)
    {
        var payload = new byte[Pm3CommandCodes.MixArgBytes + extra.Length];
        BinaryPrimitives.WriteUInt64LittleEndian(payload.AsSpan(0), arg0);
        BinaryPrimitives.WriteUInt64LittleEndian(payload.AsSpan(8), arg1);
        BinaryPrimitives.WriteUInt64LittleEndian(payload.AsSpan(16), arg2);
        if (!extra.IsEmpty)
            extra.CopyTo(payload.AsSpan(Pm3CommandCodes.MixArgBytes));
        return EncodeCommand(command, payload, ng: false);
    }

    public static Pm3ResponseFrame DecodeAnyResponse(ReadOnlySpan<byte> frame)
    {
        if (frame.Length >= 10 &&
            BinaryPrimitives.ReadUInt32LittleEndian(frame) == Pm3CommandCodes.ResponsePreambleMagic)
            return DecodeResponse(frame);

        if (frame.Length >= Pm3CommandCodes.OldFrameSize)
            return DecodeOldResponse(frame);

        throw new InvalidOperationException($"Unrecognized response frame ({frame.Length} bytes).");
    }

    public static Pm3ResponseFrame DecodeOldResponse(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < Pm3CommandCodes.OldFrameSize)
            throw new InvalidOperationException($"OLD response frame too short ({frame.Length} bytes).");

        var cmd = BinaryPrimitives.ReadUInt64LittleEndian(frame);
        return new Pm3ResponseFrame
        {
            Command = (ushort)(cmd & 0xFFFF),
            Status = Pm3CommandCodes.Pm3Success,
            Reason = 0,
            IsNg = false,
            OldArg =
            [
                BinaryPrimitives.ReadUInt64LittleEndian(frame[8..]),
                BinaryPrimitives.ReadUInt64LittleEndian(frame[16..]),
                BinaryPrimitives.ReadUInt64LittleEndian(frame[24..]),
            ],
            Data = frame.Slice(32, Pm3CommandCodes.MaxDataSize).ToArray(),
        };
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

        var rawData = frame.Slice(10, length).ToArray();
        var crc = BinaryPrimitives.ReadUInt16LittleEndian(frame[(10 + length)..]);
        if (crc != Pm3CommandCodes.ResponsePostambleMagic)
            throw new InvalidOperationException($"Unexpected response postamble 0x{crc:X4}.");

        byte[] data;
        ulong[] oldArg = [0, 0, 0];
        if (ng)
        {
            data = rawData;
        }
        else
        {
            if (rawData.Length < Pm3CommandCodes.MixArgBytes)
                throw new InvalidOperationException("MIX response payload too short for oldarg.");

            oldArg[0] = BinaryPrimitives.ReadUInt64LittleEndian(rawData.AsSpan(0));
            oldArg[1] = BinaryPrimitives.ReadUInt64LittleEndian(rawData.AsSpan(8));
            oldArg[2] = BinaryPrimitives.ReadUInt64LittleEndian(rawData.AsSpan(16));
            data = rawData.Length > Pm3CommandCodes.MixArgBytes
                ? rawData[Pm3CommandCodes.MixArgBytes..]
                : [];
        }

        return new Pm3ResponseFrame
        {
            Command = command,
            Status = status,
            Reason = reason,
            IsNg = ng,
            Data = data,
            OldArg = oldArg,
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
