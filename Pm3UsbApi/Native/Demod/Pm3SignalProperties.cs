namespace Pm3UsbApi.Native.Demod;

/// <summary>
/// LF signal statistics used by demodulators (port of proxmark3 signal_t).
/// </summary>
internal sealed class Pm3SignalProperties
{
    public const int NoiseAmplitudeThreshold = 8;
    public const int MinSamples = 100;
    public const int IgnoreFirstSamples = 10;

    public int Low { get; private set; } = 255;
    public int High { get; private set; } = -255;
    public int Mean { get; private set; }
    public int Amplitude { get; private set; }
    public bool IsNoise { get; private set; } = true;

    public void Reset()
    {
        Low = 255;
        High = -255;
        Mean = 0;
        Amplitude = 0;
        IsNoise = true;
    }

    public void Compute(ReadOnlySpan<byte> samples)
    {
        Reset();
        if (samples.Length < MinSamples)
            return;

        var offsetSize = samples.Length - IgnoreFirstSamples;
        var tmp = samples[IgnoreFirstSamples..].ToArray();
        Array.Sort(tmp);

        var low10 = (byte)((tmp[(int)(offsetSize * 0.1)] + tmp[(int)((offsetSize - 1) * 0.1)]) / 2);
        var hi90 = (byte)((tmp[(int)(offsetSize * 0.9)] + tmp[(int)((offsetSize - 1) * 0.9)]) / 2);

        uint sum = 0;
        uint cnt = 0;
        for (var i = IgnoreFirstSamples; i < samples.Length; i++)
        {
            if (samples[i] < Low) Low = samples[i];
            if (samples[i] > High) High = samples[i];
            if (samples[i] < low10 || samples[i] > hi90)
                continue;
            sum += samples[i];
            cnt++;
        }

        Mean = cnt > 0 ? (int)(sum / cnt) : 0;
        Amplitude = High - Mean;
        IsNoise = Amplitude < NoiseAmplitudeThreshold;
    }

    public void GetHiLo(out int high, out int low, byte fuzzHi, byte fuzzLo)
    {
        high = High * fuzzHi / 100;
        if (Low < 0)
        {
            low = Low * fuzzLo / 100;
        }
        else
        {
            var range = High - Low;
            low = Low + range * (100 - fuzzLo) / 100;
        }
    }
}
