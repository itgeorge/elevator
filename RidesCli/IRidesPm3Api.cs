using Tokens;

namespace RidesCli;

/// <summary>
/// Abstraction for Proxmark3 operations. Allows tests to fake hardware.
/// </summary>
public interface IRidesPm3Api
{
    /// <summary>Detect whether a T55xx token is present.</summary>
    Task<bool> TryDetectTokenAsync(CancellationToken ct = default);

    /// <summary>Read a block from page 0.</summary>
    Task<string> ReadPage0BlockAsync(uint block, CancellationToken ct = default);

    /// <summary>Write a block to page 0 (blocks 1-6 only).</summary>
    Task WritePage0BlockAsync(uint block, T55Block data, CancellationToken ct = default);

    /// <summary>Dump all page 0 blocks.</summary>
    Task<string> DumpAsync(CancellationToken ct = default);

    /// <summary>Write mirrored ride blocks 5 and 6 with verify.</summary>
    Task<bool> WriteRideMirrorBlocksAsync(T55Block data, CancellationToken ct = default);

    /// <summary>Write and verify page 0 blocks in the inclusive range.</summary>
    Task<bool> WriteAndVerifyPage0BlocksAsync(IReadOnlyList<T55Block> blocks, int firstBlock, int lastBlock, CancellationToken ct = default);

    /// <summary>Run LF tune and return peak mV.</summary>
    Task<uint> GetSignalStrengthMvAsync(CancellationToken ct = default);
}
