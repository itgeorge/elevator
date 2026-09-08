using System.Globalization;
using System.Text;
using Pm3UsbApi;

namespace LfElevatorCaptureCli;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Any(a => a is "--help" or "-h"))
        {
            PrintHelp();
            return 0;
        }

        if (args.Length == 1 && args[0] == "--self-test")
        {
            RunSelfTest();
            return 0;
        }

        try
        {
            var options = Parse(args);
            if (options.Label is null)
                throw new ArgumentException("A label is required. Example: red or black.");
            if (!options.DryRun && options.Windows is null && options.DurationSeconds is null &&
                (options.NoPrompt || Console.IsInputRedirected))
                throw new ArgumentException("Noninteractive input requires --windows, --duration-seconds, or --dry-run.");

            var safeLabel = CaptureLabel.Sanitize(options.Label);
            var run = CaptureRun.Create(options, safeLabel);
            run.Manifest.Events.Add(EventText("run created"));
            CaptureManifestStore.Save(run.ManifestPath, run.Manifest);
            run.Transcript.Append("RUN label=" + safeLabel + " mode=passive-bounded-sniff");

            if (options.DryRun)
            {
                var samplePath = Path.Combine(run.Directory, "0001-" + safeLabel + ".pm3");
                var command = PassiveCapturePlan.BuildCommand(options.Samples, samplePath[..^4]);
                if (!PassiveCapturePlan.IsAllowed(command))
                    throw new InvalidOperationException("Internal passive command policy rejected its own command.");
                run.Transcript.Append("DRY-RUN COMMAND " + command);
                run.Manifest.Events.Add(EventText("dry-run; no PM3 process started"));
                run.Manifest.EndedUtc = UtcNow();
                CaptureManifestStore.Save(run.ManifestPath, run.Manifest);
                Console.WriteLine($"Dry run: no PM3 hardware accessed. Output plan: {run.ManifestPath}");
                return 0;
            }

            using var stop = new CaptureStopController();
            ConsoleCancelEventHandler? cancelHandler = null;
            cancelHandler = (_, e) =>
            {
                e.Cancel = true;
                // Ctrl-C is intentionally immediate: it may cancel a PM3 process.
                stop.RequestImmediateStop();
            };
            Console.CancelKeyPress += cancelHandler;

            try
            {
                Console.WriteLine($"Passive LF capture label={safeLabel}; run={run.Directory}");
                Console.WriteLine("Only 'hw version', 'lf sniff -s N', and local 'data save' are used.");
                Console.WriteLine("Move PM3 + fob to the authorized elevator reader; press Enter to stop.");
                Console.WriteLine("Enter requests a graceful stop: the current sniff; data save batch completes and is verified first.");
                Console.WriteLine("Ctrl-C stops immediately and cleans up. No tag writes, reads, config, tune, or simulation are performed.");

                await using var pm3 = new Pm3(new Pm3Options
                {
                    ExecutorKind = Pm3ExecutorKind.Process,
                    Pm3ClientPath = options.Pm3ClientPath,
                    DevicePort = options.Port,
                    AutoConnect = options.Port is null,
                    WorkingDirectory = run.Directory,
                    DefaultCommandTimeout = TimeSpan.FromSeconds(options.TimeoutSeconds),
                    ConnectTimeout = TimeSpan.FromSeconds(options.TimeoutSeconds),
                });

                run.Transcript.Append("COMMAND " + PassiveCapturePlan.ConnectCommand);
                try
                {
                    await pm3.ConnectAsync(stop.ImmediateToken);
                    run.Manifest.Events.Add(EventText("PM3 connected"));
                    run.Transcript.Append("RESULT connected");
                }
                catch (OperationCanceledException) when (stop.ImmediateStopRequested)
                {
                    run.Manifest.Events.Add(EventText("stopped while connecting"));
                    return 130;
                }
                catch (Exception ex)
                {
                    run.Manifest.Events.Add(EventText("connect failed: " + ex.Message));
                    run.Transcript.Append("RESULT connect-failed " + ex);
                    Console.Error.WriteLine($"PM3 connection failed: {ex.Message}");
                    return 1;
                }

                CaptureManifestStore.Save(run.ManifestPath, run.Manifest);
                Task? enterTask = null;
                if (!options.NoPrompt && !Console.IsInputRedirected)
                    enterTask = Task.Run(() => WaitForEnter(stop.RequestGracefulStop));

                var started = DateTimeOffset.UtcNow;
                while (!stop.ImmediateStopRequested && !stop.GracefulStopRequested &&
                       ShouldContinue(run.Manifest.Captures.Count, started, options))
                {
                    var window = run.Manifest.Captures.Count + 1;
                    var captureStarted = DateTimeOffset.UtcNow;
                    var timestamp = captureStarted.ToString("yyyyMMdd'T'HHmmssfff'Z'", CultureInfo.InvariantCulture);
                    var baseName = $"{window:0000}-{safeLabel}-{timestamp}";
                    var basePath = Path.Combine(run.Directory, baseName);
                    var filePath = basePath + ".pm3";
                    var command = PassiveCapturePlan.BuildCommand(options.Samples, basePath);
                    if (!PassiveCapturePlan.IsAllowed(command))
                        throw new InvalidOperationException("Internal passive command policy rejected its own command.");

                    var entry = new CaptureEntry
                    {
                        Window = window,
                        StartedUtc = captureStarted.ToString("O"),
                        FileName = Path.GetFileName(filePath),
                        SamplesRequested = options.Samples,
                    };
                    run.Manifest.Captures.Add(entry);
                    run.Transcript.Append($"COMMAND window={window} {command}");
                    try
                    {
                        // Enter only sets GracefulStopRequested; it cannot cancel this await.
                        // The sniff and data save therefore finish before the stop is observed.
                        var output = await pm3.ExecuteRawCommandAsync(command, stop.ImmediateToken);
                        run.Transcript.Append($"RESULT window={window}\n{output}");
                        entry.EndedUtc = UtcNow();
                        var savedPath = CaptureFileLocator.Find(run.Directory, baseName);
                        if (savedPath is null)
                            throw new IOException("PM3 reported no saved .pm3 file; the bounded sniff may have timed out before data save.");
                        entry.FileName = Path.GetFileName(savedPath);
                        entry.Status = "completed";
                        entry.Retained = true;
                        Console.WriteLine($"Saved window {window}: {entry.FileName}");
                        EvictOldWindows(run, options.KeepWindows);
                    }
                    catch (OperationCanceledException) when (stop.ImmediateStopRequested)
                    {
                        entry.EndedUtc = UtcNow();
                        entry.Status = "cancelled";
                        entry.Error = "stopped immediately by Ctrl-C";
                        run.Transcript.Append($"RESULT window={window} cancelled-immediately");
                        break;
                    }
                    catch (Exception ex) when (stop.ImmediateStopRequested)
                    {
                        entry.EndedUtc = UtcNow();
                        entry.Status = "cancelled";
                        entry.Error = "stopped immediately by Ctrl-C";
                        run.Transcript.Append($"RESULT window={window} cancelled-immediately: {ex}");
                        break;
                    }
                    catch (Exception ex)
                    {
                        entry.EndedUtc = UtcNow();
                        entry.Status = "failed";
                        entry.Error = ex.Message;
                        run.Transcript.Append($"RESULT window={window} failed: {ex}");
                        Console.Error.WriteLine($"Window {window} failed: {ex.Message}");
                    }
                    finally
                    {
                        CaptureManifestStore.Save(run.ManifestPath, run.Manifest);
                    }
                }

                if (stop.GracefulStopRequested)
                {
                    run.Manifest.Events.Add(EventText("graceful Enter stop observed after current batch was saved/verified"));
                    Console.WriteLine("Enter received; current bounded window was saved and verified. Stopping gracefully.");
                }
                var wasImmediateStop = stop.ImmediateStopRequested;
                stop.RequestImmediateStop();
                run.Manifest.Events.Add(EventText("capture loop stopped"));
                run.Manifest.EndedUtc = UtcNow();
                CaptureManifestStore.Save(run.ManifestPath, run.Manifest);
                _ = enterTask; // It is intentionally not awaited: ReadLine can remain blocked on shutdown.
                return wasImmediateStop ? 130 : 0;
            }
            finally
            {
                Console.CancelKeyPress -= cancelHandler;
                stop.RequestImmediateStop();
                run.Manifest.EndedUtc ??= UtcNow();
                CaptureManifestStore.Save(run.ManifestPath, run.Manifest);
                run.Transcript.Append("RUN ended");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 2;
        }
    }

    private static void EvictOldWindows(CaptureRun run, int keep)
    {
        if (keep == 0) return;
        var completed = run.Manifest.Captures
            .Where(c => c.Status == "completed" && c.Retained)
            .OrderBy(c => c.Window)
            .ToList();
        while (completed.Count > keep)
        {
            var old = completed[0];
            completed.RemoveAt(0);
            var path = Path.Combine(run.Directory, old.FileName);
            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception ex) { old.Error = "eviction failed: " + ex.Message; continue; }
            old.Retained = false;
            old.Status = "evicted";
            run.Manifest.Events.Add(EventText($"evicted window {old.Window} (keep={keep})"));
        }
    }

    private static bool ShouldContinue(int completedOrAttemptedWindows, DateTimeOffset started, CaptureOptions options) =>
        (options.Windows is null || completedOrAttemptedWindows < options.Windows.Value) &&
        (options.DurationSeconds is null || DateTimeOffset.UtcNow - started < TimeSpan.FromSeconds(options.DurationSeconds.Value));

    private static void WaitForEnter(Action requestGracefulStop)
    {
        try
        {
            _ = Console.ReadLine();
            requestGracefulStop();
        }
        catch { /* shutdown or redirected console */ }
    }

    private static string EventText(string text) => $"[{UtcNow()}] {text}";
    private static string UtcNow() => DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);

    private static CaptureOptions Parse(string[] args)
    {
        var o = new CaptureOptions();
        var positional = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            string Next(string name) => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{name} requires a value.");
            switch (args[i])
            {
                case "--label": o.Label = Next("--label"); break;
                case "--output": case "-o": o.OutputDirectory = Next(args[i]); break;
                case "--port": case "-p": o.Port = Next(args[i]); break;
                case "--pm3-path": o.Pm3ClientPath = Next("--pm3-path"); break;
                case "--samples": o.Samples = PositiveInt(Next("--samples"), "--samples"); break;
                case "--windows": o.Windows = PositiveInt(Next("--windows"), "--windows"); break;
                case "--duration-seconds": o.DurationSeconds = PositiveInt(Next("--duration-seconds"), "--duration-seconds"); break;
                case "--keep": o.KeepWindows = NonNegativeInt(Next("--keep"), "--keep"); break;
                case "--timeout-seconds": o.TimeoutSeconds = PositiveInt(Next("--timeout-seconds"), "--timeout-seconds"); break;
                case "--no-prompt": o.NoPrompt = true; break;
                case "--dry-run": o.DryRun = true; break;
                case "--help": case "-h": break;
                default:
                    if (args[i].StartsWith('-')) throw new ArgumentException($"Unknown option: {args[i]}");
                    positional.Add(args[i]);
                    break;
            }
        }
        if (o.Label is not null && positional.Count > 0) throw new ArgumentException("Specify the label once, either positionally or with --label.");
        if (positional.Count > 1) throw new ArgumentException("Only one positional label is accepted.");
        o.Label ??= positional.SingleOrDefault();
        if (o.Windows is not null && o.DurationSeconds is not null)
            throw new ArgumentException("Use either --windows or --duration-seconds, not both.");
        return o;
    }

    private static int PositiveInt(string value, string option) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n > 0
            ? n : throw new ArgumentException($"{option} must be a positive integer.");
    private static int NonNegativeInt(string value, string option) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n >= 0
            ? n : throw new ArgumentException($"{option} must be a non-negative integer.");

    private static void RunSelfTest()
    {
        if (CaptureLabel.Sanitize("red/black") != "red_black") throw new InvalidOperationException("label sanitization failed");
        var command = PassiveCapturePlan.BuildCommand(40000, "/tmp/red_capture");
        if (!PassiveCapturePlan.IsAllowed(command) || command.Contains("tune", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("passive command policy failed");
        Console.WriteLine("Offline self-test: PASS (label sanitization and passive command allow-list)");
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Authorized passive LF elevator capture CLI");
        Console.WriteLine("Usage: dotnet run --project debug/LfElevatorCaptureCli -- [options] <label>");
        Console.WriteLine();
        Console.WriteLine("Captures bounded rolling windows using only: hw version; lf sniff -s N; data save -f FILE.");
        Console.WriteLine("No writes, password/config/tune commands, block reads/dumps, clone, sim, or reset commands are sent.");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --output, -o DIR          Root output directory (default: debug/lf-elevator-captures)");
        Console.WriteLine("  --port, -p PORT           Explicit PM3 port; otherwise PM3 helper auto-discovery is used");
        Console.WriteLine("  --pm3-path PATH           Proxmark3 client executable");
        Console.WriteLine("  --samples N               Samples per bounded lf sniff window (default: 40000)");
        Console.WriteLine("  --windows N               Stop after N windows (for scripts/noninteractive use)");
        Console.WriteLine("  --duration-seconds N      Stop after N seconds (alternative to --windows)");
        Console.WriteLine("  --keep N                  Retain newest N files; 0 means retain all (default: 8)");
        Console.WriteLine("  --timeout-seconds N       Per-command/connect timeout (default: 8)");
        Console.WriteLine("  --no-prompt               Do not read stdin; requires --windows or --duration-seconds");
        Console.WriteLine("  --dry-run                 Create manifest/transcript only; never starts PM3");
        Console.WriteLine("  --help, -h                Show this help");
        Console.WriteLine("  --self-test               Offline smoke test; never starts PM3");
        Console.WriteLine();
        Console.WriteLine("Interactive default: move PM3+fob to the authorized reader, then press Enter to stop.");
        Console.WriteLine("Enter requests graceful stop after the active window is saved/verified; Ctrl-C cancels immediately.");
    }

    private sealed class CaptureOptions
    {
        public string? Label { get; set; }
        public string OutputDirectory { get; set; } = Path.Combine("debug", "lf-elevator-captures");
        public string? Port { get; set; }
        public string? Pm3ClientPath { get; set; }
        public int Samples { get; set; } = 40_000;
        public int? Windows { get; set; }
        public int? DurationSeconds { get; set; }
        public int KeepWindows { get; set; } = 8;
        public int TimeoutSeconds { get; set; } = 8;
        public bool NoPrompt { get; set; }
        public bool DryRun { get; set; }
    }

    private sealed class CaptureRun
    {
        public required string Directory { get; init; }
        public required string ManifestPath { get; init; }
        public required CaptureManifest Manifest { get; init; }
        public required CaptureTranscript Transcript { get; init; }

        public static CaptureRun Create(CaptureOptions options, string safeLabel)
        {
            var root = Path.GetFullPath(options.OutputDirectory);
            System.IO.Directory.CreateDirectory(root);
            var runName = $"{DateTimeOffset.UtcNow:yyyyMMdd'T'HHmmssfff'Z'}-{safeLabel}";
            var directory = Path.Combine(root, runName);
            System.IO.Directory.CreateDirectory(directory);
            var manifest = new CaptureManifest
            {
                Label = options.Label!,
                SanitizedLabel = safeLabel,
                StartedUtc = UtcNow(),
                SamplesPerWindow = options.Samples,
                RequestedWindows = options.Windows,
                DurationSeconds = options.DurationSeconds,
                KeepWindows = options.KeepWindows,
            };
            return new CaptureRun
            {
                Directory = directory,
                ManifestPath = Path.Combine(directory, "manifest.json"),
                Manifest = manifest,
                Transcript = new CaptureTranscript(Path.Combine(directory, "transcript.log")),
            };
        }
    }

    private sealed class CaptureTranscript
    {
        private readonly string _path;
        public CaptureTranscript(string path) => _path = path;
        public void Append(string text)
        {
            var builder = new StringBuilder()
                .Append('[').Append(UtcNow()).Append("] ").AppendLine(text);
            File.AppendAllText(_path, builder.ToString());
        }
    }
}
