using System.Buffers.Binary;

namespace Pm3UsbApi.Native.Protocol;

/// <summary>
/// Proxmark3 firmware capabilities (capabilities_t v6/v7 from pm3_cmd.h).
/// </summary>
internal readonly record struct Pm3Capabilities(
    byte Version,
    uint Baudrate,
    uint BigBufSize,
    bool ViaFpc,
    bool ViaUsb,
    bool CompiledWithLf,
    bool IsRdv4,
    bool HwAvailableFlash,
    bool HwAvailableSmartcard)
{
    public const byte MinSupportedVersion = 6;
    public const byte CurrentVersion = 7;
    public const int StructSize = 13;

    public static Pm3Capabilities CreateDefault() =>
        new(
            CurrentVersion,
            Baudrate: 115200,
            BigBufSize: (uint)Pm3CommandCodes.DefaultT55SampleCount,
            ViaFpc: false,
            ViaUsb: true,
            CompiledWithLf: true,
            IsRdv4: false,
            HwAvailableFlash: false,
            HwAvailableSmartcard: false);

    public int T55SampleCount
    {
        get
        {
            if (BigBufSize == 0)
                return Pm3CommandCodes.DefaultT55SampleCount;

            var clamped = (int)Math.Min(BigBufSize, Pm3CommandCodes.DefaultT55SampleCount);
            return Math.Max(clamped, 255);
        }
    }

    public static bool TryDecode(ReadOnlySpan<byte> data, out Pm3Capabilities capabilities)
    {
        capabilities = default;
        if (data.Length < StructSize)
            return false;

        var version = data[0];
        if (version is < MinSupportedVersion or > CurrentVersion)
            return false;

        var baudrate = version >= CurrentVersion
            ? BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(1, 4))
            : 115200u;
        var bigBufSize = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(5, 4));
        var flags = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(9, 4));

        capabilities = new Pm3Capabilities(
            version,
            baudrate,
            bigBufSize,
            ViaFpc: GetBit(flags, 0),
            ViaUsb: GetBit(flags, 1),
            CompiledWithLf: GetBit(flags, 7),
            IsRdv4: GetBit(flags, 25),
            HwAvailableFlash: GetBit(flags, 23),
            HwAvailableSmartcard: GetBit(flags, 24));

        return true;
    }

    public static Pm3Capabilities Decode(ReadOnlySpan<byte> data)
    {
        if (!TryDecode(data, out var capabilities))
            throw new Pm3CapabilitiesException(
                $"Unsupported capabilities payload (length={data.Length}, version={(data.Length > 0 ? data[0] : 0)}, " +
                $"hex={Convert.ToHexString(data.Length <= 32 ? data : data[..32])}).");
        return capabilities;
    }

    public void EnsureLfSupported()
    {
        if (!CompiledWithLf)
            throw new Pm3CapabilitiesException("Proxmark3 firmware was not compiled with LF support; native T55 operations are unavailable.");
    }

    private static bool GetBit(uint value, int bit) => ((value >> bit) & 1) != 0;
}

internal sealed class Pm3CapabilitiesException : Exception
{
    public Pm3CapabilitiesException(string message) : base(message)
    {
    }
}
