using Pm3UsbApi.Native.Demod;

namespace Pm3UsbApi.Native.T55;

internal static class Pm3LfChipTypeScanner
{
    public static bool TryDetectUnsupportedChip(
        ReadOnlySpan<byte> samples,
        Pm3SignalProperties signal,
        out Pm3UnsupportedChipTypeInfo info)
    {
        info = default;
        if (Pm3LfEm410x.TryDetectId(samples, signal, out var cardId, out var clock))
        {
            info = new Pm3UnsupportedChipTypeInfo(Pm3LfChipFamily.Em410x, cardId, clock);
            return true;
        }

        return false;
    }
}

internal readonly record struct Pm3UnsupportedChipTypeInfo(
    Pm3LfChipFamily ChipFamily,
    ulong CardId,
    int Clock);
