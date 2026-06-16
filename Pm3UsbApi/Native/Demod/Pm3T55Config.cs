namespace Pm3UsbApi.Native.Demod;

/// <summary>
/// T55x7 session configuration discovered from block 0 (mirrors proxmark3 client config).
/// </summary>
internal sealed class Pm3T55Config
{
    public const byte DemodAsk = 0x08;
    public const uint EmUniqueConfigBlock = 0x00148040;

    public byte Modulation { get; set; }
    public byte Bitrate { get; set; }
    public bool Inverted { get; set; }
    public bool SequenceTerminator { get; set; }
    public byte Offset { get; set; }
    public byte DownlinkMode { get; set; }
    public uint Block0 { get; set; }
    public bool Detected { get; set; }
    public bool UsePassword { get; set; }
    public uint Password { get; set; }
    public int Clock { get; set; }

    public int BitrateClock => Bitrate switch
    {
        0 => 8,
        1 => 16,
        2 => 32,
        3 => 40,
        4 => 50,
        5 => 64,
        6 => 100,
        7 => 128,
        _ => 64,
    };

    public void ApplyDetection(
        byte modulation,
        byte bitrate,
        bool inverted,
        bool sequenceTerminator,
        byte offset,
        byte downlinkMode,
        uint block0,
        int clock)
    {
        Modulation = modulation;
        Bitrate = bitrate;
        Inverted = inverted;
        SequenceTerminator = sequenceTerminator;
        Offset = offset;
        DownlinkMode = downlinkMode;
        Block0 = block0;
        Clock = clock;
        Detected = true;
        UsePassword = false;
        Password = 0;
    }

    public static string ModulationName(byte modulation) => modulation switch
    {
        DemodAsk => "ASK",
        _ => "unknown",
    };
}
