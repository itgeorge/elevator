using Pm3UsbApi;
using Tokens;

namespace Pm3Cli;

class Pm3CliProgram
{
    private static Pm3? _pm3;
    private static Pm3Options _options = new();
    private static bool _connected;

    static async Task<int> Main(string[] args)
    {
        ParseArgs(args);

        Console.WriteLine("Pm3Cli - Interactive Proxmark3 API tool");
        Console.WriteLine("Type 'help' for available commands.");
        Console.WriteLine();

        using var cts = new CancellationTokenSource();

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        try
        {
            await RunInteractiveLoopAsync(cts.Token);
        }
        finally
        {
            if (_pm3 is not null)
            {
                await _pm3.DisposeAsync();
            }
        }

        return 0;
    }

    private static void ParseArgs(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--pm3-path" when i + 1 < args.Length:
                    _options = _options with { Pm3ClientPath = args[++i] };
                    break;
                case "--port" when i + 1 < args.Length:
                    _options = _options with { DevicePort = args[++i] };
                    break;
                case "--timeout" when i + 1 < args.Length:
                    if (int.TryParse(args[++i], out var sec) && sec > 0)
                    {
                        _options = _options with { DefaultCommandTimeout = TimeSpan.FromSeconds(sec) };
                    }
                    break;
            }
        }
    }

    private static async Task RunInteractiveLoopAsync(CancellationToken ct)
    {
        _pm3 = new Pm3(_options);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                Console.Write("pm3api> ");
                var line = Console.ReadLine();
                if (line is null)
                    break;

                var input = line.Trim();
                if (string.IsNullOrEmpty(input))
                    continue;

                var parts = SplitCommand(input);
                var cmd = parts[0].ToLowerInvariant();
                var cmdArgs = parts.Skip(1).ToList();

                switch (cmd)
                {
                    case "connect":
                        await RunConnectAsync(ct);
                        break;
                    case "disconnect":
                        await RunDisconnectAsync(ct);
                        break;
                    case "status":
                        await RunStatusAsync(ct);
                        break;
                    case "detect":
                        await RunDetectAsync(ct);
                        break;
                    case "tune":
                        await RunTuneAsync(ct);
                        break;
                    case "read":
                        await RunReadAsync(cmdArgs, ct);
                        break;
                    case "write":
                        await RunWriteAsync(cmdArgs, ct);
                        break;
                    case "dump":
                        await RunDumpAsync(ct);
                        break;
                    case "raw":
                        await RunRawAsync(cmdArgs, ct);
                        break;
                    case "config":
                        RunConfig(cmdArgs);
                        break;
                    case "help":
                        ShowHelp();
                        break;
                    case "exit":
                    case "quit":
                    case "q":
                        return;
                    default:
                        Console.WriteLine($"Unknown command: {cmd}. Type 'help' for available commands.");
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine();
                return;
            }
        }
    }

    private static bool RequiresConnection()
    {
        if (_connected) return true;
        Console.WriteLine("Not connected. Use 'connect' first.");
        return false;
    }

    private static string[] SplitCommand(string input)
    {
        var result = new List<string>();
        var current = "";
        var inQuotes = false;
        var quoteChar = '\0';

        foreach (var c in input)
        {
            if (inQuotes)
            {
                if (c == quoteChar)
                {
                    inQuotes = false;
                }
                else
                {
                    current += c;
                }
            }
            else if (c == '"' || c == '\'')
            {
                inQuotes = true;
                quoteChar = c;
            }
            else if (char.IsWhiteSpace(c))
            {
                if (current.Length > 0)
                {
                    result.Add(current);
                    current = "";
                }
            }
            else
            {
                current += c;
            }
        }

        if (current.Length > 0)
            result.Add(current);

        return result.ToArray();
    }

    private static async Task RunConnectAsync(CancellationToken ct)
    {
        if (_pm3 is null) return;
        try
        {
            await _pm3.ConnectAsync(ct);
            _connected = true;
            Console.WriteLine("Connected to Proxmark3.");
        }
        catch (Pm3Exception ex)
        {
            _connected = false;
            HandlePm3Exception(ex);
        }
    }

    private static async Task RunDisconnectAsync(CancellationToken ct)
    {
        if (_pm3 is null) return;
        try
        {
            await _pm3.DisconnectAsync(ct);
            _pm3 = new Pm3(_options);
            _connected = false;
            Console.WriteLine("Disconnected.");
        }
        catch (Pm3Exception ex)
        {
            HandlePm3Exception(ex);
        }
    }

    private static Task RunStatusAsync(CancellationToken _)
    {
        Console.WriteLine(_connected ? "Connected." : "Not connected.");
        return Task.CompletedTask;
    }

    private static async Task RunDetectAsync(CancellationToken ct)
    {
        if (_pm3 is null || !RequiresConnection()) return;
        try
        {
            await _pm3.EnsureT55SessionActiveAsync(ct);
            Console.WriteLine("T55xx chip detected.");
        }
        catch (Pm3Exception ex)
        {
            HandlePm3Exception(ex);
        }
    }

    private static async Task RunTuneAsync(CancellationToken ct)
    {
        if (_pm3 is null || !RequiresConnection()) return;
        try
        {
            await _pm3.StartLfTuneAsync(ct);
            var mV = await _pm3.GetLfTuneLastMilliVoltsAsync(ct);
            Console.WriteLine($"Peak: {mV} mV");
        }
        catch (Pm3Exception ex)
        {
            HandlePm3Exception(ex);
        }
    }

    private static async Task RunReadAsync(IReadOnlyList<string> cmdArgs, CancellationToken ct)
    {
        if (_pm3 is null || !RequiresConnection()) return;
        if (cmdArgs.Count < 1)
        {
            Console.WriteLine("Usage: read <block>");
            return;
        }
        if (!uint.TryParse(cmdArgs[0], out var block) || block > 7)
        {
            Console.WriteLine("Block must be 0-7.");
            return;
        }
        try
        {
            var hex = await _pm3.ReadPage0BlockAsync(block, ct);
            Console.WriteLine($"Block {block}: {hex}");
        }
        catch (Pm3Exception ex)
        {
            HandlePm3Exception(ex);
        }
    }

    private static async Task RunWriteAsync(IReadOnlyList<string> cmdArgs, CancellationToken ct)
    {
        if (_pm3 is null || !RequiresConnection()) return;
        if (cmdArgs.Count < 2)
        {
            Console.WriteLine("Usage: write <block> <hex>");
            return;
        }
        if (!uint.TryParse(cmdArgs[0], out var block) || block > 7)
        {
            Console.WriteLine("Block must be 1-6 (block 0 and 7 are forbidden).");
            return;
        }
        try
        {
            var data = T55Block.FromHex(cmdArgs[1]);
            await _pm3.WritePage0BlockAsync(block, data, ct);
            Console.WriteLine($"Block {block} written.");
        }
        catch (ArgumentException ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
        catch (Pm3Exception ex)
        {
            HandlePm3Exception(ex);
        }
    }

    private static async Task RunDumpAsync(CancellationToken ct)
    {
        if (_pm3 is null || !RequiresConnection()) return;
        try
        {
            var output = await _pm3.DumpAsync(ct);
            Console.WriteLine(output);
        }
        catch (Pm3Exception ex)
        {
            HandlePm3Exception(ex);
        }
    }

    private static async Task RunRawAsync(IReadOnlyList<string> cmdArgs, CancellationToken ct)
    {
        if (_pm3 is null || !RequiresConnection()) return;
        if (cmdArgs.Count == 0)
        {
            Console.WriteLine("Usage: raw <pm3 command>");
            return;
        }
        var command = string.Join(" ", cmdArgs);
        try
        {
            var output = await _pm3.ExecuteRawCommandAsync(command, ct);
            Console.WriteLine(output);
        }
        catch (Pm3Exception ex)
        {
            HandlePm3Exception(ex);
        }
    }

    private static void RunConfig(IReadOnlyList<string> cmdArgs)
    {
        if (cmdArgs.Count == 0)
        {
            ShowConfig();
            return;
        }
        if (cmdArgs.Count < 2)
        {
            Console.WriteLine("Usage: config <key> <value>");
            Console.WriteLine("Keys: pm3-path, port, timeout, working-dir, transcript");
            return;
        }
        var key = cmdArgs[0].ToLowerInvariant();
        var value = string.Join(" ", cmdArgs.Skip(1));

        var updated = key switch
        {
            "pm3-path" or "pm3path" => _options with { Pm3ClientPath = string.IsNullOrWhiteSpace(value) ? null : value },
            "port" => _options with { DevicePort = string.IsNullOrWhiteSpace(value) ? null : value },
            "timeout" => int.TryParse(value, out var sec) && sec > 0
                ? _options with { DefaultCommandTimeout = TimeSpan.FromSeconds(sec) }
                : null,
            "working-dir" or "workingdir" => _options with { WorkingDirectory = string.IsNullOrWhiteSpace(value) ? null : value },
            "transcript" => value.ToLowerInvariant() switch
            {
                "on" or "true" or "1" => _options with { EnableTranscriptLogging = true },
                "off" or "false" or "0" => _options with { EnableTranscriptLogging = false },
                _ => null
            },
            _ => null
        };

        if (updated is null)
        {
            Console.WriteLine($"Unknown or invalid config key: {key}");
            return;
        }

        _options = updated;
        _pm3 = new Pm3(_options);
        _connected = false;
        Console.WriteLine($"Config updated. {key} = {(string.IsNullOrEmpty(value) ? "(auto)" : value)}");
    }

    private static void ShowConfig()
    {
        Console.WriteLine($"  pm3-path:     {_options.Pm3ClientPath ?? "(auto-detect)"}");
        Console.WriteLine($"  port:         {_options.DevicePort ?? "(auto-detect)"}");
        Console.WriteLine($"  timeout:      {(int)_options.DefaultCommandTimeout.TotalSeconds}s");
        Console.WriteLine($"  working-dir:  {_options.WorkingDirectory ?? "(current)"}");
        Console.WriteLine($"  transcript:   {(_options.EnableTranscriptLogging ? "on" : "off")}");
    }

    private static void ShowHelp()
    {
        Console.WriteLine("Commands:");
        Console.WriteLine("  connect              Connect to device");
        Console.WriteLine("  disconnect          Disconnect");
        Console.WriteLine("  status               Show connection status");
        Console.WriteLine("  detect               Run T55 detect");
        Console.WriteLine("  tune                 Run LF tune, show peak mV");
        Console.WriteLine("  read <block>         Read page 0 block (0-7)");
        Console.WriteLine("  write <block> <hex>  Write page 0 block (1-6)");
        Console.WriteLine("  dump                 Dump all blocks");
        Console.WriteLine("  raw <command>        Send raw pm3 command");
        Console.WriteLine("  config [key value]   Show or set options (pm3-path, port, timeout, etc.)");
        Console.WriteLine("  help                 Show this help");
        Console.WriteLine("  exit                 Quit");
    }

    private static void HandlePm3Exception(Pm3Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"Error: {ex.Message}");

        if (ex is Pm3ConnectionException)
        {
            Console.WriteLine("Check that the Proxmark3 is connected and the pm3 client path is correct.");
        }
        else if (ex is Pm3ClientNotFoundException)
        {
            Console.WriteLine("Install Proxmark3 client (e.g. ProxSpace on Windows) or set --pm3-path.");
        }
        else if (ex is Pm3TimeoutException)
        {
            Console.WriteLine("Try increasing --timeout or check device connection.");
        }

        if (ex.CommandResult?.RawOutput is { } raw && !string.IsNullOrWhiteSpace(raw))
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("--- pm3 output ---");
            Console.WriteLine(raw);
            Console.WriteLine("------------------");
        }

        Console.ResetColor();
    }
}
