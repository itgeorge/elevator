using Pm3UsbApi;

namespace RidesBridge;

/// <summary>
/// Production bridge adapter. It intentionally exposes only the Slice 1 block-5 read and
/// talks to Pm3UsbApi directly; no shell, CLI, or arbitrary command surface is used.
/// </summary>
public sealed class Pm3BridgeDeviceAdapter : IBridgePm3Device
{
    private readonly BridgeOptions _bridgeOptions;
    // This lock also serializes disposal with a read. It is deliberately retained after
    // disposal so a late caller gets a stable unavailable error rather than ObjectDisposedException
    // from the synchronization primitive itself.
    private readonly SemaphoreSlim _operationLock = new(1, 1);
    private Pm3? _pm3;
    private int _disposed;

    public Pm3BridgeDeviceAdapter(BridgeOptions bridgeOptions)
    {
        _bridgeOptions = bridgeOptions.Validate();
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

    public async Task<string> ReadPage0Block5Async(CancellationToken ct = default)
    {
        await _operationLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            await EnsureConnectedAsync(ct).ConfigureAwait(false);
            var pm3 = _pm3 ?? throw BridgeHardwareException.Unavailable();
            await pm3.EnsureT55SessionActiveAsync(ct).ConfigureAwait(false);
            var value = await pm3.ReadPage0BlockAsync(5, ct).ConfigureAwait(false);
            if (!IsBlockHex(value))
                throw new BridgeHardwareException(BridgeHardwareError.MalformedResponse, "PM3 returned a malformed block response.");
            return value.ToUpperInvariant();
        }
        catch (BridgeHardwareException)
        {
            throw;
        }
        catch (Pm3TimeoutException ex)
        {
            throw new BridgeHardwareException(BridgeHardwareError.Timeout, "Proxmark3 operation timed out.", ex);
        }
        catch (Pm3UnsupportedChipTypeException ex)
        {
            throw new BridgeHardwareException(BridgeHardwareError.NoChip, "No supported T55xx chip is present.", ex);
        }
        catch (Pm3CommandException ex) when (ex.Message.Contains("No T55", StringComparison.OrdinalIgnoreCase)
                                              || ex.Message.Contains("chip", StringComparison.OrdinalIgnoreCase))
        {
            throw new BridgeHardwareException(BridgeHardwareError.NoChip, "No T55xx chip is present.", ex);
        }
        catch (Pm3Exception ex)
        {
            throw new BridgeHardwareException(BridgeHardwareError.Unavailable, "Proxmark3 is unavailable.", ex);
        }
        catch (TimeoutException ex)
        {
            throw new BridgeHardwareException(BridgeHardwareError.Timeout, "Proxmark3 operation timed out.", ex);
        }
        catch (FormatException ex)
        {
            throw new BridgeHardwareException(BridgeHardwareError.MalformedResponse, "PM3 returned a malformed block response.", ex);
        }
        catch (IOException ex)
        {
            throw new BridgeHardwareException(BridgeHardwareError.Unavailable, "Proxmark3 is unavailable.", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new BridgeHardwareException(BridgeHardwareError.Unavailable, "Proxmark3 is unavailable.", ex);
        }
        catch (ObjectDisposedException ex)
        {
            throw new BridgeHardwareException(BridgeHardwareError.Unavailable, "Proxmark3 is unavailable.", ex);
        }
        catch (InvalidOperationException ex)
        {
            throw new BridgeHardwareException(BridgeHardwareError.Unavailable, "Proxmark3 is unavailable.", ex);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _operationLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            var pm3 = Interlocked.Exchange(ref _pm3, null);
            if (pm3 is not null)
                await pm3.DisposeAsync().ConfigureAwait(false);
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
        if (_pm3 is not null && await _pm3.IsConnectedAsync(ct).ConfigureAwait(false))
            return;

        ThrowIfDisposed();
        if (_pm3 is null)
        {
            _pm3 = new Pm3(new Pm3Options
            {
                ExecutorKind = Pm3ExecutorKind.Native,
                DevicePort = _bridgeOptions.Pm3Port,
                AutoConnect = _bridgeOptions.Pm3AutoDiscover,
                Pm3ClientPath = _bridgeOptions.Pm3ClientPath,
                ConnectTimeout = TimeSpan.FromSeconds(10),
                DefaultCommandTimeout = TimeSpan.FromSeconds(15),
                EnableTranscriptLogging = false,
            });
        }
        if (!await _pm3.IsConnectedAsync(ct).ConfigureAwait(false))
            await _pm3.ConnectAsync(ct).ConfigureAwait(false);
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
            throw BridgeHardwareException.Unavailable();
    }

    private static bool IsBlockHex(string value) => value.Length == 8
        && value.All(Uri.IsHexDigit);
}
