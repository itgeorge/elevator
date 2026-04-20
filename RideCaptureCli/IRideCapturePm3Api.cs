using Tokens;

namespace RideCaptureCli;

public interface IRideCapturePm3Api
{
    Task<bool> TryDetectTokenAsync(CancellationToken ct = default);
    Task<string> ReadPage0BlockAsync(uint block, CancellationToken ct = default);
    Task<string> DumpAsync(CancellationToken ct = default);
    Task<uint> GetSignalStrengthMvAsync(CancellationToken ct = default);
}
