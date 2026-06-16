namespace Pm3UsbApi.Tests.Integration;

internal static class IntegrationTestOptions
{
    public static Pm3Options Create(Pm3ExecutorKind executorKind, TimeSpan? commandTimeout = null)
    {
        var port = Environment.GetEnvironmentVariable("PM3_DEVICE_PORT");

        return new Pm3Options
        {
            ExecutorKind = executorKind,
            DevicePort = string.IsNullOrWhiteSpace(port) ? null : port.Trim(),
            AutoConnect = string.IsNullOrWhiteSpace(port) || port.Trim().Equals("auto", StringComparison.OrdinalIgnoreCase),
            DefaultCommandTimeout = commandTimeout ?? TimeSpan.FromSeconds(15),
            ConnectTimeout = TimeSpan.FromSeconds(20),
            WorkingDirectory = Pm3Options.DevRunsDirectoryName,
        };
    }
}
