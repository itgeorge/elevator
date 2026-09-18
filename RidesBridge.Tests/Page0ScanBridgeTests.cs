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
public sealed class Page0ScanBridgeTests
{
    [Test]
    public async Task AuthenticatedScanReturnsUppercaseBlocksSignalAndTouchesOnlyFourFiveAndSix()
    {
        var device = new RecordingScanDevice
        {
            Blocks =
            {
                [4] = "d6d1c733",
                [5] = "bbc7fd03",
                [6] = "bbc7fd03",
            },
            SignalMillivolts = 420,
        };
        await using var host = await ScanTestHost.CreateAsync(device);
        host.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await host.PairAsync());

        var response = await host.Client.GetAsync("/api/v1/hardware/page0/scan");
        var body = await response.Content.ReadFromJsonAsync<Page0ScanResponse>();

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(body, Is.EqualTo(new Page0ScanResponse("v1", "D6D1C733", "BBC7FD03", "BBC7FD03", 420)));
        Assert.That(device.Calls, Is.EqualTo(new[] { "tune", "read4", "read5", "read6" }));
    }

    [Test]
    public async Task ScanRequiresAuthenticationAndDoesNotTouchHardware()
    {
        var device = new RecordingScanDevice();
        await using var host = await ScanTestHost.CreateAsync(device);

        var response = await host.Client.GetAsync("/api/v1/hardware/page0/scan");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(device.Calls, Is.Empty);
    }

    [Test]
    public async Task ScanMapsNoChipTuneAndReadFailuresToDistinctCodes()
    {
        await using var noChipHost = await ScanTestHost.CreateAsync(RecordingScanDevice.CreateNoChip());
        noChipHost.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await noChipHost.PairAsync());
        var noChip = await noChipHost.Client.GetAsync("/api/v1/hardware/page0/scan");
        Assert.That(noChip.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        Assert.That((await noChip.Content.ReadFromJsonAsync<BridgeErrorResponse>())!.Code, Is.EqualTo("no_chip"));

        await using var tuneHost = await ScanTestHost.CreateAsync(RecordingScanDevice.CreateTuneFailure());
        tuneHost.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await tuneHost.PairAsync());
        var tune = await tuneHost.Client.GetAsync("/api/v1/hardware/page0/scan");
        Assert.That(tune.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
        Assert.That((await tune.Content.ReadFromJsonAsync<BridgeErrorResponse>())!.Code, Is.EqualTo("lf_tune_failed"));

        await using var readHost = await ScanTestHost.CreateAsync(RecordingScanDevice.CreateReadFailure());
        readHost.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await readHost.PairAsync());
        var read = await readHost.Client.GetAsync("/api/v1/hardware/page0/scan");
        Assert.That(read.StatusCode, Is.EqualTo(HttpStatusCode.BadGateway));
        Assert.That((await read.Content.ReadFromJsonAsync<BridgeErrorResponse>())!.Code, Is.EqualTo("page0_read_failed"));
    }

    [Test]
    public async Task MissingBlocksEndpointReturnsAllowlistedBlocksExactlyOnceInOrder()
    {
        var device = new RecordingScanDevice();
        await using var host = await ScanTestHost.CreateAsync(device);
        host.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await host.PairAsync());

        var response = await host.Client.GetAsync("/api/v1/hardware/page0/missing");
        var body = await response.Content.ReadFromJsonAsync<Page0MissingBlocksResponse>();

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(body!.Blocks.Select(entry => (entry.Block, entry.Value)), Is.EqualTo(new[]
        {
            (0, "00148040"),
            (1, "11111111"),
            (2, "22222222"),
            (3, "33333333"),
            (7, "77777777"),
        }));
        Assert.That(device.Calls, Is.EqualTo(new[] { "read0", "read1", "read2", "read3", "read7" }));
    }

    [Test]
    public async Task MissingBlocksRequiresAuthenticationAndDoesNotTouchHardware()
    {
        var device = new RecordingScanDevice();
        await using var host = await ScanTestHost.CreateAsync(device);

        var response = await host.Client.GetAsync("/api/v1/hardware/page0/missing");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(device.Calls, Is.Empty);
    }

    [Test]
    public async Task CancellationDuringMissingBlocksDoesNotReturnSuccess()
    {
        var device = RecordingScanDevice.CreateCancelOnBlock(3);
        await using var host = await ScanTestHost.CreateAsync(device);
        host.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await host.PairAsync());

        var response = await host.Client.GetAsync("/api/v1/hardware/page0/missing");

        Assert.That(response.StatusCode, Is.EqualTo((HttpStatusCode)499));
        Assert.That(response.Content.Headers.ContentLength, Is.Null.Or.EqualTo(0));
        Assert.That(device.Calls, Is.EqualTo(new[] { "read0", "read1", "read2" }));
    }

    [Test]
    public async Task FakePm3KnownScanReturnsSeededVenusValuesAndMissingBlocksSupportUnknownDumpAssembly()
    {
        await using var host = await ScanTestHost.CreateAsync(FakePm3Device.CreateKnownVenusSeeded());
        host.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await host.PairAsync());

        var scanResponse = await host.Client.GetAsync("/api/v1/hardware/page0/scan");
        var scan = await scanResponse.Content.ReadFromJsonAsync<Page0ScanResponse>();
        Assert.That(scan, Is.EqualTo(new Page0ScanResponse(
            "v1",
            FakePm3Device.SeedBlock4Hex,
            FakePm3Device.SeedBlock5Hex,
            FakePm3Device.SeedBlock6Hex,
            FakePm3Device.SeedSignalMillivolts)));
        Assert.That(EncodingSequences.Venus.TryDecode(T55Block.FromHex(scan!.Block5), out var rides), Is.True);
        Assert.That(rides, Is.EqualTo((uint)FakePm3Device.SeedRidesRemaining));

        var missingResponse = await host.Client.GetAsync("/api/v1/hardware/page0/missing");
        var missing = await missingResponse.Content.ReadFromJsonAsync<Page0MissingBlocksResponse>();
        Assert.That(missing!.Blocks.Select(entry => entry.Block), Is.EqualTo(Page0MissingBlocks.Allowlist));
    }

    [Test]
    public async Task FakePm3NoChipScanReturns409NoChip()
    {
        await using var host = await ScanTestHost.CreateAsync(FakePm3Device.CreateNoChip());
        host.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await host.PairAsync());

        var response = await host.Client.GetAsync("/api/v1/hardware/page0/scan");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        Assert.That((await response.Content.ReadFromJsonAsync<BridgeErrorResponse>())!.Code, Is.EqualTo("no_chip"));
    }

    [Test]
    public async Task FakePm3TuneFailedScanReturns503LfTuneFailed()
    {
        await using var host = await ScanTestHost.CreateAsync(FakePm3Device.CreateTuneFailed());
        host.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await host.PairAsync());

        var response = await host.Client.GetAsync("/api/v1/hardware/page0/scan");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
        Assert.That((await response.Content.ReadFromJsonAsync<BridgeErrorResponse>())!.Code, Is.EqualTo("lf_tune_failed"));
    }

    [Test]
    public async Task FakePm3ReadFailedScanReturns502Page0ReadFailed()
    {
        await using var host = await ScanTestHost.CreateAsync(FakePm3Device.CreateReadFailed());
        host.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await host.PairAsync());

        var response = await host.Client.GetAsync("/api/v1/hardware/page0/scan");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadGateway));
        Assert.That((await response.Content.ReadFromJsonAsync<BridgeErrorResponse>())!.Code, Is.EqualTo("page0_read_failed"));
    }

    [Test]
    public async Task FakePm3UnknownMirrorsSeedReturnsUndecodableScanAndDeterministicMissingBlocks()
    {
        await using var host = await ScanTestHost.CreateAsync(FakePm3Device.CreateUnknownMirrorsSeeded());
        host.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await host.PairAsync());

        var scan = await (await host.Client.GetAsync("/api/v1/hardware/page0/scan")).Content.ReadFromJsonAsync<Page0ScanResponse>();
        Assert.That(scan!.Block4, Is.EqualTo(FakePm3Device.UnknownSeedBlock4Hex));
        Assert.That(scan.Block5, Is.EqualTo(FakePm3Device.UnknownSeedBlock5Hex));
        Assert.That(scan.Block6, Is.EqualTo(FakePm3Device.UnknownSeedBlock6Hex));
        Assert.That(EncodingSequences.All.Any(sequence => sequence.TryDecode(T55Block.FromHex(scan.Block5), out _)), Is.False);

        var missing = await (await host.Client.GetAsync("/api/v1/hardware/page0/missing")).Content.ReadFromJsonAsync<Page0MissingBlocksResponse>();
        Assert.That(missing!.Blocks.Select(entry => (entry.Block, entry.Value)), Is.EqualTo(new[]
        {
            (0, "00148040"),
            (1, "00000001"),
            (2, "00000002"),
            (3, "00000003"),
            (7, "00000000"),
        }));
    }

    private sealed class ScanTestHost : IAsyncDisposable
    {
        public HttpClient Client { get; }
        private readonly WebApplication _app;

        private ScanTestHost(WebApplication app, HttpClient client)
        {
            _app = app;
            Client = client;
        }

        public static async Task<ScanTestHost> CreateAsync(IBridgePm3Device device)
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
            return new ScanTestHost(app, app.GetTestClient());
        }

        public async Task<string> PairAsync()
        {
            var pairing = _app.Services.GetRequiredService<PairingCodeService>().IssueCode();
            var response = await Client.PostAsJsonAsync("/api/v1/pair", new PairRequest(pairing.Value));
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<PairResponse>())!.AccessToken;
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.DisposeAsync();
        }
    }

    private sealed class RecordingScanDevice : IBridgePm3Device
    {
        public Dictionary<int, string> Blocks { get; } = new()
        {
            [0] = "00148040",
            [1] = "11111111",
            [2] = "22222222",
            [3] = "33333333",
            [4] = "44444444",
            [5] = "AAAAAAAA",
            [6] = "CCCCCCCC",
            [7] = "77777777",
        };
        public List<string> Calls { get; } = [];
        public int SignalMillivolts { get; init; } = FakePm3Device.SeedSignalMillivolts;
        public bool FailTune { get; init; }
        public bool FailRead { get; init; }
        public bool FailNoChip { get; init; }
        public int? CancelOnBlock { get; init; }

        public static RecordingScanDevice CreateNoChip() => new() { FailNoChip = true };
        public static RecordingScanDevice CreateTuneFailure() => new() { FailTune = true };
        public static RecordingScanDevice CreateReadFailure() => new() { FailRead = true };
        public static RecordingScanDevice CreateCancelOnBlock(int block) => new() { CancelOnBlock = block };

        public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<string> ReadPage0Block5Async(CancellationToken ct = default) => ReadAsync(5, ct);
        public Task<string> ReadPage0Block6Async(CancellationToken ct = default) => ReadAsync(6, ct);
        public Task<(string Block5Hex, string Block6Hex)> ReadPage0MirrorAsync(CancellationToken ct = default) =>
            Task.FromResult((Blocks[5], Blocks[6]));

        public Task<Page0ScanReadResult> ScanPage0Async(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (FailNoChip)
                throw new BridgeHardwareException(BridgeHardwareError.NoChip, "No supported T55xx chip is present.");
            Calls.Add("tune");
            if (FailTune)
                throw new BridgeHardwareException(BridgeHardwareError.TuneFailed, "LF tune failed.");
            if (FailRead)
                throw new BridgeHardwareException(BridgeHardwareError.ReadFailed, "Page-0 scan read failed.");
            Calls.Add("read4");
            Calls.Add("read5");
            Calls.Add("read6");
            return Task.FromResult(new Page0ScanReadResult(Blocks[4], Blocks[5], Blocks[6], SignalMillivolts));
        }

        public Task<IReadOnlyList<Page0BlockReadResult>> ReadPage0MissingBlocksAsync(CancellationToken ct = default)
        {
            var results = new List<Page0BlockReadResult>(Page0MissingBlocks.Allowlist.Length);
            foreach (var block in Page0MissingBlocks.Allowlist)
            {
                ct.ThrowIfCancellationRequested();
                if (CancelOnBlock == block)
                    throw new OperationCanceledException();
                Calls.Add($"read{block}");
                results.Add(new Page0BlockReadResult(block, Blocks[block]));
            }
            return Task.FromResult((IReadOnlyList<Page0BlockReadResult>)results);
        }

        public Task WritePage0Block5Async(string value, CancellationToken ct = default) => Task.CompletedTask;
        public Task WritePage0Block6Async(string value, CancellationToken ct = default) => Task.CompletedTask;
        public Task<string> ReadPage0Block1To6Async(int block, CancellationToken ct = default) => ReadAsync(block, ct);
        public Task WritePage0Block1To6Async(int block, string value, CancellationToken ct = default)
        {
            Blocks[block] = value;
            return Task.CompletedTask;
        }
        public async Task<IReadOnlyList<Page0BlockReadResult>> ReadPage0Blocks1To6Async(CancellationToken ct = default)
        {
            var results = new List<Page0BlockReadResult>(Page0Blocks1To6.Allowlist.Length);
            foreach (var block in Page0Blocks1To6.Allowlist)
                results.Add(new Page0BlockReadResult(block, await ReadAsync(block, ct).ConfigureAwait(false)));
            return results;
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private Task<string> ReadAsync(int block, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Calls.Add($"read{block}");
            return Task.FromResult(Blocks[block]);
        }
    }
}
