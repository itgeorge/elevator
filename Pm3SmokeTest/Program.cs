// Phase 3 manual smoke test: verifies Pm3Session can connect, run lf t55 detect, and disconnect.
// Run: dotnet run --project Pm3SmokeTest
// With port: dotnet run --project Pm3SmokeTest -- --port COM5
// With exe path: dotnet run --project Pm3SmokeTest -- "C:\path\to\proxmark3.exe"
// Both: dotnet run --project Pm3SmokeTest -- --port COM5 "C:\path\to\proxmark3.exe"

using Pm3UsbApi;
using Pm3UsbApi.Execution;
using Pm3UsbApi.Session;

string? pm3Path = null;
string? devicePort = null;
for (var i = 0; i < args.Length; i++)
{
    if ((args[i] == "--port" || args[i] == "-p") && i + 1 < args.Length)
        devicePort = args[++i];
    else if (!args[i].StartsWith("-"))
        pm3Path = args[i].Trim();
}

var options = new Pm3Options
{
    Pm3ClientPath = string.IsNullOrEmpty(pm3Path) ? null : pm3Path,
    DevicePort = devicePort,
    DefaultCommandTimeout = TimeSpan.FromSeconds(20),
};

Console.WriteLine("Pm3Session smoke test (Phase 3)");
Console.WriteLine("Testing: Connect -> lf t55 detect -> Disconnect");
Console.WriteLine();

try
{
    var executor = new Pm3ProcessExecutor(options);
    await using var session = new Pm3Session(executor, options);

    // Connect (uses hw version internally)
    Console.WriteLine("Connecting...");
    await session.ConnectAsync();
    Console.WriteLine("Connected.");

    // Run lf t55 detect (chain detect + command; for detect, command IS detect)
    Console.WriteLine();
    Console.WriteLine("Executing: lf t55 detect (via ExecuteT55CommandAsync)");
    var result = await session.ExecuteT55CommandAsync("lf t55 detect");

    Console.WriteLine("--- Output ---");
    foreach (var line in result.OutputLines)
        Console.WriteLine(line);
    Console.WriteLine("--- End ---");
    Console.WriteLine($"Exit code: {result.ExitCode}");
    Console.WriteLine($"HasErrors: {result.HasErrors}");
    if (result.ErrorSummary is not null)
        Console.WriteLine($"ErrorSummary: {result.ErrorSummary}");
    Console.WriteLine();

    // Verify IsConnectedAsync
    var isConnected = await session.IsConnectedAsync();
    Console.WriteLine($"IsConnectedAsync: {isConnected}");

    // Disconnect
    Console.WriteLine();
    Console.WriteLine("Disconnecting...");
    await session.DisconnectAsync();
    Console.WriteLine("Disconnected.");

    if (!result.HasErrors || result.RawOutput.Contains("Chip Type", StringComparison.OrdinalIgnoreCase)
        || result.RawOutput.Contains("T55", StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine();
        Console.WriteLine("SUCCESS: Session layer works. lf t55 detect completed.");
        Console.WriteLine("Note: If no tag was present, output may show 'no chip detected' - that's expected.");
    }
    else if (result.HasErrors)
    {
        Console.WriteLine();
        Console.WriteLine("WARNING: Command reported errors. Check device connection and tag presence.");
    }
}
catch (Pm3ClientNotFoundException ex)
{
    Console.WriteLine($"ERROR: {ex.Message}");
    Console.WriteLine("Set Pm3Options.Pm3ClientPath to proxmark3.exe (e.g. .../ProxSpace/pm3/proxmark3/client/proxmark3.exe)");
    Environment.Exit(1);
}
catch (Pm3ConnectionException ex)
{
    Console.WriteLine($"ERROR: Connection failed - {ex.Message}");
    if (ex.CommandResult?.RawOutput is { } output)
        Console.WriteLine("Captured output:\n" + output);
    Environment.Exit(1);
}
catch (Pm3TimeoutException ex)
{
    Console.WriteLine($"ERROR: Timeout - {ex.Message}");
    if (ex.CommandResult?.RawOutput is { } output)
        Console.WriteLine("Captured output:\n" + output);
    Environment.Exit(1);
}
catch (Exception ex)
{
    Console.WriteLine($"ERROR: {ex.GetType().Name}: {ex.Message}");
    Environment.Exit(1);
}
