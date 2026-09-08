using Pm3UsbApi;
using Pm3UsbApi.Commands;
using Pm3UsbApi.Parsers;

const string Usage = "Usage: dotnet run --project debug/ReadOnlyLfElevatorDiagnostics -- [--plan | --self-test | --parse <capture.txt> | --probe-t55 [--port <path>]]";

if (args.Length > 0 && args[0] == "--probe-t55")
{
    if (!TryParseProbeArguments(args, out var probePort))
    {
        Console.Error.WriteLine(Usage);
        Environment.ExitCode = 2;
        return;
    }

    Environment.ExitCode = await RunT55ProbeAsync(probePort);
    return;
}

if (args.Length == 0 || args[0] is "--plan" or "--self-test")
{
    if (args.Length == 0 || args[0] == "--self-test")
        RunOfflineSelfTest();

    if (args.Length == 0 || args[0] == "--plan")
        PrintReadOnlyPlan();

    return;
}

if (args is ["--parse", var path])
{
    ParseCapture(path);
    return;
}

Console.Error.WriteLine(Usage);
Environment.ExitCode = 2;

static bool TryParseProbeArguments(string[] arguments, out string? port)
{
    port = null;
    if (arguments.Length == 1)
        return true;

    if (arguments.Length == 3 && arguments[1] == "--port" && !string.IsNullOrWhiteSpace(arguments[2]))
    {
        port = arguments[2];
        return true;
    }

    return false;
}

