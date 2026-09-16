using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace RidesBridge;

public static class BridgeApplication
{
    internal const string AuthenticatedTokenItem = "RidesBridge.AuthenticatedToken";

    public static IServiceCollection AddRidesBridge(
        this IServiceCollection services,
        BridgeOptions options,
        IBridgePm3Device? device = null)
    {
        options.Validate();
        services.AddSingleton(options);
        services.AddSingleton<PairingCodeService>(_ => new PairingCodeService(options.PairingLifetime));
        services.AddSingleton<IPairedClientStore>(_ => new FilePairedClientStore(options.EffectivePairedClientsPath));
        services.AddSingleton<BridgeIdentityService>(_ => new BridgeIdentityService(options.EffectiveBridgeIdentityPath));
        services.AddSingleton<BridgeOperationGate>(_ => new BridgeOperationGate(options.OperationWaitTimeout));
        if (device is null)
            services.AddSingleton<IBridgePm3Device, Pm3BridgeDeviceAdapter>();
        else
            services.AddSingleton(device);
        services.AddSingleton<MercuryConditionalWriter>(serviceProvider =>
            new MercuryConditionalWriter(
                serviceProvider.GetRequiredService<IBridgePm3Device>(),
                serviceProvider.GetRequiredService<BridgeOptions>().HardwareRecoveryTimeout));
        services.AddHostedService<BridgeLifecycleService>();
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

        app.MapGet("/api/v1/hardware/mercury/mirrors", async (
            IBridgePm3Device device,
            BridgeOperationGate gate,
            BridgeOptions options,
            HttpContext context) =>
        {
            try
            {
                var mirrors = await gate.ExecuteAsync(
                    operationCt => device.ReadMercuryMirrorAsync(operationCt),
                    context.RequestAborted,
                    waitTimeout: options.OperationWaitTimeout,
                    operationTimeout: options.HardwareExecutionTimeout).ConfigureAwait(false);
                if (!IsBlockHex(mirrors.Block5Hex) || !IsBlockHex(mirrors.Block6Hex))
                    throw new BridgeHardwareException(BridgeHardwareError.MalformedResponse, "PM3 returned a malformed block response.");
                return Results.Ok(new MercuryMirrorReadResponse(
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

        app.MapPost("/api/v1/hardware/mercury/mutations", async (
            MercuryMutationRequest? request,
            MercuryConditionalWriter writer,
            BridgeOperationGate gate,
            BridgeOptions options,
            HttpContext context) =>
        {
            if (!MercuryMutationValidator.TryValidate(request, out _, out var validationError))
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
            catch (MercuryMutationValidationException ex)
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
        _ => Results.Json(new BridgeErrorResponse("pm3_unavailable", "Proxmark3 is unavailable."), statusCode: 503),
    };

    private static bool IsBlockHex(string? value) => value is not null && value.Length == 8 && value.All(Uri.IsHexDigit);
}

public sealed class BridgeLifecycleService : IHostedService
{
    private readonly IBridgePm3Device _device;
    private readonly BridgeOperationGate _operationGate;
    private readonly object _stopSync = new();
    private Task? _stopTask;

    public BridgeLifecycleService(IBridgePm3Device device, BridgeOperationGate? operationGate = null)
    {
        _device = device;
        _operationGate = operationGate ?? new BridgeOperationGate();
    }

    public Task StartAsync(CancellationToken cancellationToken) => _device.StartAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken)
    {
        lock (_stopSync)
        {
            // Do not dispose outside the operation gate. In particular, do not let a host
            // cancellation token interrupt the gate wait and race an in-flight USB read.
            return _stopTask ??= StopCoreAsync();
        }
    }

    private Task StopCoreAsync() => _operationGate.ExecuteAsync(async _ =>
    {
        await _device.DisposeAsync().ConfigureAwait(false);
        return true;
    }, CancellationToken.None, waitTimeout: null);
}
