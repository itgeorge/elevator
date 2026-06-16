namespace Pm3UsbApi.Native.Demod;

/// <summary>
/// Token-scoped ASK/Manchester demod for T55x7 elevator tags (RF/64, 0x00148040).
/// Ported from proxmark3 common/lfdemod.c (askdemod_ext, cleanAskRawDemod, manrawdecode).
/// </summary>
internal static class Pm3LfDemod
{
    private const int MaxDemodBits = 4096;
    public const int TokenClock = 64;

    private static readonly int[] AskClocks = [8, 16, 32, 40, 50, 64, 100, 128];

    public static int AskDemodExt(
        byte[] bits,
        ref int bitLen,
        ref int clk,
        ref int invert,
        int maxErr,
        byte askType,
        ref bool stCheck,
        Pm3SignalProperties signal,
        Action<string>? trace = null)
    {
        void Step(string msg) => trace?.Invoke(msg);

        Step("enter");
        if (bitLen < 255 || signal.IsNoise)
            return -1;

        var size = Math.Min(bitLen, bits.Length);
        if (size < 320)
            return -1;

        size -= 60;
        Step($"detect-clock size={size}");
        var start = DetectAskClock(bits, size, ref clk, maxErr, signal);
        Step($"detect-clock done clk={clk} start={start}");
        if (clk <= 0 || start < 0)
            return -3;

        invert = invert == 1 ? 1 : 0;
        signal.GetHiLo(out var high, out var low, 75, 75);

        var startIdx = start - (clk / 2);
        int errCnt;

        if (DetectCleanAskWave(bits, size, high, low))
        {
            Step("clean-ask");
            errCnt = CleanAskRawDemod(bits, ref size, clk, invert, high, low, ref startIdx);
            Step($"clean-ask done size={size} err={errCnt}");
            if (askType == 1 && errCnt >= 0)
            {
                Step("man-decode");
                byte alignPos = 0;
                errCnt = ManRawDecode(bits, ref size, 0, ref alignPos);
                Step($"man-decode done size={size} err={errCnt}");
                startIdx += (clk / 2) * alignPos;
            }
        }
        else
        {
            Step("weak-ask");
            errCnt = WeakAskDemod(bits, ref size, clk, invert, high, low, askType, start, ref startIdx);
            Step($"weak-ask done size={size} err={errCnt}");
        }

        if (errCnt < 0 || size < 16 || errCnt > maxErr)
            return -3;

        bitLen = Math.Min(size, MaxDemodBits);
        if (bitLen < size)
            Array.Copy(bits, 0, bits, 0, bitLen);

        _ = stCheck;
        _ = startIdx;
        return errCnt;
    }

    private static int DetectAskClock(byte[] dest, int size, ref int clock, int maxErr, Pm3SignalProperties signal)
    {
        if (size <= 1060 || signal.IsNoise)
            return -2;

        signal.GetHiLo(out var peakHi, out var peakLow, 75, 75);

        if (clock > 0)
        {
            if (DetectCleanAskWave(dest, size, peakHi, peakLow))
            {
                var idx = FindBestStart(dest, size, clock, peakHi, peakLow);
                if (idx >= 0)
                    return idx;
            }

            return FindWeakAskStart(dest, size, clock, maxErr, peakHi, peakLow, loopCap: 256);
        }

        if (DetectCleanAskWave(dest, size, peakHi, peakLow))
        {
            foreach (var candidate in new[] { 64, 32, 40, 50 })
            {
                var idx = FindBestStart(dest, size, candidate, peakHi, peakLow);
                if (idx >= 0)
                {
                    clock = candidate;
                    return idx;
                }
            }
        }

        // Fallback: limited weak search (full PM3 scan is very slow on 12k samples).
        var bestErr = int.MaxValue;
        var bestStart = -1;
        var bestClock = TokenClock;
        var weakLoopCap = Math.Min(256, size / 16);

        foreach (var candidate in AskClocks)
        {
            var start = FindWeakAskStart(dest, size, candidate, maxErr, peakHi, peakLow, weakLoopCap);
            if (start < 0)
                continue;

            var err = ScoreWeakAskClock(dest, size, candidate, start, peakHi, peakLow);
            if (err < bestErr)
            {
                bestErr = err;
                bestStart = start;
                bestClock = candidate;
            }
        }

        if (bestStart < 0)
            return -2;

        clock = bestClock;
        return bestStart;
    }