static async Task<int> RunT55ProbeAsync(string? port)
{
    // This probe is intentionally bounded and uses the process executor because the native
    // executor does not expose arbitrary CLI text such as `lf search`.
    var probeTimeout = TimeSpan.FromSeconds(15);
    var options = new Pm3Options
    {
        ExecutorKind = Pm3ExecutorKind.Process,
        DevicePort = port,
        AutoConnect = port is null,
        DefaultCommandTimeout = probeTimeout,
        ConnectTimeout = probeTimeout,
    };

    await using var pm3 = new Pm3(options);
    using var cancellation = new CancellationTokenSource(probeTimeout);

    try
    {
        await pm3.ConnectAsync(cancellation.Token);

        const string rawSearchCommand = "lf search";
        var rawSearch = await pm3.ExecuteRawCommandAsync(rawSearchCommand, cancellation.Token);
        Console.WriteLine("Raw lf search output:");
        Console.Write(rawSearch);
        if (!rawSearch.EndsWith(Environment.NewLine, StringComparison.Ordinal))
            Console.WriteLine();

        try
        {
            await pm3.EnsureT55SessionActiveAsync(cancellation.Token);
            Console.WriteLine("T55 detect: detected; session active.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"T55 detect exception: {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"T55 probe failed: {ex.GetType().Name}: {ex.Message}");
        return 1;
    }
    finally
    {
        await pm3.DisconnectAsync();
    }
}

static void PrintReadOnlyPlan()
{
    Console.WriteLine("Read-only LF elevator diagnostic plan");
    Console.WriteLine("This project only formats an allow-listed plan; it never opens a port, starts pm3, or executes a command.");
    Console.WriteLine();

    // Keep this list noninteractive: `pm3 -c "lf tune"` waits for Enter and
    // therefore must not be suggested to a process-client batch.
    var commands = new List<IPm3DeviceCommand>
    {
        new HwVersionCommand(),
        new T55DetectCommand(),
        new T55DumpCommand(),
    };
    commands.AddRange(Enumerable.Range(0, 8)
        .Select(block => (IPm3DeviceCommand)new T55ReadBlockCommand((uint)block)));

    foreach (var command in commands)
        Console.WriteLine($"{command.GetType().Name,-24}  {FormatReadOnlyCommand(command)}");

    Console.WriteLine();
    Console.WriteLine("Suggested noninteractive order: hw version; lf t55 detect; lf t55 dump or individual lf t55 read -b 0..7.");
    Console.WriteLine("IMPORTANT: lf tune is manual/interactive-only; pm3 -c \"lf tune\" waits for Enter and is intentionally omitted here.");
    Console.WriteLine("For a process client, keep detect and its T55 follow-up in one session, e.g.:");
    Console.WriteLine("  pm3 -p <PORT> -c \"lf t55 detect; lf t55 dump\"");
    Console.WriteLine("  pm3 -p <PORT> -c \"lf t55 detect; lf t55 read -b 0\"");
    Console.WriteLine("The native API performs capture/download and ASK/Manchester demodulation locally.");
}

static string FormatReadOnlyCommand(IPm3DeviceCommand command) => command switch
{
    HwVersionCommand => "hw version",
    T55DetectCommand => "lf t55 detect",
    T55DumpCommand => "lf t55 dump",
    T55ReadBlockCommand read when read.Block <= 7 => $"lf t55 read -b {read.Block}",
    _ => throw new InvalidOperationException($"Command is outside the read-only diagnostic allow-list: {command.GetType().Name}"),
};

static void RunOfflineSelfTest()
{
    Require(TryParseProbeArguments(["--probe-t55"], out var noPort) && noPort is null, "probe args without port");
    Require(TryParseProbeArguments(["--probe-t55", "--port", "/dev/null"], out var explicitPort) && explicitPort == "/dev/null", "probe args with port");
    Require(!TryParseProbeArguments(["--probe-t55", "lf dump"], out _), "reject arbitrary probe args");

    var lines = new[]
    {
        "[+] Chip Type: T55x7",
        "[+] Modulation: ASK",
        "[+] Block 0: 00148040",
        "[=] 615 mV",
        "0 | 00148040 | config",
        "1 | F100C064 | identity",
        "2 | A3045930 | identity",
        "3 | 1F12203C | identity",
        "4 | B7B10632 | identity",
        "5 | 7F1270B9 | rides",
        "6 | 7F1270B9 | rides mirror",
        "7 | 00000000 | reserved",
    };

    var result = new CommandResult
    {
        Commands = Array.Empty<IPm3DeviceCommand>(),
        OutputLines = lines,
        ExitCode = 0,
        HasErrors = false,
    };

    var detect = DetectParser.Parse(result);
    Require(detect.ChipFound && detect.ChipType == "T55x7", "detect parser");
    Require(detect.Modulation == "ASK" && detect.Block0Hex == "00148040", "detect fields");
    Require(TuneParser.Parse(result).PeakMilliVolts == 615, "tune parser");

    var dump = DumpParser.Parse(result);
    Require(dump.Success && dump.Blocks.Count == 8, "dump parser");
    for (var block = 0; block < 8; block++)
        Require(BlockReadParser.Parse(result, block).Success, $"block {block} parser");

    Console.WriteLine("Offline self-test: PASS (probe argument guard; detect, tune, dump, and blocks 0-7)");
}

static void ParseCapture(string path)
{
    if (!File.Exists(path))
        throw new FileNotFoundException("Capture text file not found.", path);

    var lines = File.ReadAllLines(path);
    var result = new CommandResult
    {
        Commands = Array.Empty<IPm3DeviceCommand>(),
        OutputLines = lines,
        ExitCode = 0,
        HasErrors = false,
    };

    var detect = DetectParser.Parse(result);
    Console.WriteLine($"Detect: found={detect.ChipFound}, chip={detect.ChipType ?? "(none)"}, modulation={detect.Modulation ?? "(none)"}, block0={detect.Block0Hex ?? "(none)"}");

    for (var block = 0; block < 8; block++)
    {
        var parsed = BlockReadParser.Parse(result, block);
        Console.WriteLine($"Block {block}: {(parsed.Success ? parsed.HexData : "(not found)")}");
    }

    var dump = DumpParser.Parse(result);
    Console.WriteLine($"Dump page 0: success={dump.Success}, blocks={dump.Blocks.Count}");
    var tune = TuneParser.Parse(result);
    Console.WriteLine($"LF tune: {(tune.Success ? $"{tune.PeakMilliVolts} mV" : "(not found)")}");
}

static void Require(bool condition, string name)
{
    if (!condition)
        throw new InvalidOperationException($"Offline self-test failed: {name}");
}
