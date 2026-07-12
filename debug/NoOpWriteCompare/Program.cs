using Pm3UsbApi;
using Pm3UsbApi.Diagnostics;
using Tokens;

var executor = Pm3Options.ReadExecutorKindFromEnvironment();
var mode = "single";
var blocks = new List<uint> { 2, 1, 5, 6 };

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--executor" when i + 1 < args.Length:
            executor = args[++i].Equals("process", StringComparison.OrdinalIgnoreCase)
                ? Pm3ExecutorKind.Process
                : Pm3ExecutorKind.Native;
            break;
        case "--mode" when i + 1 < args.Length:
            mode = args[++i].Trim().ToLowerInvariant();
            break;
        case "--blocks" when i + 1 < args.Length:
            blocks = args[++i].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(uint.Parse)
                .ToList();
            break;
    }
}

Pm3DiagnosticLog.EnsureInitialized();
Console.WriteLine($"PM3 logs: {Pm3DiagnosticLog.Current.BaseDirectory}");
Console.WriteLine($"PM3 session log: {Pm3DiagnosticLog.Current.SessionLogPath}");
Console.WriteLine($"PM3 errors log: {Pm3DiagnosticLog.Current.ErrorsLogPath}");

var options = new Pm3Options
{
    ExecutorKind = executor,
    DefaultCommandTimeout = TimeSpan.FromSeconds(30),
};

if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PM3_DEVICE_PORT")))
    options = options with { DevicePort = Environment.GetEnvironmentVariable("PM3_DEVICE_PORT") };

await using var pm3 = new Pm3(options);
Console.WriteLine($"Connecting (executor={options.ExecutorKind})...");
try
{
    await pm3.ConnectAsync();
    Console.WriteLine("Connected.");
}
catch (Exception ex)
{
    Console.WriteLine($"CONNECT_THROW executor={options.ExecutorKind} type={ex.GetType().Name} message={ex.Message}");
    Environment.ExitCode = 2;
    return;
}

if (mode is "venus-reset-single" or "mercury-reset-single")
{
    var targets = mode == "venus-reset-single"
        ? new Dictionary<uint, string>
        {
            [1] = "43FE0062",
            [2] = "5BA494A3",
            [3] = "D6D1C733",
            [4] = "D6D1C733",
            [5] = "48C74948",
            [6] = "48C74948",
        }
        : new Dictionary<uint, string>
        {
            [1] = "9BFE0062",
            [2] = "5BA4A3DE",
            [3] = "D5D1D713",
            [4] = "D5D1D713",
            [5] = EncodingSequences.Mercury.Encode(0).ToHex(),
            [6] = EncodingSequences.Mercury.Encode(0).ToHex(),
        };

    Console.WriteLine($"Mode: {mode}");
    Console.WriteLine("Baseline read 0..7 before destructive single-block writes:");
    for (uint block = 0; block <= 7; block++)
    {
        var hex = await pm3.ReadPage0BlockAsync(block);
        Console.WriteLine($"BEFORE block={block} hex={hex}");
    }

    foreach (var (block, intended) in targets.OrderBy(kvp => kvp.Key))
    {
        if (block is 0 or 7 || block > 7)
            throw new InvalidOperationException($"Refusing to write unsafe block {block}.");

        Console.WriteLine($"WRITE_ATTEMPT executor={options.ExecutorKind} block={block} intended={intended}");
        try
        {
            await pm3.WritePage0BlockAsync(block, T55Block.FromHex(intended));
            Console.WriteLine($"WRITE_RETURNED executor={options.ExecutorKind} block={block} intended={intended}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"WRITE_THROW executor={options.ExecutorKind} block={block} intended={intended} type={ex.GetType().Name} message={ex.Message}");
            Console.WriteLine("Stopping further writes after write failure.");
            break;
        }

        try
        {
            var after = await pm3.ReadPage0BlockAsync(block);
            var ok = string.Equals(after, intended, StringComparison.OrdinalIgnoreCase);
            Console.WriteLine($"VERIFY executor={options.ExecutorKind} block={block} intended={intended} readback={after} ok={ok}");
            if (!ok)
            {
                Console.WriteLine("Stopping further writes after verify mismatch.");
                break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"VERIFY_READ_THROW executor={options.ExecutorKind} block={block} intended={intended} type={ex.GetType().Name} message={ex.Message}");
            Console.WriteLine("Stopping further writes after verify read failure.");
            break;
        }
    }

    Console.WriteLine("Final read 0..7 after destructive single-block writes:");
    for (uint block = 0; block <= 7; block++)
    {
        try
        {
            var hex = await pm3.ReadPage0BlockAsync(block);
            Console.WriteLine($"FINAL block={block} hex={hex}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FINAL_READ_THROW block={block} type={ex.GetType().Name} message={ex.Message}");
        }
    }

    return;
}

if (mode == "batch")
{
    var before = new List<T55Block>();
    for (uint block = 0; block <= 7; block++)
    {
        var hex = await pm3.ReadPage0BlockAsync(block);
        before.Add(T55Block.FromHex(hex));
        Console.WriteLine($"BEFORE block={block} hex={hex}");
    }

    try
    {
        var ok = await pm3.WriteAndVerifyPage0BlocksAsync(before, 1, 6);
        Console.WriteLine($"BATCH_RESULT executor={options.ExecutorKind} first=1 last=6 ok={ok}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"BATCH_THROW executor={options.ExecutorKind} type={ex.GetType().Name} message={ex.Message}");
    }

    for (uint block = 0; block <= 7; block++)
    {
        try
        {
            var hex = await pm3.ReadPage0BlockAsync(block);
            Console.WriteLine($"AFTER block={block} hex={hex}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"AFTER_READ_THROW block={block} type={ex.GetType().Name} message={ex.Message}");
        }
    }

    return;
}

foreach (var block in blocks)
{
    if (block is 0 or 7 || block > 7)
        throw new ArgumentOutOfRangeException(nameof(block), "This harness only writes page-0 blocks 1..6.");

    string before;
    try
    {
        before = await pm3.ReadPage0BlockAsync(block);
        Console.WriteLine($"BEFORE executor={options.ExecutorKind} block={block} hex={before}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"READ_BEFORE_THROW executor={options.ExecutorKind} block={block} type={ex.GetType().Name} message={ex.Message}");
        continue;
    }

    try
    {
        await pm3.WritePage0BlockAsync(block, T55Block.FromHex(before));
        Console.WriteLine($"WRITE_RETURNED executor={options.ExecutorKind} block={block} intended={before}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"WRITE_THROW executor={options.ExecutorKind} block={block} intended={before} type={ex.GetType().Name} message={ex.Message}");
    }

    try
    {
        var after = await pm3.ReadPage0BlockAsync(block);
        Console.WriteLine($"AFTER executor={options.ExecutorKind} block={block} hex={after} matchesBefore={string.Equals(after, before, StringComparison.OrdinalIgnoreCase)}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"READ_AFTER_THROW executor={options.ExecutorKind} block={block} type={ex.GetType().Name} message={ex.Message}");
    }
}
