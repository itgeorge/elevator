using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace RidesBridge;

public static class BridgeApplication
{
    internal const string AuthenticatedTokenItem = "RidesBridge.AuthenticatedToken";

    public static IServiceCollection AddRidesBridge(
        this IServiceCollection services,
        BridgeOptions options,
        IBridgePm3Device? device = null,
        IBonjourPublisher? publisher = null)
    {
        options.Validate();
        services.AddSingleton(options);
        services.AddSingleton<PairingCodeService>(_ => new PairingCodeService(options.PairingLifetime));
        services.AddSingleton<FilePairedClientStore>(_ => new FilePairedClientStore(options.EffectivePairedClientsPath));
        services.AddSingleton<IPairedClientStore>(serviceProvider => serviceProvider.GetRequiredService<FilePairedClientStore>());
        services.AddSingleton<IPairedClientRelocationStore>(serviceProvider => serviceProvider.GetRequiredService<FilePairedClientStore>());
        services.AddSingleton<BridgeIdentityService>(_ => new BridgeIdentityService(options.EffectiveBridgeIdentityPath));
        services.AddSingleton<BridgeOperationGate>(_ => new BridgeOperationGate(options.OperationWaitTimeout));
        if (device is null)
            services.AddSingleton<IBridgePm3Device, Pm3BridgeDeviceAdapter>();
        else
            services.AddSingleton(device);
        if (publisher is null)
            services.AddSingleton<IBonjourPublisher, HaukcodeBonjourPublisher>();
        else
            services.AddSingleton(publisher);
        services.AddSingleton<Page0ConditionalWriter>(serviceProvider =>
            new Page0ConditionalWriter(
                serviceProvider.GetRequiredService<IBridgePm3Device>(),
                serviceProvider.GetRequiredService<BridgeOptions>().HardwareRecoveryTimeout));
        services.AddSingleton<BridgeLifecycleService>(serviceProvider =>
        {
            // Resolve identity before the device so an invalid persistent identity cannot
            // leave a constructed hardware adapter behind during host startup.
            var options = serviceProvider.GetRequiredService<BridgeOptions>();
            var identity = serviceProvider.GetRequiredService<BridgeIdentityService>();
            var publisher = serviceProvider.GetRequiredService<IBonjourPublisher>();
            var gate = serviceProvider.GetRequiredService<BridgeOperationGate>();
            var device = serviceProvider.GetRequiredService<IBridgePm3Device>();
            var logger = serviceProvider.GetRequiredService<ILogger<BridgeLifecycleService>>();
            return new BridgeLifecycleService(device, gate, options, identity, publisher, logger);
        });
        services.AddSingleton<IHostedService>(serviceProvider => serviceProvider.GetRequiredService<BridgeLifecycleService>());
        return services;
    }

    public static void MapRidesBridge(this WebApplication app)
    {
        // Log only method/path/status. Request headers and bodies are intentionally never logged;
        // this keeps pairing PINs and bearer tokens out of structured request logs.
        var requestLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("RidesBridge.Http");
        app.Use(async (context, next) =>
        {
            try
            {
                await next().ConfigureAwait(false);
            }
            finally
            {
                requestLogger.LogInformation(
                    "HTTP {Method} {Path} completed with status {StatusCode}",
                    context.Request.Method,
                    context.Request.Path,
                    context.Response.StatusCode);
            }
        });

        app.Use(async (context, next) =>
        {
            if (RequiresBearer(context.Request.Path))
            {
                var token = ReadBearerToken(context.Request.Headers.Authorization.ToString());
                var store = context.RequestServices.GetRequiredService<IPairedClientStore>();
                if (token is null || !store.IsValid(token))
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    context.Response.Headers.WWWAuthenticate = "Bearer";
                    return;
                }
                context.Items[AuthenticatedTokenItem] = token;
            }
            await next().ConfigureAwait(false);
        });

        app.MapGet("/api/v1/health", () => Results.Ok(new HealthResponse("ok", BridgeOptions.ApiVersion, BridgeOptions.BridgeVersion)));

        // Authenticated, no-hardware liveness check for moving a saved bearer to a new address.
        app.MapGet("/api/v1/pair/status", () => Results.Ok(new PairStatusResponse(BridgeOptions.ApiVersion, true)));

