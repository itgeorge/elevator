namespace Pm3UsbApi.Native.Demod;

/// <summary>
/// Minimal EM410x decode for unsupported chip-type detection.
/// Ported from proxmark3 common/lfdemod.c (Em410xDecode, removeEm410xParity).
/// </summary>
internal static class Pm3LfEm410x
{
    public const int DefaultClock = 64;

    public static bool TryDetectId(
        ReadOnlySpan<byte> samples,
        Pm3SignalProperties signal,
        out ulong cardId,
        out int clock)
    {
        cardId = 0;
        clock = DefaultClock;

        if (samples.Length < 255 || signal.IsNoise)
            return false;

        var work = samples.ToArray();
        foreach (var invert in new[] { 0, 1 })
        {
            var bits = (byte[])work.Clone();
            var bitLen = bits.Length;
            var clk = 0;
            var invertInt = invert;
            var st = true;

            var err = Pm3LfDemod.AskDemodExt(
                bits,
                ref bitLen,
                ref clk,
                ref invertInt,
                maxErr: 100,
                askType: 1,
                ref st,
                signal);

            if (err < 0 || bitLen < 64)
                continue;

            if (TryDecode(bits, bitLen, out var hi, out var lo) && hi == 0 && lo != 0)
            {
                cardId = lo;
                clock = clk > 0 ? clk : DefaultClock;
                return true;
            }
        }

        return false;
    }

    internal static bool TryDecode(byte[] bits, int bitLen, out uint hi, out ulong lo)
    {
        hi = 0;
        lo = 0;
        if (bitLen < 64)
            return false;

        var size = (nuint)bitLen;
        return Decode(bits, ref size, out hi, out lo) > 0;
    }

    private static int Decode(byte[] bits, ref nuint size, out uint hi, out ulong lo)
    {
        hi = 0;
        lo = 0;

        if (size < 64)
            return -2;

        var startIdx = (nuint)0;
        Span<byte> preamble = [0, 1, 1, 1, 1, 1, 1, 1, 1, 1];
        if (!PreambleSearch(bits, preamble, ref size, ref startIdx))
            return -4;

        var adjust = size < 128;
        var sidx = startIdx + (nuint)preamble.Length;
        if (adjust)
            sidx--;

        size = RemoveEm410xParity(bits, sidx, size, out var validShort, out var validShortExtended, out var validLong);
        if (size == 0)
            return -6;

        if (validShort)
        {
            hi = 0;
            lo = ((ulong)ByteBitsToByte(bits, 8) << 32) | ByteBitsToByte(bits, 32, 8);
            return 1;
        }

        if (validShortExtended || validLong)
        {
            hi = (uint)ByteBitsToByte(bits, 24);
            lo = ((ulong)ByteBitsToByte(bits, 32, 24) << 32) | ByteBitsToByte(bits, 32, 24 + 32);
            return validShortExtended ? 4 : 2;
        }

        return -6;
    }

    private static bool PreambleSearch(byte[] bits, ReadOnlySpan<byte> preamble, ref nuint size, ref nuint startIdx)
    {
        if (size <= (nuint)preamble.Length)
            return false;

        for (nuint idx = 0; idx < size - (nuint)preamble.Length; idx++)
        {
            if (bits.AsSpan((int)idx, preamble.Length).SequenceEqual(preamble))
            {
                startIdx = idx;
                return true;
            }
        }

        return false;
    }

    private static nuint RemoveEm410xParity(
        byte[] bits,
        nuint startIdx,
        nuint size,
        out bool validShort,
        out bool validShortExtended,
        out bool validLong)
    {
        validShort = false;
        validShortExtended = false;
        validLong = false;

        var blen = size switch
        {
            128 => 110,
            80 => 70,
            _ => 55,
        };

        var parityCol = new uint[4];
        nuint bitCnt = 0;
        var parityWd = 0u;
        var validColParity = false;
        var validRowParity = true;
        var validRowParitySkipColP = true;

        for (var word = 0; word < blen; word += 5)
        {
            for (var bit = 0; bit < 5; bit++)
            {
                if (word + bit >= blen)
                    break;

                var sample = bits[(int)(startIdx + (nuint)(word + bit))];
                parityWd = (parityWd << 1) | sample;

                if (word <= 50 && bit < 4)
                    parityCol[bit] = (parityCol[bit] << 1) | sample;

                bits[bitCnt++] = sample;
            }

            if (word + 5 > blen)
                break;

            bitCnt--;
            validRowParity &= ParityTest(parityWd, 5, 0);

            if (word == 50)
            {
                validColParity = ParityTest(parityCol[0], 11, 0);
                validColParity &= ParityTest(parityCol[1], 11, 0);
                validColParity &= ParityTest(parityCol[2], 11, 0);
                validColParity &= ParityTest(parityCol[3], 11, 0);
            }
            else
            {
                validRowParitySkipColP &= ParityTest(parityWd, 5, 0);
            }

            parityWd = 0;
        }

        if (blen != 128 && validRowParitySkipColP && validColParity)
            validShort = true;

        if (blen == 128 && validRowParity)
            validLong = true;

        if (blen == 128 && validRowParitySkipColP && validColParity)
            validShortExtended = true;

        return validShort || validShortExtended || validLong ? bitCnt : 0;
    }

    private static bool ParityTest(uint bits, byte bitLen, byte parityType) =>
        OddParity32(bits & Mask(bitLen)) ^ (parityType != 0);

    private static uint Mask(byte bitLen) => bitLen >= 32 ? 0xFFFFFFFFu : (1u << bitLen) - 1;

    private static bool OddParity32(uint value)
    {
        value ^= value >> 16;
        value ^= value >> 8;
        value ^= value >> 4;
        value ^= value >> 2;
        value ^= value >> 1;
        return (value & 1) != 0;
    }

    internal static byte[] BuildSyntheticDemodBits(ulong cardId)
    {
        var bits = new List<byte> { 0, 1, 1, 1, 1, 1, 1, 1, 1, 1 };
        var columnParity = new byte[4];

        for (var byteIdx = 0; byteIdx < 5; byteIdx++)
        {
            var uidByte = (byte)((cardId >> (32 - (byteIdx * 8))) & 0xFF);
            for (var half = 0; half < 2; half++)
            {
                var rowParity = 0;
                for (var k = 0; k < 4; k++)
                {
                    var bit = (byte)((uidByte >> (7 - (half * 4 + k))) & 1);
                    bits.Add(bit);
                    rowParity ^= bit;
                    columnParity[k] ^= bit;
                }

                bits.Add((byte)rowParity);
            }
        }

        bits.AddRange(columnParity);
        bits.Add(0);
        return bits.ToArray();
    }

    private static ulong ByteBitsToByte(byte[] bits, int numBits, int offset = 0)
    {
        ulong num = 0;
        for (var i = 0; i < numBits; i++)
            num = (num << 1) | bits[offset + i];
        return num;
    }
}
