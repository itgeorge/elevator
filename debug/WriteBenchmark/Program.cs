using System.Buffers.Binary;
using System.Diagnostics;
using System.Reflection;
using Pm3UsbApi;
using RidesCli;
using Tokens;

var options = new Pm3Options { ExecutorKind = Pm3ExecutorKind.Native };
if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PM3_DEVICE_PORT")))
    options = options with { DevicePort = Environment.GetEnvironmentVariable("PM3_DEVICE_PORT") };

await using var pm3 = new Pm3(options);
Console.WriteLine($"Connecting (executor={options.ExecutorKind})...");
await pm3.ConnectAsync();
Console.WriteLine("Connected.\n");

await TimeAsync("read", async () => _ = await ReadRidesAsync(pm3));
Console.WriteLine($"  -> {(await ReadRidesAsync(pm3))} rides\n");

await TimeAsync("set 55", async () => await SetAsync(pm3, 55));
Console.WriteLine($"  -> {(await ReadRidesAsync(pm3))} rides\n");

await TimeAsync("add 5 (set 60)", async () => await SetAsync(pm3, 60));
Console.WriteLine($"  -> {(await ReadRidesAsync(pm3))} rides\n");

await TimeAsync("reset", async () => await ResetAsync(pm3));
Console.WriteLine($"  -> {(await ReadRidesAsync(pm3))} rides after reset\n");

static async Task<double> TimeAsync(string label, Func<Task> action)
{
    var sw = Stopwatch.StartNew();
    await action();
    sw.Stop();
    Console.WriteLine($"{label}: {sw.Elapsed.TotalSeconds:F3}s");
    return sw.Elapsed.TotalSeconds;
}

static async Task<uint> ReadRidesAsync(Pm3 pm3)
{
    var block5 = T55Block.FromHex(await pm3.ReadPage0BlockAsync(5));
    var block6 = T55Block.FromHex(await pm3.ReadPage0BlockAsync(6));
    var result = RideBlockResolver.Resolve(block5, block6);
    if (result.Status != RideReadStatus.Success)
        throw new InvalidOperationException($"Read failed: {result.Status}");
    return result.Rides!.Value;
}

static async Task SetAsync(Pm3 pm3, uint rides)
{
    if (!await pm3.WriteRideMirrorBlocksAsync(TokenBlockUtils.Encode(rides)))
        throw new InvalidOperationException("Set verify failed.");
}

static async Task ResetAsync(Pm3 pm3)
{
    _ = await pm3.ReadPage0BlockAsync(5);
    _ = await pm3.ReadPage0BlockAsync(6);

    var resetBlocks = LoadDefaultResetPage0Blocks();
    var zeroBlock = TokenBlockUtils.Encode(0);
    resetBlocks[5] = zeroBlock;
    resetBlocks[6] = zeroBlock;

    if (!await pm3.WriteAndVerifyPage0BlocksAsync(resetBlocks, 1, 6))
        throw new InvalidOperationException("Reset write/verify failed.");
}

static List<T55Block> LoadDefaultResetPage0Blocks()
{
    var assembly = Assembly.GetAssembly(typeof(RidesCommandHandler))
        ?? throw new InvalidOperationException("RidesCli assembly not found.");
    var resourceName = assembly.GetManifestResourceNames()
        .FirstOrDefault(name => name.EndsWith("default-500-rides.bin", StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException("Embedded resource 'default-500-rides.bin' not found.");

    using var stream = assembly.GetManifestResourceStream(resourceName)
        ?? throw new InvalidOperationException("Failed to load embedded resource stream.");
    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    var bytes = ms.ToArray();

    var blocks = new List<T55Block>(8);
    for (var i = 0; i < 8 * 4; i += 4)
        blocks.Add(new T55Block(BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(i, 4))));
    return blocks;
}