        // Deliberately not included in RequiresBearer: the locator is derived from the
        // bearer, while the response proves that this candidate has its verifier. The
        // Authorization header is ignored by this endpoint.
        app.MapPost("/api/v1/pair/proof", async (
            HttpContext context,
            IPairedClientRelocationStore store,
            BridgeIdentityService identity,
            BridgeOptions options) =>
        {
            var request = await ReadPairProofRequestAsync(context.Request, context.RequestAborted).ConfigureAwait(false);
            if (request is null
                || !PairRelocationProof.TryDecodeUpperHex(request.Locator, PairRelocationProof.DigestBytes, out _)
                || !PairRelocationProof.TryDecodeUpperHex(request.Nonce, PairRelocationProof.DigestBytes, out var nonceBytes))
                return InvalidPairProof();

            Uri canonicalUrl;
            try
            {
                canonicalUrl = BonjourServiceDescriptor.CanonicalPrivateHttpUrl(new Uri(request.Url!, UriKind.Absolute));
            }
            catch (Exception ex) when (ex is UriFormatException or ArgumentException or BridgeConfigurationException)
            {
                return InvalidPairProof();
            }

            // Membership is evaluated from the same bind/interface semantics used for
            // Bonjour publication, not from public TXT supplied by the caller.
            IReadOnlyList<Uri> reportedUrls;
            try { reportedUrls = options.GetReportedUrls(); }
            catch (BridgeConfigurationException) { return InvalidPairProof(); }
            var urlIsReported = reportedUrls.Any(url => string.Equals(
                url.AbsoluteUri, canonicalUrl.AbsoluteUri, StringComparison.Ordinal));
            // Still perform the bounded verifier scan for a canonical but unreported URL;
            // discard its result below. This keeps URL mismatch and unknown/revoked locator
            // failures on the same generic path without ever issuing a proof for that URL.
            var proofFound = store.TryCreateProof(
                request.Locator!, nonceBytes, identity.Id, canonicalUrl.AbsoluteUri,
                BridgeOptions.ApiVersion, out var proof);
            if (!urlIsReported || !proofFound)
                return InvalidPairProof();

            return Results.Ok(new PairProofResponse(
                identity.Id.ToUpperInvariant(), BridgeOptions.ApiVersion, request.Nonce!, proof));
        });

        app.MapPost("/api/v1/pair", async (PairRequest request, PairingCodeService pairing, IPairedClientStore store, CancellationToken ct) =>
        {
            if (request.Pin is null || request.Pin.Length != 6 || request.Pin.Any(c => c is < '0' or > '9'))
                return Results.BadRequest(new BridgeErrorResponse("invalid_pairing_request", "PIN must contain exactly six digits."));
            if (!pairing.TryRedeem(request.Pin))
                return Results.Unauthorized();

            var token = CreateBearerToken();
            await store.AddAsync(token, ct).ConfigureAwait(false);
            return Results.Ok(new PairResponse(token));
        });

        app.MapPost("/api/v1/pair/revoke", async (HttpContext context, IPairedClientStore store, CancellationToken ct) =>
        {
            var token = (string)context.Items[AuthenticatedTokenItem]!;
            await store.RevokeAsync(token, ct).ConfigureAwait(false);
            return Results.NoContent();
        });

