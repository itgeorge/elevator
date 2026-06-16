namespace Pm3UsbApi.Native.Demod;

/// <summary>
/// Port of proxmark3 client/src/util.c PackBits and T55 modulation constants.
/// </summary>
internal static class Pm3BitUtils
{
    public const byte DemodAsk = 0x08;
    public const uint T55X7EmUniqueConfigBlock = 0x00148040;

    private static readonly int[] BitRateClocks = [8, 16, 32, 40, 50, 64, 100, 128];

    public static uint PackBits(int start, int len, ReadOnlySpan<byte> bits)
    {
        if (len > 32)
            return 0;

        var tmp = 0u;
        for (var j = len - 1; j >= 0; j--, start++)
            tmp |= (uint)(bits[start] & 1) << j;
        return tmp;
    }

    public static bool TestBitRate(byte readRate, int clk) =>
        readRate < BitRateClocks.Length && BitRateClocks[readRate] == clk;

    public static bool TestModulation(byte mode, byte modRead) =>
        mode == DemodAsk && modRead == DemodAsk;

    public static bool TestKnownConfigBlock(uint block0) =>
        block0 is 0x00088040 or 0x00088041 or T55X7EmUniqueConfigBlock or 0x000880E0 or 0x000880E1
            or 0x000880E2 or 0x000880E3 or 0x000880E4 or 0x000880E5 or 0x000880E6 or 0x000880E7
            or 0x000880E8 or 0x000880E9 or 0x000880EA or 0x000880EB or 0x000880EC or 0x000880ED
            or 0x000880EE or 0x000880EF or 0x000880F0 or 0x000880F1 or 0x000880F2 or 0x000880F3
            or 0x000880F4 or 0x000880F5 or 0x000880F6 or 0x000880F7 or 0x000880F8 or 0x000880F9
            or 0x000880FA or 0x000880FB or 0x000880FC or 0x000880FD or 0x000880FE or 0x000880FF
            or 0x00148040 or 0x00148041 or 0x00148042 or 0x00148043 or 0x00148044 or 0x00148045
            or 0x00148046 or 0x00148047 or 0x00148048 or 0x00148049 or 0x0014804A or 0x0014804B
            or 0x0014804C or 0x0014804D or 0x0014804E or 0x0014804F;

    public static bool TryFindConfigOffset(ReadOnlySpan<byte> demodBits, byte modulation, int clk, out byte offset, out byte bitrate)
    {
        offset = 0;
        bitrate = 0;

        if (demodBits.Length < 64)
            return false;

        for (byte idx = 28; idx < 64; idx++)
        {
            var si = idx;
            if (PackBits(si, 28, demodBits) == 0)
                continue;

            var safer = (byte)PackBits(si, 4, demodBits);
            si += 4;
            var resv = (byte)PackBits(si, 4, demodBits);
            si += 4;
            if (resv > 0)
                continue;

            var bitRate = (byte)PackBits(si, 6, demodBits);
            si += 6;
            var extend = (byte)PackBits(si, 1, demodBits);
            si += 1;
            var modRead = (byte)PackBits(si, 5, demodBits);

            var extMode = (safer is 0x6 or 0x9) && extend == 1;
            if (!extMode)
            {
                if (bitRate > 7 || !TestBitRate(bitRate, clk))
                    continue;
            }

            if (!TestModulation(modulation, modRead))
                continue;

            offset = idx;
            bitrate = bitRate;
            return true;
        }

        return false;
    }
}
