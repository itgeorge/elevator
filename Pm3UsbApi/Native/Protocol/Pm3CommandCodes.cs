namespace Pm3UsbApi.Native.Protocol;

/// <summary>
/// Proxmark3 NG command and status codes from include/pm3_cmd.h.
/// </summary>
internal static class Pm3CommandCodes
{
    public const ushort CmdVersion = 0x0107;
    public const ushort CmdPing = 0x0109;
    public const ushort CmdMeasureAntennaTuningLf = 0x0402;

    public const sbyte Pm3Success = 0;
    public const sbyte Pm3EopAborted = -1;

    public const uint CommandPreambleMagic = 0x61334D50; // "PM3a"
    public const uint ResponsePreambleMagic = 0x62334D50; // "PM3b"
    public const ushort CommandPostambleMagic = 0x3361;   // "a3"
    public const ushort ResponsePostambleMagic = 0x3362;   // "b3"

  public const int MaxDataSize = 512;

    /// <summary>LF_FREQ2DIV(125) from pm3_cmd.h.</summary>
    public const byte LfDivisor125 = 95;
}
