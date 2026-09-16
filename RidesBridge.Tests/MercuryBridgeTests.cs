using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using RidesBridge;
using Tokens;

namespace RidesBridge.Tests;

[TestFixture]
public sealed class MercuryBridgeTests
{
    [Test]
    public async Task AuthenticatedMirrorReadReturnsUppercaseRawBlocksAndOnlyReadsFiveAndSix()
    {
        await using var host = await MercuryTestHost.CreateAsync(new RecordingMercuryDevice
        {
            Blocks = { [5] = "deadbeef", [6] = "a1b2c3d4" },
        });
        host.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await host.PairAsync());

        var response = await host.Client.GetAsync("/api/v1/hardware/mercury/mirrors");
        var body = await response.Content.ReadFromJsonAsync<MercuryMirrorReadResponse>();

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(body, Is.EqualTo(new MercuryMirrorReadResponse("v1", "DEADBEEF", "A1B2C3D4")));
        Assert.That(host.Device.Calls, Is.EqualTo(new[] { "read5", "read6" }));
    }

    [Test]
    public async Task MirrorReadRequiresAuthentication()
    {
        await using var host = await MercuryTestHost.CreateAsync(new RecordingMercuryDevice());

        var response = await host.Client.GetAsync("/api/v1/hardware/mercury/mirrors");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(host.Device.Calls, Is.Empty);
    }

    [Test]
    public async Task MutationRequiresAuthenticationAndDoesNotTouchHardware()
    {
        var device = new RecordingMercuryDevice();
        await using var host = await MercuryTestHost.CreateAsync(device);

        var response = await host.Client.PostAsJsonAsync(
            "/api/v1/hardware/mercury/mutations",
            Request(new MercuryMutation(5, "AAAAAAAA", "BBBBBBBB")));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(device.Calls, Is.Empty);
    }

    [TestCase(0, "invalid_mutation_block")]
    [TestCase(7, "invalid_mutation_block")]
    [TestCase(8, "invalid_mutation_block")]
    [TestCase(1, "mercury_block_not_allowed")]
    [TestCase(2, "mercury_block_not_allowed")]
    [TestCase(3, "mercury_block_not_allowed")]
    [TestCase(4, "mercury_block_not_allowed")]
    public async Task InvalidTargetIsRejectedBeforeHardware(int block, string code)
    {
        await using var host = await MercuryTestHost.CreateAsync(new RecordingMercuryDevice());
        host.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await host.PairAsync());

        var response = await host.Client.PostAsJsonAsync(
            "/api/v1/hardware/mercury/mutations",
            Request(new MercuryMutation(block, "00000000", "00000001")));
        var error = await response.Content.ReadFromJsonAsync<BridgeErrorResponse>();

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(error!.Code, Is.EqualTo(code));
        Assert.That(host.Device.Calls, Is.Empty);
    }

    [Test]
    public async Task DuplicateAndMalformedMutationsAreRejectedBeforeHardware()
    {
        await using var host = await MercuryTestHost.CreateAsync(new RecordingMercuryDevice());
        host.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await host.PairAsync());

        var duplicate = await host.Client.PostAsJsonAsync(
            "/api/v1/hardware/mercury/mutations",
            Request(new MercuryMutation(5, "00000000", "00000001"), new MercuryMutation(5, "00000000", "00000002")));
        var malformed = await host.Client.PostAsJsonAsync(
            "/api/v1/hardware/mercury/mutations",
            Request(new MercuryMutation(6, "123", "00000001")));

        Assert.That(duplicate.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That((await duplicate.Content.ReadFromJsonAsync<BridgeErrorResponse>())!.Code,
            Is.EqualTo("duplicate_mutation_block"));
        Assert.That(malformed.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That((await malformed.Content.ReadFromJsonAsync<BridgeErrorResponse>())!.Code,
            Is.EqualTo("invalid_block_hex"));
        Assert.That(host.Device.Calls, Is.Empty);
    }

    [Test]
    public async Task PreflightReadsEveryTargetBeforeAnyWriteAndReturnsAllActualValuesOnConflict()
    {
        var device = new RecordingMercuryDevice
        {
            Blocks = { [5] = "11111111", [6] = "22222222" },
        };
        await using var host = await MercuryTestHost.CreateAsync(device);
        host.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await host.PairAsync());

        var response = await host.Client.PostAsJsonAsync(
            "/api/v1/hardware/mercury/mutations",
            Request(new MercuryMutation(5, "AAAAAAAA", "BBBBBBBB"), new MercuryMutation(6, "CCCCCCCC", "DDDDDDDD")));
        var body = await response.Content.ReadFromJsonAsync<MercuryMutationResponse>();

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(body!.Status, Is.EqualTo("conflict"));
        Assert.That(body.Results.Select(r => (r.Block, r.Status, r.Actual)), Is.EqualTo(new[]
        {
            (5, "conflict", "11111111"), (6, "conflict", "22222222"),
        }));
        Assert.That(body.RollbackStatus, Is.EqualTo("notNeeded"));
        Assert.That(device.Calls, Is.EqualTo(new[] { "read5", "read6" }));
    }

    [Test]
    public async Task AllDesiredReturnsAlreadyAppliedWithoutWrites()
    {
        var device = new RecordingMercuryDevice
        {
            Blocks = { [5] = "BBBBBBBB", [6] = "DDDDDDDD" },
        };
        await using var host = await MercuryTestHost.CreateAsync(device);
        host.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await host.PairAsync());

        var response = await host.Client.PostAsJsonAsync(
            "/api/v1/hardware/mercury/mutations",
            Request(new MercuryMutation(6, "CCCCCCCC", "DDDDDDDD"), new MercuryMutation(5, "AAAAAAAA", "BBBBBBBB")));
        var body = await response.Content.ReadFromJsonAsync<MercuryMutationResponse>();

        Assert.That(body!.Status, Is.EqualTo("alreadyApplied"));
        Assert.That(body.Results.Select(r => r.Status), Is.All.EqualTo("alreadyApplied"));
        Assert.That(device.Calls, Is.EqualTo(new[] { "read5", "read6" }));
    }

    [Test]
    public async Task WritesExpectedTargetsInBlockOrderWithImmediateReadBackAndSupportsPartialRetry()
    {
        var device = new RecordingMercuryDevice
        {
            Blocks = { [5] = "AAAAAAAA", [6] = "CCCCCCCC" },
        };
        await using var host = await MercuryTestHost.CreateAsync(device);
        host.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await host.PairAsync());

        var response = await host.Client.PostAsJsonAsync(
            "/api/v1/hardware/mercury/mutations",
            Request(new MercuryMutation(6, "CCCCCCCC", "DDDDDDDD"), new MercuryMutation(5, "AAAAAAAA", "BBBBBBBB")));
        var body = await response.Content.ReadFromJsonAsync<MercuryMutationResponse>();

        Assert.That(body!.Status, Is.EqualTo("written"));
        Assert.That(device.Calls, Is.EqualTo(new[]
        {
            "read5", "read6", "write5=BBBBBBBB", "read5", "write6=DDDDDDDD", "read6",
        }));

        device.Blocks[6] = "CCCCCCCC";
        device.Calls.Clear();
        response = await host.Client.PostAsJsonAsync(
            "/api/v1/hardware/mercury/mutations",
            Request(new MercuryMutation(5, "AAAAAAAA", "BBBBBBBB"), new MercuryMutation(6, "CCCCCCCC", "DDDDDDDD")));
        body = await response.Content.ReadFromJsonAsync<MercuryMutationResponse>();

        Assert.That(body!.Status, Is.EqualTo("written"));
        Assert.That(body.Results.Select(r => r.Status), Is.EqualTo(new[] { "alreadyApplied", "written" }));
        Assert.That(device.Calls, Is.EqualTo(new[] { "read5", "read6", "write6=DDDDDDDD", "read6" }));
    }

    [Test]
    public async Task VerificationFailureStopsLaterWritesAndRollsBackOnlyChangedBlocks()
    {
        var device = new RecordingMercuryDevice
        {
            Blocks = { [5] = "AAAAAAAA", [6] = "CCCCCCCC" },
        };
        device.ReadResponses[6] = new Queue<string>(["BAD00000"]);
        await using var host = await MercuryTestHost.CreateAsync(device);
        host.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await host.PairAsync());

        var response = await host.Client.PostAsJsonAsync(
            "/api/v1/hardware/mercury/mutations",
            Request(new MercuryMutation(5, "AAAAAAAA", "BBBBBBBB"), new MercuryMutation(6, "CCCCCCCC", "DDDDDDDD")));
        var body = await response.Content.ReadFromJsonAsync<MercuryMutationResponse>();

        Assert.That(body!.Status, Is.EqualTo("verifyFailed"));
        Assert.That(body.RollbackStatus, Is.EqualTo("rollbackSucceeded"));
        Assert.That(body.Rollback.Select(r => (r.Block, r.Expected, r.Actual, r.Succeeded)), Is.EqualTo(new[]
        {
            (6, "CCCCCCCC", "CCCCCCCC", true), (5, "AAAAAAAA", "AAAAAAAA", true),
        }));
        Assert.That(device.Calls, Is.EqualTo(new[]
        {
            "read5", "read6", "write5=BBBBBBBB", "read5", "write6=DDDDDDDD", "read6",
            "write6=CCCCCCCC", "read6", "write5=AAAAAAAA", "read5",
        }));
        Assert.That(device.Calls, Does.Not.Contain("write7"));
        Assert.That(device.Blocks[5], Is.EqualTo("AAAAAAAA"));
        Assert.That(device.Blocks[6], Is.EqualTo("CCCCCCCC"));
    }

    [Test]
    public async Task IncompleteRollbackIsReportedAndContinuesThroughEveryChangedBlock()
    {
        var device = new RecordingMercuryDevice
        {
            Blocks = { [5] = "AAAAAAAA", [6] = "CCCCCCCC" },
            FailExpectedWrites = true,
        };
        device.ReadResponses[6] = new Queue<string>(["BAD00000"]);
        await using var host = await MercuryTestHost.CreateAsync(device);
        host.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await host.PairAsync());

        var response = await host.Client.PostAsJsonAsync(
            "/api/v1/hardware/mercury/mutations",
            Request(new MercuryMutation(5, "AAAAAAAA", "BBBBBBBB"), new MercuryMutation(6, "CCCCCCCC", "DDDDDDDD")));
        var body = await response.Content.ReadFromJsonAsync<MercuryMutationResponse>();

        Assert.That(body!.Status, Is.EqualTo("verifyFailed"));
        Assert.That(body.RollbackStatus, Is.EqualTo("rollbackIncomplete"));
        Assert.That(body.Results.Single(r => r.Block == 5).Actual, Is.Null);
        Assert.That(body.Results.Single(r => r.Block == 6).Actual, Is.Null);
        Assert.That(body.Rollback, Has.Count.EqualTo(2));
        Assert.That(device.Calls.Count(c => c.StartsWith("write", StringComparison.Ordinal)), Is.EqualTo(4));
    }

    [Test]
    public async Task CancellationAfterMutationStartsStillCompletesRollback()
    {
        var device = new RecordingMercuryDevice
        {
            Blocks = { [5] = "AAAAAAAA" },
            CancelOnFirstWrite = true,
        };
        using var cancellation = new CancellationTokenSource();
        device.Cancellation = cancellation;
        var writer = new MercuryConditionalWriter(device);

        var result = await writer.ExecuteAsync(
            Request(new MercuryMutation(5, "AAAAAAAA", "BBBBBBBB")), cancellation.Token);

        Assert.That(result.Status, Is.EqualTo("verifyFailed"));
        Assert.That(result.RollbackStatus, Is.EqualTo("rollbackSucceeded"));
        Assert.That(device.Calls, Is.EqualTo(new[] { "read5", "write5=BBBBBBBB", "write5=AAAAAAAA", "read5" }));
        Assert.That(device.Blocks[5], Is.EqualTo("AAAAAAAA"));
    }

    [Test]
    public async Task WriteThatAppliesThenThrowsIsIncludedInReverseRollback()
    {
        var device = new RecordingMercuryDevice
        {
            Blocks = { [5] = "AAAAAAAA", [6] = "CCCCCCCC" },
            ApplyThenThrowBlock = 6,
            ApplyThenThrowValue = "DDDDDDDD",
        };
        await using var host = await MercuryTestHost.CreateAsync(device);
        host.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await host.PairAsync());

        var response = await host.Client.PostAsJsonAsync(
            "/api/v1/hardware/mercury/mutations",
            Request(new MercuryMutation(5, "AAAAAAAA", "BBBBBBBB"), new MercuryMutation(6, "CCCCCCCC", "DDDDDDDD")));
        var body = await response.Content.ReadFromJsonAsync<MercuryMutationResponse>();

        Assert.That(body!.Status, Is.EqualTo("verifyFailed"));
        Assert.That(body.RollbackStatus, Is.EqualTo("rollbackSucceeded"));
        Assert.That(body.Results.Single(r => r.Block == 6).Actual, Is.EqualTo("CCCCCCCC"));
        Assert.That(body.Results.Single(r => r.Block == 5).Actual, Is.EqualTo("AAAAAAAA"));
        Assert.That(body.Rollback.Select(r => r.Block), Is.EqualTo(new[] { 6, 5 }));
        Assert.That(device.Calls, Is.EqualTo(new[]
        {
            "read5", "read6", "write5=BBBBBBBB", "read5", "write6=DDDDDDDD",
            "write6=CCCCCCCC", "read6", "write5=AAAAAAAA", "read5",
        }));
        Assert.That(device.Blocks[5], Is.EqualTo("AAAAAAAA"));
        Assert.That(device.Blocks[6], Is.EqualTo("CCCCCCCC"));
    }

    [Test]
    public async Task RollbackHasIndependentFiniteRecoveryBudget()
    {
        var device = new RecordingMercuryDevice
        {
            Blocks = { [5] = "AAAAAAAA" },
            DelayExpectedWriteUntilCancellation = true,
        };
        device.ReadResponses[5] = new Queue<string>(["BAD00000"]);
        var writer = new MercuryConditionalWriter(device, TimeSpan.FromMilliseconds(40));
        var stopwatch = Stopwatch.StartNew();

        var result = await writer.ExecuteAsync(
            Request(new MercuryMutation(5, "AAAAAAAA", "BBBBBBBB")), CancellationToken.None);

        stopwatch.Stop();
        Assert.That(result.Status, Is.EqualTo("verifyFailed"));
        Assert.That(result.RollbackStatus, Is.EqualTo("rollbackIncomplete"));
        Assert.That(result.Rollback, Has.Count.EqualTo(1));
        Assert.That(result.Rollback[0].Succeeded, Is.False);
        Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(1)));
    }

    [Test]
    public async Task AdapterDiscardsTransportFatalSessionBeforeNextReadUsesFreshSession()
    {
        var firstCalls = new List<string>();
        var secondCalls = new List<string>();
        var first = new FakeBridgePm3Session(firstCalls, _ => Task.FromException<string>(new IOException("transport failed")));
        var second = new FakeBridgePm3Session(secondCalls);
        var sessions = new Queue<FakeBridgePm3Session>([first, second]);
        var adapter = new Pm3BridgeDeviceAdapter(
            new BridgeOptions { BindUrl = "http://127.0.0.1:5080", DataDirectory = Path.GetTempPath() },
            _ => sessions.Dequeue());

        var error = Assert.ThrowsAsync<BridgeHardwareException>(async () => await adapter.ReadPage0Block5Async());
        var value = await adapter.ReadPage0Block5Async();

        Assert.That(error!.Error, Is.EqualTo(BridgeHardwareError.Unavailable));
        Assert.That(first.Disposed, Is.True);
        Assert.That(value, Is.EqualTo("A1B2C3D4"));
        Assert.That(second.Disposed, Is.False);
        await adapter.DisposeAsync();
        Assert.That(second.Disposed, Is.True);
    }

    [Test]
    public void DependencyInjectionUsesConfiguredHardwareRecoveryTimeoutForRecovery()
    {
        var options = new BridgeOptions
        {
            BindUrl = "http://127.0.0.1:5080",
            DataDirectory = Path.GetTempPath(),
            PairedClientsPath = Path.Combine(Path.GetTempPath(), "ridesbridge-tests", Guid.NewGuid().ToString("N"), "paired.json"),
            HardwareExecutionTimeout = TimeSpan.FromMilliseconds(73),
        };
        var device = new RecordingMercuryDevice();
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddRidesBridge(options, device);
        using var provider = services.BuildServiceProvider();

        var writer = provider.GetRequiredService<MercuryConditionalWriter>();

        Assert.That(writer.RecoveryTimeoutForTesting, Is.EqualTo(options.HardwareRecoveryTimeout));
    }

    [Test]
    public async Task AdapterMirrorAndWritesUseOnlyMercuryBlocks()
    {
        var calls = new List<string>();
        var session = new FakeBridgePm3Session(calls);
        var adapter = new Pm3BridgeDeviceAdapter(
            new BridgeOptions { BindUrl = "http://127.0.0.1:5080", DataDirectory = Path.GetTempPath() },
            _ => session);

        Assert.That(await adapter.ReadMercuryMirrorAsync(), Is.EqualTo(("A1B2C3D4", "A1B2C3D4")));
        await adapter.WritePage0Block5Async("11223344");
        await adapter.WritePage0Block6Async("55667788");
        await adapter.DisposeAsync();

        Assert.That(calls, Is.EqualTo(new[]
        {
            "connected", "invalidate", "ensure", "read", "read6",
            "connected", "invalidate", "ensure", "write5:11223344",
            "connected", "invalidate", "ensure", "write6:55667788", "dispose",
        }));
    }

    private static MercuryMutationRequest Request(params MercuryMutation[] mutations) =>
        new("v1", mutations);

    private sealed class MercuryTestHost : IAsyncDisposable
    {
        public WebApplication App { get; }
        public HttpClient Client { get; }
        public RecordingMercuryDevice Device { get; }

        private MercuryTestHost(WebApplication app, HttpClient client, RecordingMercuryDevice device)
        {
            App = app;
            Client = client;
            Device = device;
        }

        public static async Task<MercuryTestHost> CreateAsync(RecordingMercuryDevice device)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                ApplicationName = typeof(BridgeApplication).Assembly.GetName().Name,
                EnvironmentName = "Testing",
            });
            builder.WebHost.UseTestServer();
            builder.Services.AddRidesBridge(new BridgeOptions
            {
                BindUrl = "http://127.0.0.1:5080",
                PairedClientsPath = Path.Combine(Path.GetTempPath(), "ridesbridge-tests", Guid.NewGuid().ToString("N"), "paired.json"),
            }, device);
            var app = builder.Build();
            app.MapRidesBridge();
            await app.StartAsync();
            return new MercuryTestHost(app, app.GetTestClient(), device);
        }

        public async Task<string> PairAsync()
        {
            var pairing = App.Services.GetRequiredService<PairingCodeService>().IssueCode();
            var response = await Client.PostAsJsonAsync("/api/v1/pair", new PairRequest(pairing.Value));
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<PairResponse>())!.AccessToken;
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await App.DisposeAsync();
        }
    }

    private sealed class RecordingMercuryDevice : IBridgePm3Device
    {
        public Dictionary<int, string> Blocks { get; } = new() { [5] = "AAAAAAAA", [6] = "CCCCCCCC" };
        public Dictionary<int, Queue<string>> ReadResponses { get; } = new();
        public List<string> Calls { get; } = [];
        public bool FailExpectedWrites { get; init; }
        public bool CancelOnFirstWrite { get; init; }
        public int? ApplyThenThrowBlock { get; init; }
        public string? ApplyThenThrowValue { get; init; }
        public bool DelayExpectedWriteUntilCancellation { get; init; }
        public CancellationTokenSource? Cancellation { get; set; }
        private int _writes;

        public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<string> ReadPage0Block5Async(CancellationToken ct = default) => ReadAsync(5, ct);
        public Task<string> ReadPage0Block6Async(CancellationToken ct = default) => ReadAsync(6, ct);

        public async Task<(string Block5Hex, string Block6Hex)> ReadMercuryMirrorAsync(CancellationToken ct = default)
            => (await ReadAsync(5, ct), await ReadAsync(6, ct));

        public Task WritePage0Block5Async(string value, CancellationToken ct = default) => WriteAsync(5, value, ct);
        public Task WritePage0Block6Async(string value, CancellationToken ct = default) => WriteAsync(6, value, ct);

        private Task<string> ReadAsync(int block, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Calls.Add($"read{block}");
            if (_writes > 0 && ReadResponses.TryGetValue(block, out var responses) && responses.Count != 0)
                return Task.FromResult(responses.Dequeue());
            return Task.FromResult(Blocks[block]);
        }

        private async Task WriteAsync(int block, string value, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Calls.Add($"write{block}={value}");
            _writes++;
            if (CancelOnFirstWrite && _writes == 1)
            {
                Blocks[block] = value;
                Cancellation!.Cancel();
                return;
            }
            if (DelayExpectedWriteUntilCancellation && value is "AAAAAAAA" or "CCCCCCCC")
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            if (FailExpectedWrites && value is "AAAAAAAA" or "CCCCCCCC")
                throw new IOException("synthetic rollback failure");
            Blocks[block] = value;
            if (ApplyThenThrowBlock == block && ApplyThenThrowValue == value)
                throw new IOException("synthetic lost write response");
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
