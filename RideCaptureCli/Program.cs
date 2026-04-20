using Pm3UsbApi;

namespace RideCaptureCli;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var options = ParseOptions(args);
        var config = RideCaptureConfig.LoadOrCreate(options.ConfigPath);
        var paths = new CapturePaths(config.OutputRootDirectory);
        paths.EnsureExists();

        var store = new CaptureCsvStore();
        var sequenceService = new CaptureSequenceService();

        if (options.CommandArgs.Count > 0)
            return await ExecuteSingleCommandAsync(options, config, paths, store, sequenceService);

        var pm3Options = BuildPm3Options(options, config);
        await using var pm3 = new Pm3(pm3Options);

        try
        {
            await pm3.ConnectAsync();
        }
        catch (Pm3Exception ex)
        {
            ConsoleStatusWriter.WriteError($"Failed to connect to Proxmark3: {ex.Message}");
            return 1;
        }

        var scanner = new CaptureScanner(new Pm3RideCaptureApiAdapter(pm3), config, paths);

        Console.WriteLine("RideCaptureCli - token sequence capture");
        Console.WriteLine($"Config: {Path.GetFullPath(options.ConfigPath)}");
        Console.WriteLine($"CSV:    {paths.CsvPath}");
        Console.WriteLine("Commands: Enter=scan, zero=scan and anchor zero, exact <n> [sequenceId], help, exit");
        Console.WriteLine();

        while (true)
        {
            Console.Write("capture> ");
            var line = Console.ReadLine();
            if (line is null)
                break;

            var command = line.Trim();
            if (command.Equals("exit", StringComparison.OrdinalIgnoreCase))
                break;

            if (command.Equals("help", StringComparison.OrdinalIgnoreCase))
            {
                PrintHelp();
                continue;
            }

            try
            {
                var result = await ExecuteCommandAsync(command, options, config, paths, store, sequenceService, scanner);
                if (result is not null)
                    ConsoleStatusWriter.WriteCaptureResult(result.AddedRecord, result.AutoNormalized, result.ManualAnchorRideCount, result.SequenceOnlyUpdate);
            }
            catch (Exception ex)
            {
                ConsoleStatusWriter.WriteError(ex.Message);
            }
        }

        return 0;
    }

    private static async Task<int> ExecuteSingleCommandAsync(
        CommandLineOptions options,
        RideCaptureConfig config,
        CapturePaths paths,
        CaptureCsvStore store,
        CaptureSequenceService sequenceService)
    {
        try
        {
            CaptureApplyResult? result;
            var command = string.Join(' ', options.CommandArgs);
            var needsScanner = CommandNeedsScanner(command);
            if (needsScanner)
            {
                var pm3Options = BuildPm3Options(options, config);
                await using var pm3 = new Pm3(pm3Options);
                await pm3.ConnectAsync();
                var scanner = new CaptureScanner(new Pm3RideCaptureApiAdapter(pm3), config, paths);
                result = await ExecuteCommandAsync(command, options, config, paths, store, sequenceService, scanner);
            }
            else
            {
                result = await ExecuteCommandAsync(command, options, config, paths, store, sequenceService, scanner: null);
            }

            if (result is not null)
                ConsoleStatusWriter.WriteCaptureResult(result.AddedRecord, result.AutoNormalized, result.ManualAnchorRideCount, result.SequenceOnlyUpdate);
            return 0;
        }
        catch (Pm3Exception ex)
        {
            ConsoleStatusWriter.WriteError($"Failed to connect to Proxmark3: {ex.Message}");
            return 1;
        }
        catch (Exception ex)
        {
            ConsoleStatusWriter.WriteError(ex.Message);
            return 1;
        }
    }

    private static bool CommandNeedsScanner(string command)
    {
        var trimmed = command.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return true;

        if (trimmed.StartsWith("exact ", StringComparison.OrdinalIgnoreCase))
        {
            var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length < 3;
        }

        return trimmed.Equals("zero", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<CaptureApplyResult?> ExecuteCommandAsync(
        string command,
        CommandLineOptions options,
        RideCaptureConfig config,
        CapturePaths paths,
        CaptureCsvStore store,
        CaptureSequenceService sequenceService,
        CaptureScanner? scanner)
    {
        if (string.IsNullOrEmpty(command))
        {
            if (scanner is null)
                throw new InvalidOperationException("A scanner is required for an empty scan command.");
            return await ExecuteScanCommandAsync(scanner, paths, store, sequenceService, exactRideCount: null);
        }

        if (command.Equals("zero", StringComparison.OrdinalIgnoreCase))
        {
            if (scanner is null)
                throw new InvalidOperationException("A scanner is required for the zero command without a sequence id.");
            return await ExecuteScanCommandAsync(scanner, paths, store, sequenceService, exactRideCount: 0);
        }

        if (command.StartsWith("exact ", StringComparison.OrdinalIgnoreCase))
        {
            var parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || !int.TryParse(parts[1], out var exactRideCount) || exactRideCount < 0)
                throw new InvalidOperationException("Invalid exact value. Use: exact <non-negative-integer> [sequenceId]");

            if (parts.Length >= 3)
            {
                var sequenceId = parts[2];
                return ExecuteSequenceOnlyExact(paths, store, sequenceService, sequenceId, exactRideCount);
            }

            if (scanner is null)
                throw new InvalidOperationException("A scanner is required for exact <n> without a sequence id.");
            return await ExecuteScanCommandAsync(scanner, paths, store, sequenceService, exactRideCount);
        }

        throw new InvalidOperationException("Unknown command. Use Enter, zero, exact <n> [sequenceId], help, or exit.");
    }

    private static async Task<CaptureApplyResult> ExecuteScanCommandAsync(
        CaptureScanner scanner,
        CapturePaths paths,
        CaptureCsvStore store,
        CaptureSequenceService sequenceService,
        int? exactRideCount)
    {
        var scan = await scanner.ScanAsync();
        var existing = store.Load(paths.CsvPath);
        var result = sequenceService.ApplyScan(existing, scan, exactRideCount);
        store.Save(paths.CsvPath, result.Records);
        return result;
    }

    private static CaptureApplyResult ExecuteSequenceOnlyExact(
        CapturePaths paths,
        CaptureCsvStore store,
        CaptureSequenceService sequenceService,
        string sequenceId,
        int exactRideCount)
    {
        var existing = store.Load(paths.CsvPath);
        var result = sequenceService.ApplyExactToLatestSequenceRecord(existing, sequenceId, exactRideCount);
        store.Save(paths.CsvPath, result.Records);
        return result;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Commands:");
        Console.WriteLine("  <Enter>                 Scan current token and append to CSV");
        Console.WriteLine("  zero                    Scan current token and anchor the current sequence at zero rides");
        Console.WriteLine("  exact <n>               Scan current token and anchor the current sequence at exact ride count n");
        Console.WriteLine("  exact <n> <sequenceId>  Update the latest row in an existing sequence without scanning");
        Console.WriteLine("  help                    Show help");
        Console.WriteLine("  exit                    Quit");
        Console.WriteLine();
    }

    private static Pm3Options BuildPm3Options(CommandLineOptions options, RideCaptureConfig config) => new()
    {
        Pm3ClientPath = options.Pm3ClientPath,
        DevicePort = options.DevicePort,
        DefaultCommandTimeout = options.TimeoutSeconds.HasValue ? TimeSpan.FromSeconds(options.TimeoutSeconds.Value) : TimeSpan.FromSeconds(15),
        WorkingDirectory = config.ProxmarkDumpSearchDirectory
    };

    private static CommandLineOptions ParseOptions(string[] args)
    {
        var options = new CommandLineOptions();
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--config" when i + 1 < args.Length:
                    options.ConfigPath = args[++i];
                    break;
                case "--pm3-path" when i + 1 < args.Length:
                    options.Pm3ClientPath = args[++i];
                    break;
                case "--port" when i + 1 < args.Length:
                    options.DevicePort = args[++i];
                    break;
                case "--timeout" when i + 1 < args.Length && int.TryParse(args[++i], out var seconds) && seconds > 0:
                    options.TimeoutSeconds = seconds;
                    break;
                default:
                    options.CommandArgs.Add(args[i]);
                    break;
            }
        }

        return options;
    }

    private sealed class CommandLineOptions
    {
        public string ConfigPath { get; set; } = "ride-capture-config.json";
        public string? Pm3ClientPath { get; set; }
        public string? DevicePort { get; set; }
        public int? TimeoutSeconds { get; set; }
        public List<string> CommandArgs { get; } = [];
    }
}
