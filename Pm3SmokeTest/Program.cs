// Phase 5 smoke test: verifies Pm3 high-level API against real hardware.
// Run: dotnet run --project Pm3SmokeTest
// With port: dotnet run --project Pm3SmokeTest -- --port COM5
// With exe path: dotnet run --project Pm3SmokeTest -- "C:\path\to\proxmark3.exe"
// Both: dotnet run --project Pm3SmokeTest -- --port COM5 "C:\path\to\proxmark3.exe"

using Pm3UsbApi;

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
    WorkingDirectory = Pm3Options.DevRunsDirectoryName, // isolate output files for dev runs
};

Console.WriteLine("Pm3 smoke test (Phase 5)");
Console.WriteLine("Testing: Connect -> Detect -> Dump -> Read -> Tune -> Disconnect");
Console.WriteLine();

try
{
    await using var pm3 = new Pm3(options);

    Console.WriteLine("Connecting...");
    await pm3.ConnectAsync();
    Console.WriteLine("Connected.");
    Console.WriteLine();

    // Ensure T55 session (optional pre-check; read/dump chain detect internally)
    try
    {
        Console.WriteLine("Running EnsureT55SessionActiveAsync...");
        await pm3.EnsureT55SessionActiveAsync();
        Console.WriteLine("T55 chip detected.");
    }
    catch (Pm3CommandException)
    {
        Console.WriteLine("No T55 tag present (EnsureT55SessionActiveAsync). Continuing with other tests...");
    }
    Console.WriteLine();

    // Dump (chains detect internally)
    try
    {
        Console.WriteLine("Running DumpAsync...");
        var dump = await pm3.DumpAsync();
        Console.WriteLine("--- Dump (first 500 chars) ---");
        Console.WriteLine(dump.Length > 500 ? dump[..500] + "..." : dump);
        Console.WriteLine("--- End ---");
    }
    catch (Pm3CommandException ex)
    {
        Console.WriteLine($"Dump failed (tag may be absent): {ex.Message}");
    }
    Console.WriteLine();

    // Read block 0 (chains detect internally)
    try
    {
        Console.WriteLine("Running ReadPage0BlockAsync(0)...");
        var hex = await pm3.ReadPage0BlockAsync(0);
        Console.WriteLine($"Block 0: {hex}");
    }
    catch (Pm3CommandException ex)
    {
        Console.WriteLine($"Read block 0 failed: {ex.Message}");
    }
    Console.WriteLine();

    // LF tune
    try
    {
        Console.WriteLine("Running StartLfTuneAsync...");
        await pm3.StartLfTuneAsync();
        var peakMv = await pm3.GetLfTuneLastMilliVoltsAsync();
        Console.WriteLine($"Peak: {peakMv} mV");
    }
    catch (InvalidOperationException)
    {
        Console.WriteLine("Tune not run.");
    }
    catch (Pm3CommandException ex)
    {
        Console.WriteLine($"Tune failed: {ex.Message}");
    }
    Console.WriteLine();

    Console.WriteLine("Disconnecting...");
    await pm3.DisconnectAsync();
    Console.WriteLine("Disconnected.");

    Console.WriteLine();
    Console.WriteLine("SUCCESS: Pm3 high-level API smoke test completed.");
}
catch (Pm3ClientNotFoundException ex)
{
    Console.WriteLine($"ERROR: {ex.Message}");
    Environment.Exit(1);
}
catch (Pm3ConnectionException ex)
{
    Console.WriteLine($"ERROR: Connection failed - {ex.Message}");
    if (ex.CommandResult?.RawOutput is { } output)
        Console.WriteLine("Captured output:\n" + output[..Math.Min(500, output.Length)] + (output.Length > 500 ? "..." : ""));
    Environment.Exit(1);
}
catch (Pm3TimeoutException ex)
{
    Console.WriteLine($"ERROR: Timeout - {ex.Message}");
    Environment.Exit(1);
}
catch (Exception ex)
{
    Console.WriteLine($"ERROR: {ex.GetType().Name}: {ex.Message}");
    Environment.Exit(1);
}
