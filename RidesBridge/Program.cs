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
    Console.WriteLine("      Expose on wildcard IPv4, select the first bindable port from 5080..5179, and enable PM3 USB auto-discovery.");
    Console.WriteLine("  dotnet run --project RidesBridge/RidesBridge.csproj -- --fake-pm3");
    Console.WriteLine("      Same local-network bind as everyday, but inject a deterministic in-process fake PM3 (no USB).");
    Console.WriteLine($"      Profile: {FakePm3ProfileResolver.GetDisplayName(FakePm3Profile.Venus)} (default) via {FakePm3ProfileResolver.ConfigurationKey} or {FakePm3ProfileResolver.EnvironmentKey}; no-chip for empty-antenna simulation; tune-failed for LF tune failure; read-failed for page-0 scan read failure; unknown for undecodable mirrors (Concept A missing-dump smoke); pm3-unavailable for disconnected USB (503 pm3_unavailable).");
    Console.WriteLine($"      Seeded scan: block4 {FakePm3Device.SeedBlock4Hex}, mirrors {FakePm3Device.SeedBlock5Hex}/{FakePm3Device.SeedBlock6Hex} ({FakePm3Device.SeedSequenceName}, {FakePm3Device.SeedRidesRemaining} rides), signal {FakePm3Device.SeedSignalMillivolts} mV.");
    Console.WriteLine($"      Unknown-mirrors test seed: block5/block6 {FakePm3Device.UnknownSeedBlock5Hex}/{FakePm3Device.UnknownSeedBlock6Hex} via {nameof(FakePm3Device.CreateUnknownMirrorsSeeded)}().");
    Console.WriteLine($"      Venus mirrors-only reset seed: default {nameof(FakePm3Device.CreateSeeded)}(); identity mismatch via {nameof(FakePm3Device.CreateVenusIdentityMismatchSeeded)}().");
    Console.WriteLine("  --help  Show this help.");
    Console.WriteLine("Everyday and fake-pm3 modes expose the HTTP bridge to private-network interfaces; use them only on a trusted local network.");
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
        : launch.FakePm3
            ? FakePm3LaunchMode.CreateOptions(
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

FakePm3Device? fakePm3Device = null;
FakePm3Profile? fakePm3Profile = null;
if (launch.FakePm3)
{
    try
    {
        fakePm3Profile = FakePm3ProfileResolver.Resolve(builder.Configuration);
        fakePm3Device = FakePm3ProfileResolver.CreateDevice(fakePm3Profile.Value);
    }
    catch (BridgeConfigurationException ex)
    {
        Console.Error.WriteLine($"RidesBridge configuration error: {ex.Message}");
        return 2;
    }
}

builder.WebHost.UseUrls(options.BindUrl);
builder.Services.AddRidesBridge(options, fakePm3Device);

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
    if (fakePm3Profile is not null)
        Console.WriteLine($"Fake PM3 profile: {FakePm3ProfileResolver.GetDisplayName(fakePm3Profile.Value)}");
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
