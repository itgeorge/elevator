namespace Pm3UsbApi.Native.Protocol;

/// <summary>
/// Proxmark3 NG/MIX command and status codes from include/pm3_cmd.h.
/// </summary>
internal static class Pm3CommandCodes
{
    public const ushort CmdAck = 0x00FF;
    public const ushort CmdWtx = 0x0116;
    public const ushort CmdNack = 0x00FE;
    public const ushort CmdVersion = 0x0107;
    public const ushort CmdPing = 0x0109;
    public const ushort CmdMeasureAntennaTuningLf = 0x0402;

    public const ushort CmdDownloadBigBuf = 0x0207;
    public const ushort CmdDownloadedBigBuf = 0x0208;
    public const ushort CmdLfT55XxReadBl = 0x0214;
    public const ushort CmdLfT55XxWriteBl = 0x0215;

    public const sbyte Pm3Success = 0;
    public const sbyte Pm3EopAborted = -1;

    public const uint CommandPreambleMagic = 0x61334D50; // "PM3a"
    public const uint ResponsePreambleMagic = 0x62334D50; // "PM3b"
    public const ushort CommandPostambleMagic = 0x3361;   // "a3"
    public const ushort ResponsePostambleMagic = 0x3362;   // "b3"

    public const int MaxDataSize = 512;
    public const int MixArgBytes = 24;
    public const int OldFrameSize = 544;

    /// <summary>LF_FREQ2DIV(125) from pm3_cmd.h.</summary>
    public const byte LfDivisor125 = 95;

    public const int T55SampleCount = 12000;
    public const int BigBufDownloadSize = 65536;
}
