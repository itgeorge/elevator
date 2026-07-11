using System.Buffers.Binary;
using System.Diagnostics;
using Pm3UsbApi;
using Tokens;

namespace Pm3UsbApi.Tests.Integration;

/// <summary>
/// Native executor load test: interleaved read, tune, set, add, and reset operations.
/// Leaves the token at <see cref="TargetFinalRides"/> rides.
/// </summary>
public static class NativeRideLoadTestRunner
{
    public const uint TargetFinalRides = 50;

    public static async Task<NativeRideLoadTestResult> RunAsync(
        Pm3 pm3,
        string resetImagePath,
        Action<string>? log = null,
        CancellationToken cancellationToken = default)
    {
        var swTotal = Stopwatch.StartNew();
        var ops = 0;

        void Write(string message) => (log ?? Console.WriteLine)(message);

        async Task Run(string name, Func<Task> action)
        {
            ops++;
            var sw = Stopwatch.StartNew();
            Write($"[{ops,2}] {name}...");
            await action().WaitAsync(cancellationToken);
            Write($"    OK ({sw.ElapsedMilliseconds}ms)");
        }

        async Task<uint> ReadRides(string label, uint? expected = null)
        {
            uint rides = 0;
            await Run(label, async () =>
            {
                rides = await ReadAndDecodeRidesAsync(pm3, cancellationToken);
                Write($"    rides={rides}");
                if (expected.HasValue && rides != expected.Value)
                {
                    throw new InvalidOperationException(
                        $"Expected {expected.Value} rides after {label}, got {rides}.");
                }
            });
            return rides;
        }

        async Task Tune(string label)
        {
            await Run(label, async () =>
            {
                await pm3.StartLfTuneAsync(cancellationToken);
                var mv = await pm3.GetLfTuneLastMilliVoltsAsync(cancellationToken);
                Write($"    tune={mv} mV");
                if (mv <= 1000)
                    throw new InvalidOperationException($"LF tune coupling too low: {mv} mV.");
            });
        }

        async Task SetRides(string label, uint target)
        {
            await Run(label, async () =>
            {
                await WriteRidesAsync(pm3, target, cancellationToken);
                Write($"    set -> {target}");
            });
        }

        async Task AddRides(string label, int delta)
        {
            await Run(label, async () =>
            {
                var current = await ReadAndDecodeRidesAsync(pm3, cancellationToken);
                var target = (uint)Math.Clamp((int)current + delta, 0, 500);
                await WriteRidesAsync(pm3, target, cancellationToken);
                Write($"    add {delta} -> {target} (was {current})");
            });
        }

        await Run("connect", async () => await pm3.ConnectAsync(cancellationToken));

        await ReadRides("read baseline");
        await Tune("tune #1");
        await ReadRides("read after tune #1");
        await SetRides("set 100", 100);
        await ReadRides("read after set 100", 100);
        await Tune("tune #2");
        await SetRides("set 25", 25);
        await ReadRides("read after set 25", 25);
        await AddRides("add 25", 25);
        await ReadRides("read after add 25", 50);
        await SetRides("set 175", 175);
        await ReadRides("read after set 175", 175);
        await Tune("tune #3");
        await SetRides("set 0", 0);
        await ReadRides("read after set 0", 0);
        await SetRides("set 300", 300);
        await ReadRides("read after set 300", 300);
        await Tune("tune #4");
        await SetRides("set 42", 42);
        await ReadRides("read after set 42", 42);
        await AddRides("add 8", 8);
        await ReadRides("read after add 8", 50);
        await Run("reset token image", async () => await ResetTokenAsync(pm3, resetImagePath, cancellationToken));
        await ReadRides("read after reset", 0);
        await SetRides("set 120", 120);
        await ReadRides("read after set 120", 120);
        await Tune("tune #5");
        await SetRides("set 50", TargetFinalRides);
        await ReadRides("read final", TargetFinalRides);
        await Tune("tune #6");
        var finalRides = await ReadRides("read verify final", TargetFinalRides);

        return new NativeRideLoadTestResult(ops, finalRides, swTotal.ElapsedMilliseconds);
    }

    public static string ResolveResetImagePath(string? baseDirectory = null)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(baseDirectory))
            candidates.Add(Path.Combine(baseDirectory, "Fixtures", "default-500-rides.bin"));

        candidates.Add(Path.Combine(AppContext.BaseDirectory, "Fixtures", "default-500-rides.bin"));
        candidates.Add(Path.Combine(Directory.GetCurrentDirectory(), "TokenDumpsCli", "Data", "default-500-rides.bin"));

        foreach (var path in candidates)
        {
            if (File.Exists(path))
                return path;
        }

        throw new FileNotFoundException(
            "Reset image not found. Expected Fixtures/default-500-rides.bin in test output or TokenDumpsCli/Data.");
    }

    private static async Task<uint> ReadAndDecodeRidesAsync(Pm3 pm3, CancellationToken ct)
    {
        var hex = await pm3.ReadPage0BlockAsync(5, ct);
        var block = T55Block.FromHex(hex);
        if (!TokenBlockUtils.Families.TryGetFamilyFromBlock(block, out _))
            throw new InvalidOperationException($"Unknown encoding family in block 5: {block.ToHex()}");
        return TokenBlockUtils.Decode(block);
    }

    private static async Task WriteRidesAsync(Pm3 pm3, uint rides, CancellationToken ct)
    {
        var block = TokenBlockUtils.Encode(rides, EncodingSequences.Mercury);
        await pm3.WritePage0BlockAsync(5, block, ct);
        await pm3.WritePage0BlockAsync(6, block, ct);
    }

    private static async Task ResetTokenAsync(Pm3 pm3, string resetImagePath, CancellationToken ct)
    {
        var bytes = await File.ReadAllBytesAsync(resetImagePath, ct);
        var blocks = LoadBlocks(bytes);
        var zero = EncodingSequences.Mercury.Encode(0);
        blocks[5] = zero;
        blocks[6] = zero;

        for (uint block = 1; block <= 6; block++)
            await pm3.WritePage0BlockAsync(block, blocks[(int)block], ct);

        var readBack = await pm3.ReadPage0BlockAsync(5, ct);
        if (readBack != zero.ToHex())
            throw new InvalidOperationException($"Reset verify failed: block5={readBack}, expected {zero.ToHex()}.");
    }

    private static List<T55Block> LoadBlocks(byte[] bytes)
    {
        if (bytes.Length < 32)
            throw new InvalidDataException("Reset image too small.");
        var blocks = new List<T55Block>(8);
        for (var i = 0; i < 32; i += 4)
            blocks.Add(new T55Block(BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(i, 4))));
        return blocks;
    }
}

public sealed record NativeRideLoadTestResult(int OperationCount, uint FinalRides, long ElapsedMilliseconds);
