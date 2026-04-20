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

        var pm3Options = new Pm3Options
        {
            Pm3ClientPath = options.Pm3ClientPath,
            DevicePort = options.DevicePort,
            DefaultCommandTimeout = options.TimeoutSeconds.HasValue ? TimeSpan.FromSeconds(options.TimeoutSeconds.Value) : TimeSpan.FromSeconds(15),
            WorkingDirectory = config.ProxmarkDumpSearchDirectory
        };

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
        var store = new CaptureCsvStore();
        var sequenceService = new CaptureSequenceService();

        Console.WriteLine("RideCaptureCli - token sequence capture");
        Console.WriteLine($"Config: {Path.GetFullPath(options.ConfigPath)}");
        Console.WriteLine($"CSV:    {paths.CsvPath}");
        Console.WriteLine("Commands: Enter=scan, zero=scan and anchor zero, exact <n>=scan and anchor exact count, help, exit");
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

            int? exactRideCount = null;
            if (!string.IsNullOrEmpty(command))
            {
                if (command.Equals("zero", StringComparison.OrdinalIgnoreCase))
                {
                    exactRideCount = 0;
                }
                else if (command.StartsWith("exact ", StringComparison.OrdinalIgnoreCase))
                {
                    var valueText = command[6..].Trim();
                    if (!int.TryParse(valueText, out var parsed) || parsed < 0)
                    {
                        ConsoleStatusWriter.WriteInfo("Invalid exact value. Use: exact <non-negative-integer>");
                        continue;
                    }

                    exactRideCount = parsed;
                }
                else
                {
                    ConsoleStatusWriter.WriteInfo("Unknown command. Use Enter, zero, exact <n>, help, or exit.");
                    continue;
                }
            }

            try
            {
                var scan = await scanner.ScanAsync();
                var existing = store.Load(paths.CsvPath);
                var result = sequenceService.ApplyScan(existing, scan, exactRideCount);
                store.Save(paths.CsvPath, result.Records);
                ConsoleStatusWriter.WriteCaptureResult(result.AddedRecord, result.AutoNormalized, result.ManualAnchorRideCount);
            }
            catch (Exception ex)
            {
                ConsoleStatusWriter.WriteError(ex.Message);
            }
        }

        return 0;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Commands:");
        Console.WriteLine("  <Enter>     Scan current token and append to CSV");
        Console.WriteLine("  zero        Scan current token and anchor the current sequence at zero rides");
        Console.WriteLine("  exact <n>   Scan current token and anchor the current sequence at exact ride count n");
        Console.WriteLine("  help        Show help");
        Console.WriteLine("  exit        Quit");
        Console.WriteLine();
    }

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
    }
}
