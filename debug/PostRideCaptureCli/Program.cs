using System.Globalization;
using System.Text;
using Pm3UsbApi;
using Pm3UsbApi.Diagnostics;
using Tokens;

namespace PostRideCaptureCli;

static class Program
{
    private const uint DefaultThresholdMv = 2000;
    private static readonly string[] DefaultExpectedBlock0 = ["00148040", "00148041"];

    static async Task<int> Main(string[] args)
    {
        Pm3DiagnosticLog.EnsureInitialized();

        try
        {
            var options = ParseArgs(args);
            return await RunAsync(options);
        }
        catch (OperationCanceledException)
        {
            Status.Info("Stopped.");
            return 0;
        }
        catch (Exception ex)
        {
            Pm3DiagnosticLog.LogFatal(ex, "PostRideCaptureCli");
            Status.Error($"Fatal: {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> RunAsync(AppOptions options)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Status.Info("PostRideCaptureCli — tune-gated block 5/6 capture");
        Status.Dim($"threshold={options.ThresholdMv} mV  settle={options.SettleDelay.TotalSeconds:0.#}s  expected blk0=[{string.Join(", ", options.ExpectedBlock0)}]");
        Status.Dim($"log={Path.GetFullPath(options.LogPath)}");
        Status.Dim("Ctrl-C to stop.");
        Console.WriteLine();

        await using var pm3 = new Pm3(options.Pm3Options);
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        Status.Info($"Connecting ({FormatPort(options.Pm3Options)})...");
        await pm3.ConnectAsync(cts.Token);
        Status.Ok("Connected.");

        var captureIndex = 0;
        var referenceMv = await TuneAsync(pm3, options, cts.Token);
        Status.Info($"Baseline tune: {referenceMv} mV — place a token when ready.");

        while (!cts.IsCancellationRequested)
        {
            referenceMv = await WaitForDeltaAsync(
                pm3,
                options,
                referenceMv,
                "Waiting for placement...",
                cts.Token);

            Status.Warn($"Placement edge at {referenceMv} mV — settling {options.SettleDelay.TotalSeconds:0.#}s...");
            await Task.Delay(options.SettleDelay, cts.Token);
            referenceMv = await TuneAsync(pm3, options, cts.Token);
            Status.Dim($"Post-settle tune: {referenceMv} mV");

            captureIndex++;
            await TryCaptureAsync(pm3, options, captureIndex, cts.Token);

            Status.Info("Waiting for removal...");
            referenceMv = await WaitForDeltaAsync(
                pm3,
                options,
                referenceMv,
                "Waiting for removal...",
                cts.Token);
            Status.Warn($"Removal edge at {referenceMv} mV");

            // Brief settle so the empty-antenna reference is stable before the next placement.
            await Task.Delay(TimeSpan.FromMilliseconds(400), cts.Token);
            referenceMv = await TuneAsync(pm3, options, cts.Token);
            Status.Info($"Empty baseline: {referenceMv} mV — place next token.");
        }

        Status.Info("Stopped.");
        return 0;
    }

    private static async Task TryCaptureAsync(Pm3 pm3, AppOptions options, int captureIndex, CancellationToken ct)
    {
        Status.Info($"[{captureIndex}] Reading block 0...");
        string block0;
        try
        {
            block0 = await pm3.ReadPage0BlockAsync(0, ct);
        }
        catch (Exception ex)
        {
            Status.Error($"[{captureIndex}] Block 0 read failed: {ex.Message}");
            AppendLog(options.LogPath, $"{Timestamp()} #{captureIndex} BLOCK0_FAIL {ex.Message}");
            return;
        }

        if (!options.ExpectedBlock0.Contains(block0, StringComparer.OrdinalIgnoreCase))
        {
            Status.Error($"[{captureIndex}] Unexpected block 0: {block0} (expected {string.Join("|", options.ExpectedBlock0)})");
            AppendLog(options.LogPath, $"{Timestamp()} #{captureIndex} BLOCK0_UNEXPECTED {block0}");
            return;
        }

        Status.Ok($"[{captureIndex}] Block 0 OK: {block0}");
        Status.Info($"[{captureIndex}] Reading blocks 5 and 6...");

        try
        {
            var (block5, block6) = await pm3.ReadRideMirrorBlocksAsync(ct);
            var match = string.Equals(block5, block6, StringComparison.OrdinalIgnoreCase);
            Status.Ok($"[{captureIndex}] blk5={block5}  blk6={block6}  mirrored={(match ? "yes" : "NO")}");
            AppendLog(options.LogPath, $"{Timestamp()} #{captureIndex} OK blk0={block0} blk5={block5} blk6={block6}");
        }
        catch (Exception ex)
        {
            Status.Error($"[{captureIndex}] Blocks 5/6 read failed: {ex.Message}");
            AppendLog(options.LogPath, $"{Timestamp()} #{captureIndex} RIDE_BLOCKS_FAIL blk0={block0} {ex.Message}");
        }
    }

    private static async Task<uint> WaitForDeltaAsync(
        Pm3 pm3,
        AppOptions options,
        uint referenceMv,
        string waitingLabel,
        CancellationToken ct)
    {
        Status.Dim($"{waitingLabel} (ref {referenceMv} mV, Δ>{options.ThresholdMv})");
        while (!ct.IsCancellationRequested)
        {
            var mv = await TuneAsync(pm3, options, ct);
            var delta = AbsDiff(mv, referenceMv);
            if (delta > options.ThresholdMv)
            {
                Status.Warn($"Tune edge: {referenceMv} → {mv} mV (Δ{delta})");
                return mv;
            }

            // Quiet heartbeat so the console doesn't look frozen.
            Status.Heartbeat($"tune {mv} mV  Δ{delta}");
        }

        ct.ThrowIfCancellationRequested();
        return referenceMv;
    }

    private static async Task<uint> TuneAsync(Pm3 pm3, AppOptions options, CancellationToken ct)
    {
        await pm3.StartLfTuneAsync(ct, options.TuneSampleCount, options.TuneTimeout);
        return await pm3.GetLfTuneLastMilliVoltsAsync(ct);
    }

    private static uint AbsDiff(uint a, uint b) => a >= b ? a - b : b - a;

    private static void AppendLog(string path, string line)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.AppendAllText(path, line + Environment.NewLine);
    }

    private static string Timestamp() => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    private static string FormatPort(Pm3Options options) =>
        !string.IsNullOrWhiteSpace(options.DevicePort)
            ? options.DevicePort
            : options.AutoConnect ? "auto" : "none";

    private static AppOptions ParseArgs(string[] args)
    {
        var pm3 = new Pm3Options
        {
            WorkingDirectory = Pm3Options.DevRunsDirectoryName,
            NativeLfTuneSampleCount = 12,
            NativeLfTuneTimeout = TimeSpan.FromSeconds(1.5),
        };
        var threshold = DefaultThresholdMv;
        var settle = TimeSpan.FromSeconds(1);
        var expected = new List<string>(DefaultExpectedBlock0);
        var expectedOverridden = false;
        var logPath = Path.Combine(Directory.GetCurrentDirectory(), "post-ride-captures.log");
        var tuneSamples = 12;
        var tuneTimeout = TimeSpan.FromSeconds(1.5);

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--port" when i + 1 < args.Length:
                    pm3 = ApplyPort(pm3, args[++i]);
                    break;
                case "--threshold-mv" when i + 1 < args.Length:
                    threshold = uint.Parse(args[++i], CultureInfo.InvariantCulture);
                    break;
                case "--settle-ms" when i + 1 < args.Length:
                    settle = TimeSpan.FromMilliseconds(int.Parse(args[++i], CultureInfo.InvariantCulture));
                    break;
                case "--expected-block0" when i + 1 < args.Length:
                    if (!expectedOverridden)
                    {
                        expected.Clear();
                        expectedOverridden = true;
                    }
                    expected.Add(T55Block.FromHex(args[++i]).ToHex());
                    break;
                case "--log" when i + 1 < args.Length:
                    logPath = args[++i];
                    break;
                case "--tune-samples" when i + 1 < args.Length:
                    tuneSamples = int.Parse(args[++i], CultureInfo.InvariantCulture);
                    break;
                case "--tune-timeout-ms" when i + 1 < args.Length:
                    tuneTimeout = TimeSpan.FromMilliseconds(int.Parse(args[++i], CultureInfo.InvariantCulture));
                    break;
                case "--help":
                case "-h":
                    PrintHelp();
                    Environment.Exit(0);
                    break;
                default:
                    throw new ArgumentException($"Unknown argument '{args[i]}'. Use --help.");
            }
        }

