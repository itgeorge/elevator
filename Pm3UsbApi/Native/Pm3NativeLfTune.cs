using Pm3UsbApi.Native.Protocol;

namespace Pm3UsbApi.Native;

internal static class Pm3NativeLfTune
{
    public static uint MeasurePeakMillivolts(
        Func<byte[], Pm3ResponseFrame> send,
        int sampleCount,
        TimeSpan timeout,
        CancellationToken ct = default,
        Func<long>? tickNow = null)
    {
        if (sampleCount < 1)
            throw new ArgumentOutOfRangeException(nameof(sampleCount), "Sample count must be at least 1.");

        var now = tickNow ?? (() => Environment.TickCount64);
        var divisor = Pm3CommandCodes.LfDivisor125;
        var init = new byte[] { 1, divisor };
        var measure = new byte[] { 2, divisor };
        var shutdown = new byte[] { 3, divisor };

        var response = send(init);
        if (response.Status != Pm3CommandCodes.Pm3Success)
            throw new InvalidOperationException("LF tune initialization failed.");

        uint peak = 0;
        var samplesTaken = 0;
        var deadline = now() + (long)timeout.TotalMilliseconds;

        while (samplesTaken < sampleCount && now() < deadline)
        {
            ct.ThrowIfCancellationRequested();
            response = send(measure);
            if (response.Status == Pm3CommandCodes.Pm3EopAborted || response.Data.Length != sizeof(uint))
                break;

            if (response.Status != Pm3CommandCodes.Pm3Success)
                throw new InvalidOperationException("LF tune measurement failed.");

            var volt = BitConverter.ToUInt32(response.Data, 0);
            if (volt > peak)
                peak = volt;

            samplesTaken++;
        }

        try
        {
            send(shutdown);
        }
        catch
        {
            // Best effort shutdown, same as client on abort.
        }

        if (peak == 0)
            throw new InvalidOperationException("LF tune returned no voltage samples.");

        return peak;
    }
}
