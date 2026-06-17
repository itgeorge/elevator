using Pm3UsbApi.Commands;
using Pm3UsbApi.Parsers;

namespace Pm3UsbApi.Session;

/// <summary>
/// Session-level policy for skipping redundant T55 detect commands on the native executor.
/// </summary>
public sealed class Pm3T55DetectCache
{
    public static readonly TimeSpan DefaultTtl = TimeSpan.FromSeconds(30);

    private readonly TimeSpan _ttl;
    private Pm3T55DetectCacheEntry? _entry;

    public Pm3T55DetectCache(TimeSpan? ttl = null)
    {
        _ttl = ttl ?? DefaultTtl;
    }

    public bool ShouldSkipDetect(
        Pm3ExecutorKind executorKind,
        string? port,
        IPm3DeviceCommand followOnCommand,
        DateTime utcNow)
    {
        if (executorKind != Pm3ExecutorKind.Native)
            return false;

        if (!IsCacheableFollowOn(followOnCommand))
            return false;

        if (_entry is null)
            return false;

        if (_entry.ExecutorKind != executorKind)
            return false;

        if (!PortsEqual(_entry.Port, port))
            return false;

        if (utcNow - _entry.DetectedAtUtc >= _ttl)
            return false;

        return true;
    }

    public void RecordDetect(
        Pm3ExecutorKind executorKind,
        string? port,
        DetectResult detect,
        DateTime utcNow)
    {
        if (executorKind != Pm3ExecutorKind.Native)
            return;

        if (!detect.ChipFound || string.IsNullOrWhiteSpace(detect.Block0Hex))
            return;

        _entry = new Pm3T55DetectCacheEntry(
            executorKind,
            NormalizePort(port),
            detect.Block0Hex.ToUpperInvariant(),
            utcNow);
    }

    public void TryRecordFromBatchResult(
        Pm3ExecutorKind executorKind,
        string? port,
        IReadOnlyList<IPm3DeviceCommand> commands,
        CommandResult result,
        DateTime utcNow)
    {
        if (!commands.Any(c => c is T55DetectCommand))
            return;

        RecordDetect(executorKind, port, DetectParser.Parse(result), utcNow);
    }

    public void Invalidate() => _entry = null;

    public void InvalidateForLfTune() => Invalidate();

    public void InvalidateForWrite() => Invalidate();

    public void InvalidateForReadFailure() => Invalidate();

    public void InvalidateForBlock0Mismatch(string? readBlock0Hex)
    {
        if (_entry is null || string.IsNullOrWhiteSpace(readBlock0Hex))
            return;

        if (!string.Equals(_entry.Block0Hex, readBlock0Hex.Trim(), StringComparison.OrdinalIgnoreCase))
            Invalidate();
    }

    public void InvalidateForDisconnect() => Invalidate();

    public bool TryGetCachedBlock0(out string block0Hex)
    {
        if (_entry is null)
        {
            block0Hex = string.Empty;
            return false;
        }

        block0Hex = _entry.Block0Hex;
        return true;
    }

    public static IReadOnlyList<IPm3DeviceCommand> BuildT55CommandBatch(
        Pm3T55DetectCache cache,
        Pm3ExecutorKind executorKind,
        string? port,
        IPm3DeviceCommand followOnCommand,
        DateTime utcNow)
    {
        if (cache.ShouldSkipDetect(executorKind, port, followOnCommand, utcNow))
            return [followOnCommand];

        return [new T55DetectCommand(), followOnCommand];
    }

    private static bool IsCacheableFollowOn(IPm3DeviceCommand command) =>
        command is T55ReadBlockCommand or T55WriteBlockCommand or T55DumpCommand;

    private static string? NormalizePort(string? port) =>
        string.IsNullOrWhiteSpace(port) ? null : port.Trim();

    private static bool PortsEqual(string? left, string? right) =>
        string.Equals(NormalizePort(left), NormalizePort(right), StringComparison.OrdinalIgnoreCase);

    private sealed record Pm3T55DetectCacheEntry(
        Pm3ExecutorKind ExecutorKind,
        string? Port,
        string Block0Hex,
        DateTime DetectedAtUtc);
}
