// Phase 2 manual smoke test: verifies Pm3ProcessExecutor can find the pm3 client and execute "hw version".
// Run: dotnet run --project Pm3SmokeTest
// With exe path: dotnet run --project Pm3SmokeTest -- "C:\Users\itgeorge\ProxSpace\pm3\proxmark3\client\proxmark3.exe"

using Pm3UsbApi;
using Pm3UsbApi.Execution;

var pm3Path = args.FirstOrDefault(a => !a.StartsWith("-"))?.Trim();

var options = new Pm3Options
{
    Pm3ClientPath = pm3Path,
    DefaultCommandTimeout = TimeSpan.FromSeconds(20),
};

Console.WriteLine("Pm3ProcessExecutor smoke test");
Console.WriteLine("Executing: hw version");
Console.WriteLine();

try
{
    await using var executor = new Pm3ProcessExecutor(options);
    var result = await executor.ExecuteAsync(["hw version"]);

    Console.WriteLine("--- Output ---");
    foreach (var line in result.OutputLines)
        Console.WriteLine(line);
    Console.WriteLine("--- End ---");
    Console.WriteLine($"Exit code: {result.ExitCode}");
    Console.WriteLine($"HasErrors: {result.HasErrors}");
    if (result.ErrorSummary is not null)
        Console.WriteLine($"ErrorSummary: {result.ErrorSummary}");
    Console.WriteLine();

    if (!result.HasErrors && result.RawOutput.Contains("Proxmark3", StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine("SUCCESS: Output contains Proxmark3 version info. Executor is working.");
    }
    else if (result.HasErrors)
    {
        Console.WriteLine("WARNING: Command reported errors. Check device connection and pm3 path.");
    }
    else
    {
        Console.WriteLine("INFO: Output received but may not contain expected version info.");
    }
}
catch (Pm3ClientNotFoundException ex)
{
    Console.WriteLine($"ERROR: {ex.Message}");
    Console.WriteLine("Set Pm3Options.Pm3ClientPath to proxmark3.exe (e.g. .../ProxSpace/pm3/proxmark3/client/proxmark3.exe)");
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
