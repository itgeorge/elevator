namespace Pm3UsbApi.Native.Demod;

/// <summary>
/// Port of proxmark3 client/src/util.c PackBits and T55 modulation constants.
/// </summary>
internal static class Pm3BitUtils
{
    public const byte DemodAsk = 0x08;
    public const byte DemodPsk1 = 0x01;
    public const uint T55X7EmUniqueConfigBlock = 0x00148040;

    internal static readonly int[] BitRateClocks = [8, 16, 32, 40, 50, 64, 100, 128];

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

    public static bool IsKnownConfigModulationVariant(uint block0)
    {
        var baseConfig = block0 & ~0x1F000u;
        ReadOnlySpan<uint> known =
        [
            0x00088040, 0x00088041, T55X7EmUniqueConfigBlock, 0x000880E0, 0x000880E1,
            0x000880E2, 0x000880E3, 0x000880E4, 0x000880E5, 0x000880E6, 0x000880E7,
            0x000880E8, 0x000880E9, 0x000880EA, 0x000880EB, 0x000880EC, 0x000880ED,
            0x000880EE, 0x000880EF, 0x000880F0, 0x000880F1, 0x000880F2, 0x000880F3,
            0x000880F4, 0x000880F5, 0x000880F6, 0x000880F7, 0x000880F8, 0x000880F9,
            0x000880FA, 0x000880FB, 0x000880FC, 0x000880FD, 0x000880FE, 0x000880FF,
            0x00148040, 0x00148041, 0x00148042, 0x00148043, 0x00148044, 0x00148045,
            0x00148046, 0x00148047, 0x00148048, 0x00148049, 0x0014804A, 0x0014804B,
            0x0014804C, 0x0014804D, 0x0014804E, 0x0014804F,
        ];

        foreach (var candidate in known)
        {
            if ((candidate & ~0x1F000u) == baseConfig)
                return true;
        }

        return false;
    }

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

    public static bool TryFindPlausibleConfig(
        ReadOnlySpan<byte> demodBits,
        int clk,
        out byte offset,
        out byte bitrate,
        out byte modRead,
        out uint block0)
    {
        offset = 0;
        bitrate = 0;
        modRead = 0;
        block0 = 0;

        if (demodBits.Length < 64)
            return false;

        for (byte idx = 28; idx < 64; idx++)
        {
            if (!TryParseConfigCandidate(demodBits, idx, clk, out offset, out bitrate, out modRead, out block0))
                continue;

            return true;
        }

        return false;
    }

    internal static bool TryParseConfigCandidate(
        ReadOnlySpan<byte> demodBits,
        byte idx,
        int clk,
        out byte offset,
        out byte bitrate,
        out byte modRead,
        out uint block0)
    {
        offset = 0;
        bitrate = 0;
        modRead = 0;
        block0 = 0;

        var si = idx;
        if (PackBits(si, 28, demodBits) == 0)
            return false;

        var safer = (byte)PackBits(si, 4, demodBits);
        si += 4;
        var resv = (byte)PackBits(si, 4, demodBits);
        si += 4;
        if (resv > 0)
            return false;

        var bitRate = (byte)PackBits(si, 6, demodBits);
        si += 6;
        var extend = (byte)PackBits(si, 1, demodBits);
        si += 1;
        modRead = (byte)PackBits(si, 5, demodBits);

        var extMode = (safer is 0x6 or 0x9) && extend == 1;
        if (!extMode)
        {
            if (bitRate > 7 || !TestBitRate(bitRate, clk))
                return false;
        }

        block0 = PackBits(idx, 32, demodBits);
        if (block0 == 0)
            return false;

        offset = idx;
        bitrate = bitRate;
        return true;
    }
}
