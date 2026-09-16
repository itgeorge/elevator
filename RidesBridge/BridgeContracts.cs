namespace RidesBridge;

public sealed record HealthResponse(string Status, string ApiVersion, string BridgeVersion);
public sealed record BlockReadResponse(int Block, string Value);
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
}
