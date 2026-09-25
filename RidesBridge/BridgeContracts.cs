namespace RidesBridge;

public sealed record HealthResponse(string Status, string ApiVersion, string BridgeVersion);
public sealed record PairStatusResponse(string Version, bool Paired);
/// <summary>Unauthenticated request used to prove a paired bearer at a Bonjour candidate.</summary>
public sealed record PairProofRequest(string? Locator, string? Nonce, string? Url);
/// <summary>Proof response; it contains no bearer or verifier material.</summary>
/// <remarks>BridgeId is a public identifier and is not trusted without the MAC proof.</remarks>
public sealed record PairProofResponse(string BridgeId, string ApiVersion, string Nonce, string Proof);
public sealed record BlockReadResponse(int Block, string Value);
public sealed record Page0MirrorReadResponse(string Version, string Block5, string Block6);
public sealed record Page0ScanResponse(string Version, string Block4, string Block5, string Block6, int SignalMillivolts);
public sealed record Page0BlockReadResult(int Block, string Value);
public sealed record Page0MissingBlocksResponse(string Version, IReadOnlyList<Page0BlockReadResult> Blocks);
public sealed record Page0Blocks1To6Response(string Version, IReadOnlyList<Page0BlockReadResult> Blocks);
public sealed record Page0ScanReadResult(string Block4Hex, string Block5Hex, string Block6Hex, int SignalMillivolts);
public sealed record Page0MutationRequest(string? Version, IReadOnlyList<Page0Mutation>? Mutations);
public sealed record Page0Mutation(int Block, string? Expected, string? Desired);
public sealed record Page0MutationBlockResult(
    int Block,
    string Status,
    string Expected,
    string Desired,
    string? Actual);
public sealed record Page0RollbackResult(
    int Block,
    string Expected,
    string? Actual,
    bool Succeeded);
public sealed record Page0MutationResponse(
    string Version,
    string Status,
    IReadOnlyList<Page0MutationBlockResult> Results,
    string RollbackStatus,
    IReadOnlyList<Page0RollbackResult> Rollback);
public sealed record BridgeErrorResponse(string Code, string Message);

public enum BridgeHardwareError
{
    Unavailable,
    NoChip,
    Timeout,
    Busy,
    MalformedResponse,
    TuneFailed,
    ReadFailed,
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
    Task<(string Block5Hex, string Block6Hex)> ReadPage0MirrorAsync(CancellationToken ct = default);
    Task WritePage0Block5Async(string value, CancellationToken ct = default);
    Task WritePage0Block6Async(string value, CancellationToken ct = default);
    Task<string> ReadPage0Block1To6Async(int block, CancellationToken ct = default);
    Task WritePage0Block1To6Async(int block, string value, CancellationToken ct = default);
    Task<IReadOnlyList<Page0BlockReadResult>> ReadPage0Blocks1To6Async(CancellationToken ct = default);
    Task<Page0ScanReadResult> ScanPage0Async(CancellationToken ct = default);
    Task<IReadOnlyList<Page0BlockReadResult>> ReadPage0MissingBlocksAsync(CancellationToken ct = default);
}