    private static int FindWeakAskStart(byte[] dest, int size, int clock, int maxErr, int peakHi, int peakLow, int loopCap)
    {
        var loopCnt = Math.Min(loopCap, Math.Min(1000, size - clock * 2));
        var tol = clock <= 32 ? 1 : 0;
        if (maxErr == 0 && size > clock * 2 + tol && clock < 128)
            loopCnt = clock * 2;

        var j = 0;
        GetNextHigh(dest, size, peakHi, ref j);
        GetNextLow(dest, size, peakLow, ref j);

        var bestErr = 1000;
        var bestStart = -1;

        for (; j < loopCnt; j++)
        {
            var err = ScoreWeakAskClock(dest, size, clock, j, peakHi, peakLow);
            if (err == 0 && clock < 128)
                return j;

            if (err < bestErr)
            {
                bestErr = err;
                bestStart = j;
            }
        }

        return bestStart;
    }

    private static int ScoreWeakAskClock(byte[] dest, int size, int clock, int start, int peakHi, int peakLow)
    {
        var tol = clock <= 32 ? 1 : 0;
        var loopEnd = ((size - start - tol) / clock) - 1;
        if (loopEnd <= 0)
            return 1000;

        var err = 0;
        for (var i = 0; i < loopEnd; i++)
        {
            var arrLoc = start + (i * clock);
            if (arrLoc >= size)
                break;

            if (dest[arrLoc] >= peakHi || dest[arrLoc] <= peakLow)
                continue;

            if (arrLoc - tol >= 0 && (dest[arrLoc - tol] >= peakHi || dest[arrLoc - tol] <= peakLow))
                continue;

            if (arrLoc + tol < size && (dest[arrLoc + tol] >= peakHi || dest[arrLoc + tol] <= peakLow))
                continue;

            err++;
        }

        return err;
    }

    private static int FindBestStart(ReadOnlySpan<byte> samples, int size, int clk, int high, int low)
    {
        var scanEnd = Math.Min(256, size - clk * 8);
        if (scanEnd <= 0)
            return -1;

        var bestErr = int.MaxValue;
        var bestStart = -1;

        for (var start = 0; start < scanEnd; start++)
        {
            var err = 0;
            var periods = Math.Min(48, (size - start) / clk);
            for (var p = 0; p < periods; p++)
            {
                var sample = samples[start + (p * clk)];
                if (sample > low && sample < high)
                    err++;
            }

            if (err < bestErr)
            {
                bestErr = err;
                bestStart = start;
            }
        }

        return bestStart;
    }

    private static bool DetectCleanAskWave(ReadOnlySpan<byte> dest, int size, int high, int low)
    {
        var loopEnd = Math.Min(1024 + 160, size);
        var allArePeaks = true;
        var cntPeaks = 0;

        for (var i = 160; i < loopEnd; i++)
        {
            if (dest[i] > low && dest[i] < high)
                allArePeaks = false;
            else
            {
                cntPeaks++;
                if (cntPeaks > 200)
                    return true;
            }
        }

        if (!allArePeaks && cntPeaks > 200)
            return true;

        return allArePeaks;
    }

