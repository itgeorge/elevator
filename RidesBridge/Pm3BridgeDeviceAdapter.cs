using Pm3UsbApi;

namespace RidesBridge;

/// <summary>
/// Production bridge adapter. It intentionally exposes only the authenticated bridge's
/// block-5 read plus Mercury mirror reads/writes, and talks to Pm3UsbApi directly; no shell,
/// CLI, or arbitrary command surface is used.
/// </summary>
internal interface IBridgePm3Session : IAsyncDisposable
{
    Task<bool> IsConnectedAsync(CancellationToken ct = default);
    Task ConnectAsync(CancellationToken ct = default);
    void InvalidateT55DetectCache();
    Task EnsureT55SessionActiveAsync(CancellationToken ct = default);
    Task<string> ReadPage0BlockAsync(uint block, CancellationToken ct = default);
    Task WritePage0BlockAsync(uint block, Tokens.T55Block data, CancellationToken ct = default);
}

public sealed class Pm3BridgeDeviceAdapter : IBridgePm3Device
{
    private readonly BridgeOptions _bridgeOptions;
    private readonly Func<BridgeOptions, IBridgePm3Session> _sessionFactory;
    // This lock also serializes disposal with a read. It is deliberately retained after
    // disposal so a late caller gets a stable unavailable error rather than ObjectDisposedException
    // from the synchronization primitive itself.
    private readonly SemaphoreSlim _operationLock = new(1, 1);
    private IBridgePm3Session? _session;
    private int _disposed;

    public Pm3BridgeDeviceAdapter(BridgeOptions bridgeOptions)
        : this(bridgeOptions, options => new Pm3BridgePm3Session(options))
    {
    }

