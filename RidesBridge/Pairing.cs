using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RidesBridge;

public sealed record PairingCode(string Value, DateTimeOffset ExpiresAt);
public sealed record PairResponse(string AccessToken, string TokenType = "Bearer");
public sealed record PairRequest(string? Pin);

public sealed class PairingCodeService
{
    public const int DefaultMaxWrongAttempts = 10;

    private readonly TimeProvider _clock;
    private readonly TimeSpan _lifetime;
    private readonly int _maxWrongAttempts;
    private readonly object _sync = new();
    private string? _code;
    private DateTimeOffset _expiresAt;
    private int _wrongAttempts;

    public PairingCodeService(
        TimeSpan lifetime,
        TimeProvider? clock = null,
        int maxWrongAttempts = DefaultMaxWrongAttempts)
    {
        if (lifetime <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(lifetime));
        if (maxWrongAttempts <= 0) throw new ArgumentOutOfRangeException(nameof(maxWrongAttempts));
        _lifetime = lifetime;
        _clock = clock ?? TimeProvider.System;
        _maxWrongAttempts = maxWrongAttempts;
        IssueCode();
    }

    /// <summary>Issues a new one-time code, replacing any code that is currently active.</summary>
    public PairingCode IssueCode()
    {
        lock (_sync)
        {
            _code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
            _expiresAt = _clock.GetUtcNow().Add(_lifetime);
            _wrongAttempts = 0;
            return new PairingCode(_code, _expiresAt);
        }
    }

    /// <summary>
    /// Returns the active operator code for terminal display, or null after expiry/exhaustion/use.
    /// This is intentionally separate from request handling so it is not logged or serialized.
    /// </summary>
    public PairingCode? GetActiveCode()
    {
        lock (_sync)
        {
            if (!IsActiveLocked())
            {
                InvalidateLocked();
                return null;
            }
            return new PairingCode(_code!, _expiresAt);
        }
    }

    public bool TryRedeem(string? candidate)
    {
        lock (_sync)
        {
            if (!IsActiveLocked())
            {
                InvalidateLocked();
                return false;
            }

            if (candidate is null || candidate.Length != 6 || candidate.Any(c => c is < '0' or > '9')
                || !CryptographicOperations.FixedTimeEquals(
                    Encoding.ASCII.GetBytes(_code!), Encoding.ASCII.GetBytes(candidate)))
            {
                _wrongAttempts++;
                if (_wrongAttempts >= _maxWrongAttempts)
                    InvalidateLocked();
                return false;
            }

            InvalidateLocked();
            return true;
        }
    }

    public bool IsActive
    {
        get
        {
            lock (_sync)
            {
                var active = IsActiveLocked();
                if (!active) InvalidateLocked();
                return active;
            }
        }
    }

    private bool IsActiveLocked() => _code is not null && _clock.GetUtcNow() < _expiresAt;

    private void InvalidateLocked()
    {
        _code = null;
        _expiresAt = default;
        _wrongAttempts = 0;
    }
}

public sealed record PairedClientRecord(string Verifier, DateTimeOffset CreatedAt, bool Revoked);

public interface IPairedClientStore
{
    Task AddAsync(string token, CancellationToken ct = default);
    bool IsValid(string token);
    Task<bool> RevokeAsync(string token, CancellationToken ct = default);
}

public sealed class FilePairedClientStore : IPairedClientStore
{
    private readonly string _path;
    private readonly object _sync = new();
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private List<PairedClientRecord> _records;

    public FilePairedClientStore(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A paired-client path is required.", nameof(path));
        _path = Path.GetFullPath(path);
        _records = Load();
    }

    public async Task AddAsync(string token, CancellationToken ct = default)
    {
        var record = new PairedClientRecord(HashToken(token), DateTimeOffset.UtcNow, false);
        lock (_sync)
        {
            _records.RemoveAll(r => r.Verifier == record.Verifier);
            _records.Add(record);
            PersistLocked();
        }
        await Task.CompletedTask.WaitAsync(ct).ConfigureAwait(false);
    }

    public bool IsValid(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return false;
        var verifier = HashToken(token);
        lock (_sync)
            return _records.Any(r => !r.Revoked && FixedEquals(r.Verifier, verifier));
    }

