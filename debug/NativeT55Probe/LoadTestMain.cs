using Pm3UsbApi;
using Pm3UsbApi.Tests.Integration;

namespace NativeT55Probe;

internal static class LoadTestMain
{
    private const string DefaultPort = "/dev/cu.usbmodem1201";

    public static async Task RunAsync(string[] args)
    {
        var port = GetArg(args, "--port") ?? DefaultPort;
        var timeout = TimeSpan.FromSeconds(12);

        await using var pm3 = new Pm3(new Pm3Options
        {
            ExecutorKind = Pm3ExecutorKind.Native,
            DevicePort = port,
            DefaultCommandTimeout = timeout,
            ConnectTimeout = timeout,
        });

        try
        {
            var resetPath = NativeRideLoadTestRunner.ResolveResetImagePath();
            var result = await NativeRideLoadTestRunner.RunAsync(pm3, resetPath);

            Console.WriteLine();
            Console.WriteLine(
                $"Load test complete: {result.OperationCount} operations, {result.ElapsedMilliseconds}ms total");

            if (result.FinalRides != NativeRideLoadTestRunner.TargetFinalRides)
            {
                Console.WriteLine(
                    $"ERROR: final rides {result.FinalRides}, expected {NativeRideLoadTestRunner.TargetFinalRides}");
                Environment.ExitCode = 1;
            }
            else
            {
                Console.WriteLine($"OK: token left at {NativeRideLoadTestRunner.TargetFinalRides} rides");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR: {ex.GetType().Name}: {ex.Message}");
            Environment.ExitCode = 1;
        }
    }

    private static string? GetArg(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
