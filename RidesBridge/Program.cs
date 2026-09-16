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
BridgeTerminalDisplay.Write(options, pairing, bridgeIdentity.Id, Console.Out);

await app.RunAsync();
