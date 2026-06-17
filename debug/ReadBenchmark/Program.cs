using System.Diagnostics;
using Pm3UsbApi;
using RidesCli;
using Tokens;

const int ReadCount = 10;
var mode = args.Length > 0 ? args[0].ToLowerInvariant() : "compare";

var options = new Pm3Options { ExecutorKind = Pm3ExecutorKind.Native };
if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PM3_DEVICE_PORT")))
    options = options with { DevicePort = Environment.GetEnvironmentVariable("PM3_DEVICE_PORT") };

await using var pm3 = new Pm3(options);
Console.WriteLine($"ReadBenchmark mode={mode} executor={options.ExecutorKind}");
Console.WriteLine("Connecting...");
await pm3.ConnectAsync();
Console.WriteLine("Connected.\n");

switch (mode)
{
    case "compare":
        await RunScenario(pm3, "warm-sequential", cold: false, batch: false);
        Console.WriteLine();
        await RunScenario(pm3, "warm-batch", cold: false, batch: true);
        Console.WriteLine();
        await RunScenario(pm3, "cold-sequential", cold: true, batch: false);
        Console.WriteLine();
        await RunScenario(pm3, "cold-batch", cold: true, batch: true);
        break;
    default:
        var cold = mode.StartsWith("cold-", StringComparison.Ordinal);
        var batch = mode is "batch" or "cold-batch";
        var label = mode;
        if (mode is "sequential" or "batch")
            label = $"warm-{mode}";
        await RunScenario(pm3, label, cold, batch);
        break;
}

static async Task RunScenario(Pm3 pm3, string label, bool cold, bool batch)
{
    Console.WriteLine($"=== {label} ===");

    var warmup = await ReadOnceAsync(pm3, batch, invalidateFirst: cold);
    Console.WriteLine($"warmup: {warmup.Rides} rides");

    var timings = new List<double>(ReadCount);
    for (var i = 0; i < ReadCount; i++)
    {
        var sw = Stopwatch.StartNew();
        var result = await ReadOnceAsync(pm3, batch, invalidateFirst: cold);
        sw.Stop();
        timings.Add(sw.Elapsed.TotalSeconds);
        Console.WriteLine($"  read {i + 1,2}: {sw.Elapsed.TotalSeconds:F3}s -> {result.Rides} rides");
    }

    PrintStats(label, timings);
}

static async Task<(uint Rides, string Block5, string Block6)> ReadOnceAsync(
    Pm3 pm3,
    bool batch,
    bool invalidateFirst)
{
    if (invalidateFirst)
        pm3.InvalidateT55DetectCache();

    string block5Hex;
    string block6Hex;

    if (batch)
        (block5Hex, block6Hex) = await pm3.ReadRideMirrorBlocksAsync();
    else
    {
        block5Hex = await pm3.ReadPage0BlockAsync(5);
        block6Hex = await pm3.ReadPage0BlockAsync(6);
    }

    var resolved = RideBlockResolver.Resolve(T55Block.FromHex(block5Hex), T55Block.FromHex(block6Hex));
    if (resolved.Status != RideReadStatus.Success)
        throw new InvalidOperationException($"Read failed: {resolved.Status}");

    return (resolved.Rides!.Value, block5Hex, block6Hex);
}

static void PrintStats(string mode, IReadOnlyList<double> timings)
{
    var sorted = timings.OrderBy(t => t).ToList();
    var mean = timings.Average();
    var median = sorted[sorted.Count / 2];
    Console.WriteLine();
    Console.WriteLine(mode);
    Console.WriteLine($"  count:  {timings.Count}");
    Console.WriteLine($"  total:  {timings.Sum():F3}s");
    Console.WriteLine($"  mean:   {mean:F3}s");
    Console.WriteLine($"  median: {median:F3}s");
    Console.WriteLine($"  min:    {sorted[0]:F3}s");
    Console.WriteLine($"  max:    {sorted[^1]:F3}s");
    if (timings.Count > 1)
    {
        var variance = timings.Select(t => (t - mean) * (t - mean)).Average();
        Console.WriteLine($"  stdev:  {Math.Sqrt(variance):F3}s");
    }
}
