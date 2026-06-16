namespace Pm3UsbApi.Native.Demod;

/// <summary>
/// Holds graph samples and demodulated bits between T55 operations.
/// </summary>
internal sealed class Pm3GraphState
{
    public const int MaxGraphSamples = 12000;
    public const int MaxDemodBits = 4096;

    public int[] GraphBuffer { get; } = new int[MaxGraphSamples];
    public int GraphLength { get; set; }
    public byte[] DemodBuffer { get; } = new byte[MaxDemodBits];
    public int DemodLength { get; set; }
    public Pm3SignalProperties Signal { get; } = new();

    /// <summary>
    /// Load BigBuf bytes from firmware into signed graph buffer.
    /// Matches proxmark3 getSamplesFromBufEx (cmdlft55xx AcquireData path):
    /// g_GraphBuffer[j] = data[j] - 127.
    /// </summary>
    public void LoadSamples(ReadOnlySpan<byte> raw)
    {
        GraphLength = Math.Min(raw.Length, MaxGraphSamples);
        for (var i = 0; i < GraphLength; i++)
            GraphBuffer[i] = raw[i] - 127;
    }

    /// <summary>
    /// Export samples for demod (getFromGraphBuffer: dest[i] = graph[i] + 128).
    /// </summary>
    public int CopyToByteSamples(Span<byte> dest)
    {
        var len = Math.Min(GraphLength, dest.Length);
        for (var i = 0; i < len; i++)
        {
            var sample = GraphBuffer[i];
            if (sample > 127)
                sample = 127;
            if (sample < -127)
                sample = -127;
            dest[i] = (byte)(sample + 128);
        }

        return len;
    }

    public void SetDemodBuffer(ReadOnlySpan<byte> bits)
    {
        DemodLength = Math.Min(bits.Length, MaxDemodBits);
        bits[..DemodLength].CopyTo(DemodBuffer);
    }
}