    private static int CleanAskRawDemod(byte[] bits, ref int size, int clk, int invert, int high, int low, ref int startIdx)
    {
        startIdx = 0;
        var bitCnt = 0;
        var smplCnt = 1;
        var errCnt = 0;
        var pos = 0;
        var cl4 = clk / 4;
        var cl2 = clk / 2;
        var waveHigh = true;

        GetNextHigh(bits, size, high, ref pos);

        if (pos > cl2 - cl4 - 1 && pos <= clk + cl4 + 1)
            bits[bitCnt++] = (byte)(invert ^ 1);

        for (var i = pos; i < size && bitCnt < MaxDemodBits; i++)
        {
            if (bits[i] >= high && waveHigh)
                smplCnt++;
            else if (bits[i] <= low && !waveHigh)
                smplCnt++;
            else if ((bits[i] >= high && !waveHigh) || (bits[i] <= low && waveHigh))
            {
                if (smplCnt > clk - cl4 - 1)
                {
                    if (smplCnt > clk + cl4 + 1)
                    {
                        errCnt++;
                        bits[bitCnt++] = 7;
                    }
                    else if (waveHigh)
                    {
                        bits[bitCnt++] = (byte)invert;
                        bits[bitCnt++] = (byte)invert;
                    }
                    else
                    {
                        bits[bitCnt++] = (byte)(invert ^ 1);
                        bits[bitCnt++] = (byte)(invert ^ 1);
                    }

                    if (startIdx == 0)
                        startIdx = i - clk;
                    waveHigh = !waveHigh;
                    smplCnt = 0;
                }
                else if (smplCnt > cl2 - cl4 - 1)
                {
                    if (smplCnt > cl2 + cl4 + 1)
                    {
                        errCnt++;
                        bits[bitCnt++] = 7;
                    }

                    bits[bitCnt++] = (byte)(waveHigh ? invert : (invert ^ 1));
                    if (startIdx == 0)
                        startIdx = i - cl2;
                    waveHigh = !waveHigh;
                    smplCnt = 0;
                }
                else
                    smplCnt++;
            }
            else
                smplCnt++;
        }

        size = bitCnt;
        return errCnt;
    }

    private static int WeakAskDemod(
        byte[] bits,
        ref int size,
        int clk,
        int invert,
        int high,
        int low,
        byte askType,
        int start,
        ref int startIdx)
    {
        var bitnum = 0;
        var lastBit = start - clk;
        var midBit = false;
        var tol = clk <= 32 ? 1 : 0;
        var errCnt = 0;

        for (var i = Math.Max(0, start); i < size && bitnum < MaxDemodBits; i++)
        {
            if (i - lastBit >= clk - tol)
            {
                if (bits[i] >= high)
                    bits[bitnum++] = (byte)invert;
                else if (bits[i] <= low)
                    bits[bitnum++] = (byte)(invert ^ 1);
                else if (i - lastBit >= clk + tol && bitnum > 0)
                {
                    bits[bitnum++] = 7;
                    errCnt++;
                }

                midBit = false;
                lastBit += clk;
            }
            else if (askType == 0 && i - lastBit >= clk / 2 - tol && !midBit)
            {
                if (bits[i] >= high)
                    bits[bitnum++] = (byte)invert;
                else if (bits[i] <= low)
                    bits[bitnum++] = (byte)(invert ^ 1);
                midBit = true;
            }
        }

        size = bitnum;
        _ = startIdx;
        return errCnt;
    }

    internal static int ManRawDecode(byte[] bits, ref int size, byte invert, ref byte alignPos)
    {
        if (size < 16)
            return 0xFFFF;

        alignPos = 0;
        var bestErr = 1000;
        var bestRun = 0;

        for (var k = 0; k < 2; k++)
        {
            var err = 0;
            for (var i = k; i < size - 1; i += 2)
            {
                if (bits[i] == bits[i + 1])
                    err++;
                if (err > 50)
                    break;
            }

            if (bestErr > err)
            {
                bestErr = err;
                bestRun = k;
            }
        }

        alignPos = (byte)bestRun;
        var bitnum = 0;
        for (var i = bestRun; i < size && bitnum < MaxDemodBits; i += 2)
        {
            if (i + 1 >= size)
                break;

            if (bits[i] == 1 && bits[i + 1] == 0)
                bits[bitnum++] = invert;
            else if (bits[i] == 0 && bits[i + 1] == 1)
                bits[bitnum++] = (byte)(invert ^ 1);
            else
                bits[bitnum++] = 7;
        }

        size = bitnum;
        return bestErr;
    }

    private static void GetNextHigh(ReadOnlySpan<byte> samples, int size, int high, ref int i)
    {
        while (i < size && samples[i] < high)
            i++;
    }

    private static void GetNextLow(ReadOnlySpan<byte> samples, int size, int low, ref int i)
    {
        while (i < size && samples[i] > low)
            i++;
    }
}