        if (expected.Count == 0)
            expected.AddRange(DefaultExpectedBlock0);

        pm3 = pm3 with
        {
            NativeLfTuneSampleCount = tuneSamples,
            NativeLfTuneTimeout = tuneTimeout,
        };

        return new AppOptions(pm3, threshold, settle, expected.ToArray(), logPath, tuneSamples, tuneTimeout);
    }

    private static Pm3Options ApplyPort(Pm3Options options, string value)
    {
        var v = value.Trim().ToLowerInvariant();
        if (v == "auto")
            return options with { DevicePort = null, AutoConnect = true };
        if (v is "" or "none" or "off")
            return options with { DevicePort = null, AutoConnect = false };
        return options with { DevicePort = value.Trim(), AutoConnect = true };
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
            PostRideCaptureCli — watch LF tune edges, then capture blk0/5/6

              --port <path|auto>         PM3 port (default: auto)
              --threshold-mv <n>         Tune delta to treat as place/remove (default: 2000)
              --settle-ms <n>            Delay after placement before reads (default: 1000)
              --expected-block0 <hex>    Allowed block 0 value (repeatable; default: 00148040 and 00148041)
              --log <path>               Capture log path (default: ./post-ride-captures.log)
              --tune-samples <n>         Native LF tune samples (default: 12)
              --tune-timeout-ms <n>      Native LF tune timeout (default: 1500)
            """);
    }

    private sealed record AppOptions(
        Pm3Options Pm3Options,
        uint ThresholdMv,
        TimeSpan SettleDelay,
        string[] ExpectedBlock0,
        string LogPath,
        int TuneSampleCount,
        TimeSpan TuneTimeout);
}

static class Status
{
    public static void Info(string message) => Write(ConsoleColor.Cyan, message);
    public static void Ok(string message) => Write(ConsoleColor.Green, message);
    public static void Warn(string message) => Write(ConsoleColor.Yellow, message);
    public static void Error(string message) => Write(ConsoleColor.Red, message);
    public static void Dim(string message) => Write(ConsoleColor.DarkGray, message);

    public static void Heartbeat(string message)
    {
        var previous = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write($"\r  {message}          ");
        Console.ForegroundColor = previous;
    }

    private static void Write(ConsoleColor color, string message)
    {
        ClearHeartbeatLine();

        var previous = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}");
        Console.ForegroundColor = previous;
    }

    private static void ClearHeartbeatLine()
    {
        var width = 80;
        try
        {
            if (Console.WindowWidth > 1)
                width = Console.WindowWidth - 1;
        }
        catch (IOException)
        {
            // Redirected console; keep a fixed wipe width.
        }

        Console.Write('\r');
        Console.Write(new string(' ', width));
        Console.Write('\r');
    }
}
