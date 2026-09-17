namespace RidesBridge;

public sealed record HealthResponse(string Status, string ApiVersion, string BridgeVersion);
public sealed record PairStatusResponse(string Version, bool Paired);
/// <summary>Unauthenticated request used to prove a paired bearer at a Bonjour candidate.</summary>
public sealed record PairProofRequest(string? Locator, string? Nonce, string? Url);
/// <summary>Proof response; it contains no bearer or verifier material.</summary>
/// <remarks>BridgeId is a public identifier and is not trusted without the MAC proof.</remarks>
public sealed record PairProofResponse(string BridgeId, string ApiVersion, string Nonce, string Proof);
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