    public async Task<bool> RevokeAsync(string token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token)) return false;
        var verifier = HashToken(token);
        bool changed;
        lock (_sync)
        {
            changed = false;
            for (var i = 0; i < _records.Count; i++)
            {
                if (FixedEquals(_records[i].Verifier, verifier) && !_records[i].Revoked)
                {
                    _records[i] = _records[i] with { Revoked = true };
                    changed = true;
                }
            }
            if (changed) PersistLocked();
        }
        await Task.CompletedTask.WaitAsync(ct).ConfigureAwait(false);
        return changed;
    }

    internal IReadOnlyList<PairedClientRecord> RecordsForTesting
    {
        get { lock (_sync) return _records.ToArray(); }
    }

    private List<PairedClientRecord> Load()
    {
        if (!File.Exists(_path)) return [];
        try
        {
            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<List<PairedClientRecord>>(json, _jsonOptions) ?? [];
        }
        catch (JsonException ex)
        {
            throw new BridgeConfigurationException($"Paired-client store is not valid JSON: {ex.Message}");
        }
    }

    private void PersistLocked()
    {
        var directory = Path.GetDirectoryName(_path);
        if (string.IsNullOrEmpty(directory))
            throw new BridgeConfigurationException("Paired-client store path has no directory.");
        Directory.CreateDirectory(directory);
        var tempPath = _path + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(_records, _jsonOptions), Encoding.UTF8);
        File.Move(tempPath, _path, true);
    }

    internal static string HashToken(string token)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(hash);
    }

    private static bool FixedEquals(string left, string right) =>
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(left), Encoding.ASCII.GetBytes(right));
}

public sealed class BridgeOperationGate
{
    public static readonly TimeSpan DefaultWaitTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MaximumWaitTimeout = TimeSpan.FromSeconds(30);

    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly TimeSpan _defaultWaitTimeout;

    public BridgeOperationGate(TimeSpan? defaultWaitTimeout = null)
    {
        _defaultWaitTimeout = ValidateWaitTimeout(defaultWaitTimeout ?? DefaultWaitTimeout, nameof(defaultWaitTimeout));
    }

    public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default) =>
        ExecuteAsync(operation, ct, _defaultWaitTimeout, operationTimeout: null);

    /// <summary>
    /// Waits using the request token, but once the operation starts only the server-owned
    /// deadline is used. This is required for mutations: a disconnected client must not
    /// cancel verification or rollback.
    /// </summary>
    public async Task<T> ExecuteDetachedAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken requestCt,
        TimeSpan? waitTimeout,
        TimeSpan operationTimeout)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (operationTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(operationTimeout));
        if (waitTimeout.HasValue)
            ValidateWaitTimeout(waitTimeout.Value, nameof(waitTimeout));

        using var waitCts = waitTimeout is null ? null : CancellationTokenSource.CreateLinkedTokenSource(requestCt);
        if (waitCts is not null)
            waitCts.CancelAfter(waitTimeout.GetValueOrDefault());
        try
        {
            await _semaphore.WaitAsync(waitCts?.Token ?? requestCt).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!requestCt.IsCancellationRequested && waitCts?.IsCancellationRequested == true)
        {
            throw new BridgeHardwareException(BridgeHardwareError.Busy, "The bridge is busy with another hardware operation.");
        }

        using var operationCts = new CancellationTokenSource(operationTimeout);
        try
        {
            return await operation(operationCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (operationCts.IsCancellationRequested && !requestCt.IsCancellationRequested)
        {
            throw HardwareTimeout();
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// Waits for the gate, then runs the operation with an independent execution deadline.
    /// The wait timeout is measured from this call; operationTimeout starts only after the gate is acquired.
    /// </summary>
    public async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken ct,
        TimeSpan? waitTimeout,
        TimeSpan? operationTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (operationTimeout.HasValue && operationTimeout.Value <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(operationTimeout));
        if (waitTimeout.HasValue)
            ValidateWaitTimeout(waitTimeout.Value, nameof(waitTimeout));

        using var waitCts = waitTimeout is null ? null : CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (waitCts is not null && waitTimeout.HasValue)
            waitCts.CancelAfter(waitTimeout.GetValueOrDefault());
        try
        {
            await _semaphore.WaitAsync(waitCts?.Token ?? ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && waitCts?.IsCancellationRequested == true)
        {
            throw new BridgeHardwareException(BridgeHardwareError.Busy, "The bridge is busy with another hardware operation.");
        }

        using var operationCts = operationTimeout is null
            ? null
            : CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (operationCts is not null && operationTimeout.HasValue)
            operationCts.CancelAfter(operationTimeout.GetValueOrDefault());

        try
        {
            var result = await operation(operationCts?.Token ?? ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            if (operationCts?.IsCancellationRequested == true)
                throw HardwareTimeout();
            return result;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && operationCts?.IsCancellationRequested == true)
        {
            throw HardwareTimeout();
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private static BridgeHardwareException HardwareTimeout() =>
        new(BridgeHardwareError.Timeout, "Proxmark3 operation timed out.");

    private static TimeSpan ValidateWaitTimeout(TimeSpan value, string parameterName)
    {
        if (value <= TimeSpan.Zero || value >= MaximumWaitTimeout)
            throw new ArgumentOutOfRangeException(parameterName, "The gate wait timeout must be greater than zero and less than 30 seconds.");
        return value;
    }
}
