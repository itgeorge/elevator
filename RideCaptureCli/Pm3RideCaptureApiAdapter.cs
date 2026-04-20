using Pm3UsbApi;

namespace RideCaptureCli;

public sealed class Pm3RideCaptureApiAdapter : IRideCapturePm3Api
{
    private readonly Pm3 _pm3;

    public Pm3RideCaptureApiAdapter(Pm3 pm3)
    {
        _pm3 = pm3 ?? throw new ArgumentNullException(nameof(pm3));
    }

    public async Task<bool> TryDetectTokenAsync(CancellationToken ct = default)
    {
        try
        {
            await _pm3.EnsureT55SessionActiveAsync(ct).ConfigureAwait(false);
            return true;
        }
        catch (Pm3CommandException)
        {
            return false;
        }
    }

    public Task<string> ReadPage0BlockAsync(uint block, CancellationToken ct = default) =>
        _pm3.ReadPage0BlockAsync(block, ct);

    public Task<string> DumpAsync(CancellationToken ct = default) =>
        _pm3.DumpAsync(ct);

    public async Task<uint> GetSignalStrengthMvAsync(CancellationToken ct = default)
    {
        await _pm3.StartLfTuneAsync(ct).ConfigureAwait(false);
        return await _pm3.GetLfTuneLastMilliVoltsAsync(ct).ConfigureAwait(false);
    }
}
