using Pm3UsbApi.Native.Demod;

namespace Pm3UsbApi.Native.T55;

/// <summary>
/// Scans demodulated LF bits for a T55 config block that uses a non-ASK modulation.
/// </summary>
internal static class Pm3T55ModulationScanner
{
    public static bool TryDetectUnsupportedModulation(
        ReadOnlySpan<byte> demodBits,
        int clk,
        out Pm3T55UnsupportedModulationInfo info)
    {
        info = default;
        if (demodBits.Length < 64)
            return false;

        var clocksToTry = clk > 0 ? new[] { clk } : Pm3BitUtils.BitRateClocks;

        foreach (var tryClk in clocksToTry)
        {
            if (!Pm3BitUtils.TryFindPlausibleConfig(demodBits, tryClk, out _, out _, out var modRead, out var block0))
                continue;

            if (block0 == 0)
                continue;

            if (modRead == Pm3BitUtils.DemodAsk)
                continue;

            if (!Pm3T55ModulationNames.IsRecognizedNonAsk(modRead))
                continue;

            info = new Pm3T55UnsupportedModulationInfo(modRead, block0, modRead);
            return true;
        }

        return false;
    }
}

internal readonly record struct Pm3T55UnsupportedModulationInfo(
    byte Modulation,
    uint Block0,
    byte ModRead);
