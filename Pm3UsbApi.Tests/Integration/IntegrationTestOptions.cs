namespace Pm3UsbApi.Tests.Integration;

internal static class IntegrationTestOptions
{
    public static Pm3Options Create(TimeSpan? commandTimeout = null)
    {
        var port = Environment.GetEnvironmentVariable("PM3_DEVICE_PORT");

        return new Pm3Options
        {
            ExecutorKind = Pm3Options.ReadExecutorKindFromEnvironment(),
            DevicePort = string.IsNullOrWhiteSpace(port) ? null : port.Trim(),
            AutoConnect = string.IsNullOrWhiteSpace(port) || port.Trim().Equals("auto", StringComparison.OrdinalIgnoreCase),
            DefaultCommandTimeout = commandTimeout ?? TimeSpan.FromSeconds(15),
            ConnectTimeout = TimeSpan.FromSeconds(20),
            WorkingDirectory = Pm3Options.DevRunsDirectoryName,
        };
    }

    public static bool UsesNativeExecutor =>
        Pm3Options.ReadExecutorKindFromEnvironment() == Pm3ExecutorKind.Native;
}
