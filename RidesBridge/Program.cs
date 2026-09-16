using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using RidesBridge;

var builder = WebApplication.CreateBuilder(args);
var options = BridgeOptions.FromConfiguration(builder.Configuration).Validate();
builder.WebHost.UseUrls(options.BindUrl);
builder.Services.AddRidesBridge(options);

var app = builder.Build();
app.MapRidesBridge();

// PairingCodeService is the intentional operator-facing issuance API. Write this only to
// the terminal; never send the PIN or its bearer token counterpart through ILogger.
var pairing = app.Services.GetRequiredService<PairingCodeService>();
var pairingCode = pairing.IssueCode();
Console.WriteLine($"RidesBridge API {BridgeOptions.ApiVersion} listening.");
Console.WriteLine($"Pairing PIN: {pairingCode.Value} (expires {pairingCode.ExpiresAt:O})");
foreach (var uri in options.GetReportedUrls())
    Console.WriteLine($"Reachable private URL: {uri}");

await app.RunAsync();
