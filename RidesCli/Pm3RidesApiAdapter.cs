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

    public async Task<string> ReadPage0BlockAsync(uint block, CancellationToken ct = default) =>
        await _pm3.ReadPage0BlockAsync(block, ct).ConfigureAwait(false);

    public async Task WritePage0BlockAsync(uint block, T55Block data, CancellationToken ct = default) =>
        await _pm3.WritePage0BlockAsync(block, data, ct).ConfigureAwait(false);

    public async Task<string> DumpAsync(CancellationToken ct = default) =>
        await _pm3.DumpAsync(ct).ConfigureAwait(false);

    public async Task<bool> WriteRideMirrorBlocksAsync(T55Block data, CancellationToken ct = default) =>
        await _pm3.WriteRideMirrorBlocksAsync(data, ct).ConfigureAwait(false);

    public async Task<bool> WriteAndVerifyPage0BlocksAsync(
        IReadOnlyList<T55Block> blocks,
        int firstBlock,
        int lastBlock,
        CancellationToken ct = default) =>
        await _pm3.WriteAndVerifyPage0BlocksAsync(blocks, firstBlock, lastBlock, ct).ConfigureAwait(false);

    public async Task<uint> GetSignalStrengthMvAsync(CancellationToken ct = default)
    {
        await _pm3.StartLfTuneAsync(ct).ConfigureAwait(false);
        return await _pm3.GetLfTuneLastMilliVoltsAsync(ct).ConfigureAwait(false);
    }

    public Task<string> RunLfTuneProbeAsync(
        string label,
        int? sampleCount = null,
        TimeSpan? timeout = null,
        string? outputDirectory = null,
        CancellationToken ct = default) =>
        _pm3.RunLfTuneProbeAsync(label, sampleCount, timeout, outputDirectory, ct);
}