    internal Pm3BridgeDeviceAdapter(
        BridgeOptions bridgeOptions,
        Func<BridgeOptions, IBridgePm3Session> sessionFactory)
    {
        _bridgeOptions = bridgeOptions.Validate();
        _sessionFactory = sessionFactory ?? throw new ArgumentNullException(nameof(sessionFactory));
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        await _operationLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public Task<string> ReadPage0Block5Async(CancellationToken ct = default) => ReadPage0BlockAsync(5, ct);

    public Task<string> ReadPage0Block6Async(CancellationToken ct = default) => ReadPage0BlockAsync(6, ct);

    public async Task<(string Block5Hex, string Block6Hex)> ReadMercuryMirrorAsync(CancellationToken ct = default)
    {
        await _operationLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            await EnsureConnectedAsync(ct).ConfigureAwait(false);
            var session = _session ?? throw BridgeHardwareException.Unavailable();
            session.InvalidateT55DetectCache();
            await session.EnsureT55SessionActiveAsync(ct).ConfigureAwait(false);
            var block5 = await ReadAndValidateAsync(session, 5, ct).ConfigureAwait(false);
            var block6 = await ReadAndValidateAsync(session, 6, ct).ConfigureAwait(false);
            return (block5, block6);
        }
        catch (BridgeHardwareException ex) when (ex.Error == BridgeHardwareError.MalformedResponse)
        {
            await DiscardSessionAfterFailureAsync().ConfigureAwait(false);
            throw;
        }
        catch (BridgeHardwareException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw await MapHardwareExceptionAsync(ex).ConfigureAwait(false);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public Task WritePage0Block5Async(string value, CancellationToken ct = default) => WritePage0BlockAsync(5, value, ct);

    public Task WritePage0Block6Async(string value, CancellationToken ct = default) => WritePage0BlockAsync(6, value, ct);

    private async Task<string> ReadPage0BlockAsync(uint block, CancellationToken ct)
    {
        await _operationLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            await EnsureConnectedAsync(ct).ConfigureAwait(false);
            var session = _session ?? throw BridgeHardwareException.Unavailable();
            // A token can be swapped between requests while the PM3 session remains connected.
            session.InvalidateT55DetectCache();
            await session.EnsureT55SessionActiveAsync(ct).ConfigureAwait(false);
            return await ReadAndValidateAsync(session, block, ct).ConfigureAwait(false);
        }
        catch (BridgeHardwareException ex) when (ex.Error == BridgeHardwareError.MalformedResponse)
        {
            await DiscardSessionAfterFailureAsync().ConfigureAwait(false);
            throw;
        }
        catch (BridgeHardwareException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw await MapHardwareExceptionAsync(ex).ConfigureAwait(false);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    private async Task WritePage0BlockAsync(uint block, string value, CancellationToken ct)
    {
        if (block is not 5 and not 6 || !IsBlockHex(value))
            throw new ArgumentException("Mercury writes require an exact 32-bit value and target block 5 or 6.", nameof(value));

        await _operationLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            await EnsureConnectedAsync(ct).ConfigureAwait(false);
            var session = _session ?? throw BridgeHardwareException.Unavailable();
            session.InvalidateT55DetectCache();
            await session.EnsureT55SessionActiveAsync(ct).ConfigureAwait(false);
            await session.WritePage0BlockAsync(block, Tokens.T55Block.FromHex(value), ct).ConfigureAwait(false);
        }
        catch (BridgeHardwareException ex) when (ex.Error == BridgeHardwareError.MalformedResponse)
        {
            await DiscardSessionAfterFailureAsync().ConfigureAwait(false);
            throw;
        }
        catch (BridgeHardwareException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw await MapHardwareExceptionAsync(ex).ConfigureAwait(false);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    private static async Task<string> ReadAndValidateAsync(IBridgePm3Session session, uint block, CancellationToken ct)
    {
        var value = await session.ReadPage0BlockAsync(block, ct).ConfigureAwait(false);
        if (!IsBlockHex(value))
            throw new BridgeHardwareException(BridgeHardwareError.MalformedResponse, "PM3 returned a malformed block response.");
        return value.ToUpperInvariant();
    }

    private async Task<BridgeHardwareException> MapHardwareExceptionAsync(Exception ex)
    {
        switch (ex)
        {
            case Pm3TimeoutException:
            case TimeoutException:
                await DiscardSessionAfterFailureAsync().ConfigureAwait(false);
                return new BridgeHardwareException(BridgeHardwareError.Timeout, "Proxmark3 operation timed out.", ex);
            case OperationCanceledException:
                await DiscardSessionAfterFailureAsync().ConfigureAwait(false);
                throw ex;
            case Pm3UnsupportedChipTypeException:
                return new BridgeHardwareException(BridgeHardwareError.NoChip, "No supported T55xx chip is present.", ex);
            case Pm3CommandException command when command.Message.Contains("No T55", StringComparison.OrdinalIgnoreCase)
                                                  || command.Message.Contains("chip", StringComparison.OrdinalIgnoreCase):
                return new BridgeHardwareException(BridgeHardwareError.NoChip, "No T55xx chip is present.", ex);
            case FormatException:
                await DiscardSessionAfterFailureAsync().ConfigureAwait(false);
                return new BridgeHardwareException(BridgeHardwareError.MalformedResponse, "PM3 returned a malformed block response.", ex);
            case Pm3Exception:
            case IOException:
            case UnauthorizedAccessException:
            case ObjectDisposedException:
            case InvalidOperationException:
                await DiscardSessionAfterFailureAsync().ConfigureAwait(false);
                return new BridgeHardwareException(BridgeHardwareError.Unavailable, "Proxmark3 is unavailable.", ex);
            default:
                throw ex;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _operationLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            var session = Interlocked.Exchange(ref _session, null);
            if (session is not null)
                await session.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            _operationLock.Release();
        }
        GC.SuppressFinalize(this);
    }

    private async Task EnsureConnectedAsync(CancellationToken ct)
    {
        ThrowIfDisposed();
        if (_session is not null && await _session.IsConnectedAsync(ct).ConfigureAwait(false))
            return;

        ThrowIfDisposed();
        _session ??= _sessionFactory(_bridgeOptions);
        if (!await _session.IsConnectedAsync(ct).ConfigureAwait(false))
            await _session.ConnectAsync(ct).ConfigureAwait(false);
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
            throw BridgeHardwareException.Unavailable();
    }

    private async Task DiscardSessionAfterFailureAsync()
    {
        var session = Interlocked.Exchange(ref _session, null);
        if (session is null)
            return;

        try
        {
            // The operation has completed or cooperatively observed cancellation by this point.
            // Dispose while still holding the adapter lock, never concurrently with transport use.
            await session.DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
            // Preserve the original cancellation/timeout; a failed cleanup must not change its API result.
        }
    }

    private static bool IsBlockHex(string? value) => value is not null && value.Length == 8
        && value.All(Uri.IsHexDigit);

    private sealed class Pm3BridgePm3Session : IBridgePm3Session
    {
        private readonly Pm3 _pm3;

        public Pm3BridgePm3Session(BridgeOptions options)
        {
            _pm3 = new Pm3(new Pm3Options
            {
                ExecutorKind = Pm3ExecutorKind.Native,
                DevicePort = options.Pm3Port,
                AutoConnect = options.Pm3AutoDiscover,
                Pm3ClientPath = options.Pm3ClientPath,
                ConnectTimeout = TimeSpan.FromSeconds(10),
                DefaultCommandTimeout = TimeSpan.FromSeconds(15),
                EnableTranscriptLogging = false,
            });
        }

        public Task<bool> IsConnectedAsync(CancellationToken ct = default) => _pm3.IsConnectedAsync(ct);

        public Task ConnectAsync(CancellationToken ct = default) => _pm3.ConnectAsync(ct);

        public void InvalidateT55DetectCache() => _pm3.InvalidateT55DetectCache();

        public Task EnsureT55SessionActiveAsync(CancellationToken ct = default) => _pm3.EnsureT55SessionActiveAsync(ct);

        public Task<string> ReadPage0BlockAsync(uint block, CancellationToken ct = default) => _pm3.ReadPage0BlockAsync(block, ct);

        public Task WritePage0BlockAsync(uint block, Tokens.T55Block data, CancellationToken ct = default) => _pm3.WritePage0BlockAsync(block, data, ct);

        public ValueTask DisposeAsync() => _pm3.DisposeAsync();
    }
}
