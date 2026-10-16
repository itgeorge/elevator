namespace RidesBridge;

/// <summary>A serialized, request-scoped page-0 mutation session; never shared between requests.</summary>
public interface IPage0MutationSession : IAsyncDisposable
{
    Task<string> ReadBlockAsync(int block, CancellationToken ct = default);

    /// <summary>Returns the read-back-verified actual value, not merely a write acknowledgement.</summary>
    Task<string> WriteVerifiedBlockAsync(int block, string value, CancellationToken ct = default);
}

/// <summary>Optional optimized transport path. Opening must freshly detect the current token.</summary>
public interface IPage0MutationSessionProvider
{
    Task<IPage0MutationSession> OpenMutationSessionAsync(CancellationToken ct = default);
}

/// <summary>Compatibility path for devices that do not guarantee native verified writes.</summary>
internal sealed class ReadbackPage0MutationSession(IBridgePm3Device device) : IPage0MutationSession
{
    public Task<string> ReadBlockAsync(int block, CancellationToken ct = default) =>
        device.ReadPage0Block1To6Async(block, ct);

    public async Task<string> WriteVerifiedBlockAsync(int block, string value, CancellationToken ct = default)
    {
        await device.WritePage0Block1To6Async(block, value, ct).ConfigureAwait(false);
        return await device.ReadPage0Block1To6Async(block, ct).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
