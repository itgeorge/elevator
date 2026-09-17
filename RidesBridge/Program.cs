using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using RidesBridge;

var builder = WebApplication.CreateBuilder(args);
var options = BridgeOptions.FromConfiguration(builder.Configuration).Validate();
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
using var pairingQrLifetime = CancellationTokenSource.CreateLinkedTokenSource(app.Lifetime.ApplicationStopping);
try
{
    var pairingCode = pairing.GetActiveCode() ?? pairing.IssueCode();
    pairingQrArtifacts = PairingQrArtifactLease.CreateForReportedUrls(
        options,
        pairingCode,
        bridgeIdentity.Id,
        clock: TimeProvider.System);
    BridgeTerminalDisplay.Write(options, pairing, bridgeIdentity.Id, Console.Out, artifactLease: pairingQrArtifacts);
    if (pairingQrArtifacts is not null)
        pairingQrCleanupTask = pairingQrArtifacts.RunAsync(pairingQrLifetime.Token);

    await app.RunAsync();
}
finally
{
    pairingQrLifetime.Cancel();
    if (pairingQrCleanupTask is not null)
        await pairingQrCleanupTask.ConfigureAwait(false);
    pairingQrArtifacts?.Dispose();
}
