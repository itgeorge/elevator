using Microsoft.AspNetCore.Builder;
using RidesBridge;

BridgeLaunchOptions launch;
try
{
    launch = BridgeLaunchOptions.Parse(args);
}
catch (BridgeLaunchConfigurationException ex)
{
    Console.Error.WriteLine($"RidesBridge launch error: {ex.Message}");
    return 2;
}

if (launch.ShowHelp)
{
    Console.WriteLine("RidesBridge launch options:");
    Console.WriteLine("  dotnet run --project RidesBridge/RidesBridge.csproj -- --everyday");
    Console.WriteLine("      Expose on wildcard IPv4, select the first bindable port from 5080..5179, and enable PM3 auto-discovery.");
    Console.WriteLine("  --help  Show this help.");
    Console.WriteLine("Everyday mode exposes the HTTP bridge to private-network interfaces; use it only on a trusted local network.");
    return 0;
}

var builder = WebApplication.CreateBuilder(launch.AspNetCoreArguments.ToArray());
BridgeOptions options;
try
{
    options = launch.Everyday
        ? EverydayLaunchMode.CreateOptions(
            builder.Configuration,
            EverydayPortSelector.Select(new TcpPortAvailabilityProbe())).Validate()
        : BridgeOptions.FromConfiguration(builder.Configuration).Validate();
}
catch (BridgeConfigurationException ex)
{
    Console.Error.WriteLine($"RidesBridge configuration error: {ex.Message}");
    return 2;
}
catch (PortSelectionException ex)
{
    Console.Error.WriteLine($"RidesBridge port selection error: {ex.Message}");
    return 2;
}
builder.WebHost.UseUrls(options.BindUrl);
builder.Services.AddRidesBridge(options);

var app = builder.Build();
app.MapRidesBridge();

// PairingCodeService and BridgeIdentityService are intentional operator-facing APIs. Write
// pairing material only to the terminal QR/manual display; never send it through ILogger.
var pairing = app.Services.GetRequiredService<PairingCodeService>();
var bridgeIdentity = app.Services.GetRequiredService<BridgeIdentityService>();
PairingQrArtifactLease? pairingQrArtifacts = null;
Task? pairingQrCleanupTask = null;
var appStarted = false;
using var pairingQrLifetime = CancellationTokenSource.CreateLinkedTokenSource(app.Lifetime.ApplicationStopping);
try
{
    // Start Kestrel and the hosted lifecycle before displaying pairing material. A port race or
    // startup failure must not leave the operator with a QR code for a server that never listened.
    await app.StartAsync();
    appStarted = true;

    var pairingCode = pairing.GetActiveCode() ?? pairing.IssueCode();
    pairingQrArtifacts = PairingQrArtifactLease.CreateForReportedUrls(
        options,
        pairingCode,
        bridgeIdentity.Id,
        clock: TimeProvider.System);
    BridgeTerminalDisplay.Write(options, pairing, bridgeIdentity.Id, Console.Out, artifactLease: pairingQrArtifacts);
    if (pairingQrArtifacts is not null)
        pairingQrCleanupTask = pairingQrArtifacts.RunAsync(pairingQrLifetime.Token);

    await app.WaitForShutdownAsync();
}
finally
{
    pairingQrLifetime.Cancel();
    if (pairingQrCleanupTask is not null)
        await pairingQrCleanupTask.ConfigureAwait(false);
    pairingQrArtifacts?.Dispose();

    try
    {
        if (appStarted)
            await app.StopAsync();
    }
    finally
    {
        await app.DisposeAsync();
    }
}

return 0;
