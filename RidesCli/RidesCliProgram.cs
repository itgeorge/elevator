using Pm3UsbApi;

namespace RidesCli;

class RidesCliProgram
{
    static async Task<int> Main(string[] args)
    {
        var config = new RidesConfig();
        var options = ParsePm3Options(args);
        var pm3 = new Pm3(options);
        var pm3Api = new Pm3RidesApiAdapter(pm3);
        var output = new ConsoleRidesOutput();
        var input = new ConsoleRidesInput();
        var handler = new RidesCommandHandler(pm3Api, output, config, input);

        Console.WriteLine("RidesCli - Elevator token ride management");
        Console.WriteLine("Type 'help' for commands. 'read' to load token from device.");
        Console.WriteLine();

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        try
        {
            await pm3.ConnectAsync(cts.Token);
        }
        catch (Pm3Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            Console.WriteLine("Connect failed. Commands needing the device will fail. Continuing...");
        }

        try
        {
            while (!cts.Token.IsCancellationRequested)
            {
                try
                {
                    Console.Write("rides> ");
                    var line = Console.ReadLine();
                    if (line is null) break;

                    var cmdArgs = SplitArgs(line.Trim());
                    if (cmdArgs.Length == 0) continue;

                    var shouldContinue = handler.Execute(cmdArgs);
                    if (!shouldContinue) break;
                }
                catch (OperationCanceledException)
                {
                    Console.WriteLine();
                    break;
                }
            }
        }
        finally
        {
            await pm3.DisposeAsync();
        }

        return 0;
    }

    static Pm3Options ParsePm3Options(string[] args)
    {
        var options = new Pm3Options();
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--pm3-path" when i + 1 < args.Length:
                    options = options with { Pm3ClientPath = args[++i] };
                    break;
                case "--port" when i + 1 < args.Length:
                    options = options with { DevicePort = args[++i] };
                    break;
                case "--timeout" when i + 1 < args.Length:
                    if (int.TryParse(args[++i], out var sec) && sec > 0)
                        options = options with { DefaultCommandTimeout = TimeSpan.FromSeconds(sec) };
                    break;
            }
        }
        return options;
    }

    static string[] SplitArgs(string input)
    {
        var result = new List<string>();
        var sb = new System.Text.StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < input.Length; i++)
        {
            var c = input[i];
            if (c == '"') { inQuotes = !inQuotes; continue; }
            if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (sb.Length > 0) { result.Add(sb.ToString()); sb.Clear(); }
            }
            else sb.Append(c);
        }
        if (sb.Length > 0) result.Add(sb.ToString());
        return result.ToArray();
    }
}
