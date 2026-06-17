namespace Pm3UsbApi;

/// <summary>
/// Human-readable T55 modulation names from proxmark3 client t55xx_modulation.
/// </summary>
public static class Pm3T55ModulationNames
{
    public const byte DemodAsk = 0x08;
    public const byte DemodPsk1 = 0x01;

    public static string Name(byte modulation) => modulation switch
    {
        0x00 => "NRZ",
        0x01 => "PSK1",
        0x02 => "PSK2",
        0x03 => "PSK3",
        0x04 => "FSK1",
        0x05 => "FSK2",
        0x06 => "FSK1a",
        0x07 => "FSK2a",
        0x08 => "ASK",
        0x10 => "BI",
        0x18 => "BIa",
        0xF0 => "FSK",
        _ => $"0x{modulation:X2}",
    };

    public static bool IsRecognizedNonAsk(byte modulation) =>
        modulation != DemodAsk && modulation is 0x00 or 0x01 or 0x02 or 0x03 or 0x04 or 0x05 or 0x06 or 0x07 or 0x10 or 0x18 or 0xF0;
}
