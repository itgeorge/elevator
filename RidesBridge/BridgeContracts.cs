namespace RidesBridge;

public sealed record HealthResponse(string Status, string ApiVersion, string BridgeVersion);
public sealed record BlockReadResponse(int Block, string Value);
public sealed record MercuryMirrorReadResponse(string Version, string Block5, string Block6);
public sealed record MercuryMutationRequest(string? Version, IReadOnlyList<MercuryMutation>? Mutations);
public sealed record MercuryMutation(int Block, string? Expected, string? Desired);
public sealed record MercuryMutationBlockResult(
    int Block,
    string Status,
    string Expected,
    string Desired,
    string? Actual);
public sealed record MercuryRollbackResult(
    int Block,
    string Expected,
    string? Actual,
    bool Succeeded);
public sealed record MercuryMutationResponse(
    string Version,
    string Status,
    IReadOnlyList<MercuryMutationBlockResult> Results,
    string RollbackStatus,
    IReadOnlyList<MercuryRollbackResult> Rollback);
public sealed record BridgeErrorResponse(string Code, string Message);

public enum BridgeHardwareError
{
    Unavailable,
    NoChip,
    Timeout,
    Busy,
    MalformedResponse,
}

public sealed class BridgeHardwareException : Exception
{
    public BridgeHardwareError Error { get; }

    public BridgeHardwareException(BridgeHardwareError error, string message, Exception? inner = null)
        : base(message, inner) => Error = error;

    public static BridgeHardwareException Unavailable(Exception? inner = null) =>
        new(BridgeHardwareError.Unavailable, "Proxmark3 is unavailable.", inner);
}

public interface IBridgePm3Device : IAsyncDisposable
{
    Task StartAsync(CancellationToken ct = default);
    Task<string> ReadPage0Block5Async(CancellationToken ct = default);
    Task<string> ReadPage0Block6Async(CancellationToken ct = default);
    Task<(string Block5Hex, string Block6Hex)> ReadMercuryMirrorAsync(CancellationToken ct = default);
    Task WritePage0Block5Async(string value, CancellationToken ct = default);
    Task WritePage0Block6Async(string value, CancellationToken ct = default);
}