        app.MapGet("/api/v1/hardware/page0/block5", async (
            IBridgePm3Device device,
            BridgeOperationGate gate,
            BridgeOptions options,
            HttpContext context) =>
        {
            try
            {
                var value = await gate.ExecuteAsync(
                    operationCt => device.ReadPage0Block5Async(operationCt),
                    context.RequestAborted,
                    waitTimeout: options.OperationWaitTimeout,
                    operationTimeout: options.HardwareExecutionTimeout).ConfigureAwait(false);
                if (!IsBlockHex(value))
                    throw new BridgeHardwareException(BridgeHardwareError.MalformedResponse, "PM3 returned a malformed block response.");
                return Results.Ok(new BlockReadResponse(5, value.ToUpperInvariant()));
            }
            catch (BridgeHardwareException ex)
            {
                return HardwareError(ex.Error);
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            {
                return Results.StatusCode(StatusCodes.Status499ClientClosedRequest);
            }
            catch (IOException)
            {
                return HardwareError(BridgeHardwareError.Unavailable);
            }
            catch (UnauthorizedAccessException)
            {
                return HardwareError(BridgeHardwareError.Unavailable);
            }
            catch (ObjectDisposedException)
            {
                return HardwareError(BridgeHardwareError.Unavailable);
            }
            catch (InvalidOperationException)
            {
                return HardwareError(BridgeHardwareError.Unavailable);
            }
        });

        app.MapGet("/api/v1/hardware/page0/scan", async (
            IBridgePm3Device device,
            BridgeOperationGate gate,
            BridgeOptions options,
            HttpContext context) =>
        {
            try
            {
                var scan = await gate.ExecuteAsync(
                    operationCt => device.ScanPage0Async(operationCt),
                    context.RequestAborted,
                    waitTimeout: options.OperationWaitTimeout,
                    operationTimeout: options.HardwareExecutionTimeout).ConfigureAwait(false);
                if (!IsBlockHex(scan.Block4Hex) || !IsBlockHex(scan.Block5Hex) || !IsBlockHex(scan.Block6Hex))
                    throw new BridgeHardwareException(BridgeHardwareError.MalformedResponse, "PM3 returned a malformed block response.");
                return Results.Ok(new Page0ScanResponse(
                    BridgeOptions.ApiVersion,
                    scan.Block4Hex.ToUpperInvariant(),
                    scan.Block5Hex.ToUpperInvariant(),
                    scan.Block6Hex.ToUpperInvariant(),
                    scan.SignalMillivolts));
            }
            catch (BridgeHardwareException ex)
            {
                return HardwareError(ex.Error);
            }
            catch (OperationCanceledException)
            {
                return Results.StatusCode(StatusCodes.Status499ClientClosedRequest);
            }
            catch (IOException)
            {
                return HardwareError(BridgeHardwareError.Unavailable);
            }
            catch (UnauthorizedAccessException)
            {
                return HardwareError(BridgeHardwareError.Unavailable);
            }
            catch (ObjectDisposedException)
            {
                return HardwareError(BridgeHardwareError.Unavailable);
            }
            catch (InvalidOperationException)
            {
                return HardwareError(BridgeHardwareError.Unavailable);
            }
        });

        app.MapGet("/api/v1/hardware/page0/missing", async (
            IBridgePm3Device device,
            BridgeOperationGate gate,
            BridgeOptions options,
            HttpContext context) =>
        {
            try
            {
                var blocks = await gate.ExecuteAsync(
                    operationCt => device.ReadPage0MissingBlocksAsync(operationCt),
                    context.RequestAborted,
                    waitTimeout: options.OperationWaitTimeout,
                    operationTimeout: options.HardwareExecutionTimeout).ConfigureAwait(false);
                if (!Page0MissingBlocks.IsValidResponse(blocks))
                    throw new BridgeHardwareException(BridgeHardwareError.MalformedResponse, "PM3 returned a malformed missing-block response.");
                return Results.Ok(new Page0MissingBlocksResponse(
                    BridgeOptions.ApiVersion,
                    blocks.Select(block => new Page0BlockReadResult(block.Block, block.Value.ToUpperInvariant())).ToArray()));
            }
            catch (BridgeHardwareException ex)
            {
                return HardwareError(ex.Error);
            }
            catch (OperationCanceledException)
            {
                return Results.StatusCode(StatusCodes.Status499ClientClosedRequest);
            }
            catch (IOException)
            {
                return HardwareError(BridgeHardwareError.Unavailable);
            }
            catch (UnauthorizedAccessException)
            {
                return HardwareError(BridgeHardwareError.Unavailable);
            }
            catch (ObjectDisposedException)
            {
                return HardwareError(BridgeHardwareError.Unavailable);
            }
            catch (InvalidOperationException)
            {
                return HardwareError(BridgeHardwareError.Unavailable);
            }
        });

        app.MapGet("/api/v1/hardware/page0/blocks1to6", async (
            IBridgePm3Device device,
            BridgeOperationGate gate,
            BridgeOptions options,
            HttpContext context) =>
        {
            try
            {
                var blocks = await gate.ExecuteAsync(
                    operationCt => device.ReadPage0Blocks1To6Async(operationCt),
                    context.RequestAborted,
                    waitTimeout: options.OperationWaitTimeout,
                    operationTimeout: options.HardwareExecutionTimeout).ConfigureAwait(false);
                if (!Page0Blocks1To6.IsValidResponse(blocks))
                    throw new BridgeHardwareException(BridgeHardwareError.MalformedResponse, "PM3 returned a malformed blocks 1..6 response.");
                return Results.Ok(new Page0Blocks1To6Response(
                    BridgeOptions.ApiVersion,
                    blocks.Select(block => new Page0BlockReadResult(block.Block, block.Value.ToUpperInvariant())).ToArray()));
            }
            catch (BridgeHardwareException ex)
            {
                return HardwareError(ex.Error);
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            {
                return Results.StatusCode(StatusCodes.Status499ClientClosedRequest);
            }
            catch (IOException)
            {
                return HardwareError(BridgeHardwareError.Unavailable);
            }
            catch (UnauthorizedAccessException)
            {
                return HardwareError(BridgeHardwareError.Unavailable);
            }
            catch (ObjectDisposedException)
            {
                return HardwareError(BridgeHardwareError.Unavailable);
            }
            catch (InvalidOperationException)
            {
                return HardwareError(BridgeHardwareError.Unavailable);
            }
        });

        app.MapGet("/api/v1/hardware/page0/mirrors", async (
            IBridgePm3Device device,
            BridgeOperationGate gate,
            BridgeOptions options,
            HttpContext context) =>
        {
            try
            {
                var mirrors = await gate.ExecuteAsync(
                    operationCt => device.ReadPage0MirrorAsync(operationCt),
                    context.RequestAborted,
                    waitTimeout: options.OperationWaitTimeout,
                    operationTimeout: options.HardwareExecutionTimeout).ConfigureAwait(false);
                if (!IsBlockHex(mirrors.Block5Hex) || !IsBlockHex(mirrors.Block6Hex))
                    throw new BridgeHardwareException(BridgeHardwareError.MalformedResponse, "PM3 returned a malformed block response.");
                return Results.Ok(new Page0MirrorReadResponse(
                    BridgeOptions.ApiVersion,
                    mirrors.Block5Hex.ToUpperInvariant(),
                    mirrors.Block6Hex.ToUpperInvariant()));
            }
            catch (BridgeHardwareException ex)
            {
                return HardwareError(ex.Error);
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            {
                return Results.StatusCode(StatusCodes.Status499ClientClosedRequest);
            }
            catch (IOException)
            {
                return HardwareError(BridgeHardwareError.Unavailable);
            }
            catch (UnauthorizedAccessException)
            {
                return HardwareError(BridgeHardwareError.Unavailable);
            }
            catch (ObjectDisposedException)
            {
                return HardwareError(BridgeHardwareError.Unavailable);
            }
            catch (InvalidOperationException)
            {
                return HardwareError(BridgeHardwareError.Unavailable);
            }
        });

        app.MapPost("/api/v1/hardware/page0/mutations", async (
            Page0MutationRequest? request,
            Page0ConditionalWriter writer,
            BridgeOperationGate gate,
            BridgeOptions options,
            HttpContext context) =>
        {
            if (!Page0MutationValidator.TryValidate(request, out _, out var validationError))
                return Results.BadRequest(validationError);

            try
            {
                // The request token is used only while waiting for the gate. Once acquired,
                // the server owns the operation so disconnect cannot skip verification/rollback.
                var result = await gate.ExecuteDetachedAsync(
                    operationCt => writer.ExecuteAsync(request!, operationCt),
                    context.RequestAborted,
                    waitTimeout: options.OperationWaitTimeout,
                    operationTimeout: options.HardwareExecutionTimeout).ConfigureAwait(false);
                return Results.Ok(result);
            }
            catch (Page0MutationValidationException ex)
            {
                return Results.BadRequest(ex.Error);
            }
            catch (BridgeHardwareException ex)
            {
                return HardwareError(ex.Error);
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            {
                return Results.StatusCode(StatusCodes.Status499ClientClosedRequest);
            }
            catch (IOException)
            {
                return HardwareError(BridgeHardwareError.Unavailable);
            }
            catch (UnauthorizedAccessException)
            {
                return HardwareError(BridgeHardwareError.Unavailable);
            }
            catch (ObjectDisposedException)
            {
                return HardwareError(BridgeHardwareError.Unavailable);
            }
            catch (InvalidOperationException)
            {
                return HardwareError(BridgeHardwareError.Unavailable);
            }
        });
    }

    private const int MaximumPairProofBodyBytes = 4096;

    private static async Task<PairProofRequest?> ReadPairProofRequestAsync(HttpRequest request, CancellationToken ct)
    {
        if (request.ContentLength is > MaximumPairProofBodyBytes)
            return null;

        using var body = new MemoryStream();
        var buffer = new byte[1024];
        while (true)
        {
            var read = await request.Body.ReadAsync(buffer.AsMemory(), ct).ConfigureAwait(false);
            if (read == 0) break;
            if (body.Length + read > MaximumPairProofBodyBytes)
                return null;
            await body.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
        }

        try
        {
            using var document = JsonDocument.Parse(body.ToArray(), new JsonDocumentOptions { MaxDepth = 4 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return null;

            string? locator = null;
            string? nonce = null;
            string? url = null;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in root.EnumerateObject())
            {
                if (!seen.Add(property.Name) || property.Value.ValueKind != JsonValueKind.String)
                    return null;
                var value = property.Value.GetString();
                switch (property.Name)
                {
                    case "locator": locator = value; break;
                    case "nonce": nonce = value; break;
                    case "url": url = value; break;
                    default: return null;
                }
            }

            return seen.Count == 3 && locator is not null && nonce is not null && url is not null
                ? new PairProofRequest(locator, nonce, url)
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IResult InvalidPairProof() =>
        Results.Json(new BridgeErrorResponse("invalid_pairing_proof", "Pairing proof request is invalid."), statusCode: StatusCodes.Status400BadRequest);

    public static string? ReadBearerToken(string? authorization)
    {
        if (string.IsNullOrWhiteSpace(authorization)) return null;
        const string prefix = "Bearer ";
        if (!authorization.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
        var token = authorization[prefix.Length..].Trim();
        return token.Length == 0 || token.Any(char.IsWhiteSpace) ? null : token;
    }

    private static bool RequiresBearer(PathString path) =>
        path.StartsWithSegments("/api/v1/hardware")
        || path.StartsWithSegments("/api/v1/pair/revoke")
        || path.StartsWithSegments("/api/v1/pair/status");

    private static string CreateBearerToken()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    private static IResult HardwareError(BridgeHardwareError error) => error switch
    {
        BridgeHardwareError.Unavailable => Results.Json(new BridgeErrorResponse("pm3_unavailable", "Proxmark3 is unavailable."), statusCode: 503),
        BridgeHardwareError.NoChip => Results.Json(new BridgeErrorResponse("no_chip", "No supported T55xx chip is present."), statusCode: 409),
        BridgeHardwareError.Timeout => Results.Json(new BridgeErrorResponse("pm3_timeout", "Proxmark3 operation timed out."), statusCode: 504),
        BridgeHardwareError.Busy => Results.Json(new BridgeErrorResponse("bridge_busy", "The bridge is busy with another hardware operation."), statusCode: 503),
        BridgeHardwareError.MalformedResponse => Results.Json(new BridgeErrorResponse("malformed_device_response", "The device returned a malformed response."), statusCode: 502),
        BridgeHardwareError.TuneFailed => Results.Json(new BridgeErrorResponse("lf_tune_failed", "LF tune did not return a usable signal measurement."), statusCode: 503),
        BridgeHardwareError.ReadFailed => Results.Json(new BridgeErrorResponse("page0_read_failed", "Page-0 block read failed."), statusCode: 502),
        _ => Results.Json(new BridgeErrorResponse("pm3_unavailable", "Proxmark3 is unavailable."), statusCode: 503),
    };

    private static bool IsBlockHex(string? value) => value is not null && value.Length == 8 && value.All(Uri.IsHexDigit);
}
