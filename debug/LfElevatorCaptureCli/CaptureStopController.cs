namespace LfElevatorCaptureCli;

/// <summary>
/// Separates the operator's graceful Enter request from immediate Ctrl-C cancellation.
/// A graceful request never cancels an in-flight PM3 batch.
/// </summary>
public sealed class CaptureStopController : IDisposable
{
    private readonly CancellationTokenSource _immediate = new();
    private int _gracefulRequested;

    public CancellationToken ImmediateToken => _immediate.Token;
    public bool GracefulStopRequested => Volatile.Read(ref _gracefulRequested) != 0;
    public bool ImmediateStopRequested => _immediate.IsCancellationRequested;

    public void RequestGracefulStop() => Interlocked.Exchange(ref _gracefulRequested, 1);
    public void RequestImmediateStop() => _immediate.Cancel();

    public void Dispose() => _immediate.Dispose();
}
