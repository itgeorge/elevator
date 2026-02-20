using Pm3UsbApi;
using Tokens;

namespace RidesCli;

/// <summary>
/// Adapts Pm3 to IRidesPm3Api.
/// </summary>
public sealed class Pm3RidesApiAdapter : IRidesPm3Api
{
    private readonly Pm3 _pm3;

    public Pm3RidesApiAdapter(Pm3 pm3)
    {
        _pm3 = pm3 ?? throw new ArgumentNullException(nameof(pm3));
    }

    public async Task<string> ReadPage0BlockAsync(uint block, CancellationToken ct = default) =>
        await _pm3.ReadPage0BlockAsync(block, ct).ConfigureAwait(false);

    public async Task WritePage0BlockAsync(uint block, T55Block data, CancellationToken ct = default) =>
        await _pm3.WritePage0BlockAsync(block, data, ct).ConfigureAwait(false);

    public async Task<string> DumpAsync(CancellationToken ct = default) =>
        await _pm3.DumpAsync(ct).ConfigureAwait(false);

    public async Task<uint> GetSignalStrengthMvAsync(CancellationToken ct = default)
    {
        await _pm3.StartLfTuneAsync(ct).ConfigureAwait(false);
        return await _pm3.GetLfTuneLastMilliVoltsAsync(ct).ConfigureAwait(false);
    }
}
