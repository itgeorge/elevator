using System.Runtime.ExceptionServices;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace RidesBridge;

public sealed class BridgeLifecycleService : IHostedService
{
    private readonly IBridgePm3Device _device;
    private readonly BridgeOperationGate _operationGate;
    private readonly BridgeOptions? _options;
    private readonly BridgeIdentityService? _identity;
    private readonly IBonjourPublisher _publisher;
    private readonly ILogger<BridgeLifecycleService>? _logger;
    private readonly object _sync = new();
    private Task? _startTask;
    private Task? _stopTask;
    private Task<Exception?>? _cleanupTask;
    private Task<Exception?>? _publisherCleanupTask;
    private bool _stopRequested;

    // Retained for callers that use the lifecycle directly in existing tests and tools.
    public BridgeLifecycleService(IBridgePm3Device device, BridgeOperationGate? operationGate = null)
        : this(device, operationGate ?? new BridgeOperationGate(), null, null, new NoopBonjourPublisher(), null)
    {
    }

    public BridgeLifecycleService(
        IBridgePm3Device device,
        BridgeOperationGate operationGate,
        BridgeOptions? options,
        BridgeIdentityService? identity,
        IBonjourPublisher publisher,
        ILogger<BridgeLifecycleService>? logger = null)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _operationGate = operationGate ?? throw new ArgumentNullException(nameof(operationGate));
        _options = options;
        _identity = identity;
        _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            if (_stopRequested)
                return Task.FromException(new InvalidOperationException("Bridge startup was requested after the bridge was stopped."));
            return _startTask ??= StartCoreAsync(cancellationToken);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        // Shutdown is deliberately server-owned. A host cancellation token must not interrupt
        // the gate or leave a USB session or mDNS service advertised. Set the state before
        // creating the cleanup task so a concurrent StartAsync cannot begin after shutdown.
        lock (_sync)
        {
            _stopRequested = true;
            return _stopTask ??= StopCoreAsync();
        }
    }

    private async Task StartCoreAsync(CancellationToken cancellationToken)
    {
        try
        {
            var advertisements = _options is null || _identity is null
                ? []
                : BonjourAdvertisementFactory.Create(_options, _identity.Id);
            await _device.StartAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await _publisher.StartAsync(advertisements, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception publisherFailure)
            {
                // Bonjour is a convenience path. A multicast-disabled network, firewall,
                // or occupied mDNS socket must not take down the direct-IP HTTP bridge.
                // Cleanup is still mandatory before continuing, because a publisher may
                // have created some advertisers before reporting a partial-start failure.
                cancellationToken.ThrowIfCancellationRequested();
                var cleanupFailure = await CleanupPublisherOnceAsync().ConfigureAwait(false);
                if (cleanupFailure is null)
                {
                    _logger?.LogWarning("Bonjour advertisement is unavailable; direct-IP bridge access remains available.");
                    return;
                }
                if (cleanupFailure is not null)
                    throw new BridgeStartupException(
                        "Bonjour advertisement failed and could not be cleaned up.",
                        new AggregateException("Bonjour advertisement startup and cleanup failed.", publisherFailure, cleanupFailure));
            }
        }
        catch (Exception startupFailure)
        {
            var cleanupFailure = await CleanupOnceAsync().ConfigureAwait(false);
            var failure = cleanupFailure is null
                ? startupFailure
                : new AggregateException("Bridge startup and cleanup failed.", startupFailure, cleanupFailure);
            if (failure is OperationCanceledException && cleanupFailure is null)
                ExceptionDispatchInfo.Capture(failure).Throw();
            throw new BridgeStartupException(
                "RidesBridge startup failed; hardware and Bonjour advertisement were stopped.", failure);
        }
    }

    private async Task StopCoreAsync()
    {
        var startTask = _startTask;
        Exception? startFailure = null;
        if (startTask is not null && !startTask.IsCompleted)
        {
            try { await startTask.ConfigureAwait(false); }
            catch (Exception ex) { startFailure = ex; }
        }

        var cleanupFailure = await CleanupOnceAsync().ConfigureAwait(false);
        if (startFailure is not null && cleanupFailure is not null)
            throw new AggregateException("Bridge startup and shutdown failed.", startFailure, cleanupFailure);
        if (startFailure is not null)
            ExceptionDispatchInfo.Capture(startFailure).Throw();
        if (cleanupFailure is not null)
            ExceptionDispatchInfo.Capture(cleanupFailure).Throw();
    }

    private Task<Exception?> CleanupOnceAsync()
    {
        lock (_sync)
            return _cleanupTask ??= CleanupCoreAsync();
    }

    private Task<Exception?> CleanupPublisherOnceAsync()
    {
        lock (_sync)
            return _publisherCleanupTask ??= CleanupPublisherCoreAsync();
    }

    private async Task<Exception?> CleanupPublisherCoreAsync()
    {
        var failures = new List<Exception>();
        try { await _publisher.StopAsync(CancellationToken.None).ConfigureAwait(false); }
        catch (Exception ex) { failures.Add(ex); }
        try { await _publisher.DisposeAsync().ConfigureAwait(false); }
        catch (Exception ex) { failures.Add(ex); }

        return failures.Count switch
        {
            0 => null,
            1 => failures[0],
            _ => new AggregateException("Bonjour cleanup failed.", failures),
        };
    }

    private async Task<Exception?> CleanupCoreAsync()
    {
        var failures = new List<Exception>();
        var publisherFailure = await CleanupPublisherOnceAsync().ConfigureAwait(false);
        if (publisherFailure is not null)
            failures.Add(publisherFailure);

        try
        {
            await _operationGate.ExecuteAsync(
                async _ =>
                {
                    await _device.DisposeAsync().ConfigureAwait(false);
                    return true;
                },
                CancellationToken.None,
                waitTimeout: null).ConfigureAwait(false);
        }
        catch (Exception ex) { failures.Add(ex); }

        return failures.Count switch
        {
            0 => null,
            1 => failures[0],
            _ => new AggregateException("Bridge cleanup failed.", failures),
        };
    }
}

public sealed class BridgeStartupException : Exception
{
    public BridgeStartupException(string message, Exception innerException) : base(message, innerException) { }
}
