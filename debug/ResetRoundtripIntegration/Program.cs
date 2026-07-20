using Pm3UsbApi;
using Pm3UsbApi.Diagnostics;
using RidesCli;
using Tokens;

Pm3DiagnosticLog.EnsureInitialized();
Console.WriteLine($"PM3 logs: {Pm3DiagnosticLog.Current.BaseDirectory}");
Console.WriteLine($"PM3 session log: {Pm3DiagnosticLog.Current.SessionLogPath}");
Console.WriteLine($"PM3 errors log: {Pm3DiagnosticLog.Current.ErrorsLogPath}");

var options = new Pm3Options { ExecutorKind = Pm3Options.ReadExecutorKindFromEnvironment() };
if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PM3_DEVICE_PORT")))
    options = options with { DevicePort = Environment.GetEnvironmentVariable("PM3_DEVICE_PORT") };

await using var pm3 = new Pm3(options);
Console.WriteLine($"Connecting (executor={options.ExecutorKind})...");
await pm3.ConnectAsync();
Console.WriteLine("Connected.");

var handler = new RidesCommandHandler(
    new Pm3RidesApiAdapter(pm3),
    new ConsoleRidesOutput(),
    new RidesConfig(),
    new AlwaysYesInput());

Console.WriteLine("\n== Baseline read ==");
handler.Execute(["read"]);

Console.WriteLine("\n== Reset mercury -> venus ==");
handler.Execute(["reset", "--sequence", "venus"]);
handler.Execute(["read"]);
AssertFinal(pm3, TokenIdentityProfiles.Venus, "venus");

Console.WriteLine("\n== Reset venus -> mercury ==");
handler.Execute(["reset", "--sequence", "mercury"]);
handler.Execute(["read"]);
AssertFinal(pm3, TokenIdentityProfiles.Mercury, "mercury");

Console.WriteLine("\nRoundtrip reset integration succeeded.");

static void AssertFinal(Pm3 pm3, TokenIdentityProfile profile, string name)
{
    var reset = ResetPage0BlocksLoader.Load(profile);
    var zero = profile.RideSequence.Encode(0);
    reset[5] = zero;
    reset[6] = zero;

    for (uint block = 1; block <= 6; block++)
    {
        var actual = pm3.ReadPage0BlockAsync(block).GetAwaiter().GetResult();
        var expected = reset[(int)block].ToHex();
        Console.WriteLine($"ASSERT {name} block={block} expected={expected} actual={actual}");
        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"{name} block {block} mismatch: expected {expected}, got {actual}");
    }
}

sealed class AlwaysYesInput : IRidesInput
{
    public string? ReadLine() => "y";
}
