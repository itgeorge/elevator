using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using RidesBridge;
using Tokens;

namespace RidesBridge.Tests;

[TestFixture]
public sealed class FakePm3LaunchTests
{
    [Test]
    public void ParseAcceptsOnlyTheExactFakePm3Flag()
    {
        var launch = BridgeLaunchOptions.Parse(["--fake-pm3"]);

        Assert.That(launch.FakePm3, Is.True);
        Assert.That(launch.Everyday, Is.False);
        Assert.That(launch.ShowHelp, Is.False);
        Assert.That(launch.AspNetCoreArguments, Is.Empty);
    }

    [TestCase("--fake-pm3=true")]
    [TestCase("--fake-pm3=false")]
    [TestCase("--fake-pm3=1")]
    [TestCase("--fake-pm3ish")]
    public void ParseRejectsMalformedFakePm3Flag(string argument)
    {
        var error = Assert.Throws<BridgeLaunchConfigurationException>(() => BridgeLaunchOptions.Parse([argument]));

        Assert.That(error!.Message, Does.Contain("--fake-pm3"));
    }

    [Test]
    public void ParseRejectsDuplicateOrAdditionalFakePm3LaunchOptions()
    {
        Assert.That(
            () => BridgeLaunchOptions.Parse(["--fake-pm3", "--fake-pm3"]),
            Throws.TypeOf<BridgeLaunchConfigurationException>().With.Message.Contains("once"));
        Assert.That(
            () => BridgeLaunchOptions.Parse(["--fake-pm3", "--unknown"]),
            Throws.TypeOf<BridgeLaunchConfigurationException>().With.Message.Contains("only"));
        Assert.That(
            () => BridgeLaunchOptions.Parse(["--fake-pm3", "value"]),
            Throws.TypeOf<BridgeLaunchConfigurationException>().With.Message.Contains("only"));
    }

    [Test]
    public void ParseRejectsCombiningFakePm3WithEveryday()
    {
        var error = Assert.Throws<BridgeLaunchConfigurationException>(() =>
            BridgeLaunchOptions.Parse(["--everyday", "--fake-pm3"]));

        Assert.That(error!.Message, Does.Contain("mutually exclusive"));
    }

    [Test]
    public void FakePm3OptionsOwnBindAndDisablePm3ButPreserveDurablePathOverrides()
    {
        using var temp = new TemporaryDirectory();
        var configuredData = Path.Combine(temp.Path, "data");
        var configuredStore = Path.Combine(temp.Path, "store.json");
        var configuredIdentity = Path.Combine(temp.Path, "identity");
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Bridge:BindUrl"] = "not-a-bind-url",
            ["Bridge:DataDirectory"] = configuredData,
            ["Bridge:PairedClientsPath"] = configuredStore,
            ["Bridge:IdentityPath"] = configuredIdentity,
            ["Pm3:Port"] = "/dev/fixed-port",
            ["Pm3:AutoDiscover"] = "false",
        }).Build();

        var options = FakePm3LaunchMode.CreateOptions(configuration, 5093);

        Assert.That(options.BindUrl, Is.EqualTo("http://0.0.0.0:5093"));
        Assert.That(options.Pm3AutoDiscover, Is.False);
        Assert.That(options.Pm3Port, Is.Null);
        Assert.That(options.FakePm3Device, Is.True);
        Assert.That(options.DataDirectory, Is.EqualTo(configuredData));
        Assert.That(options.EffectivePairedClientsPath, Is.EqualTo(configuredStore));
        Assert.That(options.EffectiveBridgeIdentityPath, Is.EqualTo(configuredIdentity));
        Assert.That(() => options.Validate(), Throws.Nothing);
    }

    [Test]
    public void BonjourUsesTheSelectedFakePm3Port()
    {
        var options = FakePm3LaunchMode.CreateOptions(
            new ConfigurationBuilder().Build(), 5094);

        var descriptor = BonjourAdvertisementFactory.Create(
            options,
            "0123456789ABCDEF0123456789ABCDEF",
            [IPAddress.Parse("192.168.1.20")])[0];

        Assert.That(descriptor.Port, Is.EqualTo(5094));
        Assert.That(descriptor.HttpUrl, Is.EqualTo(new Uri("http://192.168.1.20:5094/")));
    }

    [Test]
    public async Task ProcessRejectsMalformedFakePm3FlagBeforeStartingTheBridge()
    {
        var result = await RunBridgeProcessAsync("--fake-pm3=true");

        Assert.That(result.ExitCode, Is.EqualTo(2));
        Assert.That(result.Stderr, Does.Contain("--fake-pm3"));
        Assert.That(result.Stdout, Does.Not.Contain("Pairing PIN:"));
    }

    [Test]
    public async Task ProcessSelectsAFreeFakePm3PortWithoutStartingPm3Hardware()
    {
        using var temp = new TemporaryDirectory();
        using var first = HoldPortIfFree(EverydayPortSelector.DefaultFirstPort);
        using var second = HoldPortIfFree(EverydayPortSelector.DefaultFirstPort + 1);

        var line = await RunBridgeUntilOutputAsync(
            new Dictionary<string, string?> { ["Bridge__DataDirectory"] = Path.Combine(temp.Path, "state") },
            "--fake-pm3");
        var bindUrl = line["Listening bind:".Length..].Trim();
        var selectedPort = new Uri(bindUrl).Port;

        Assert.That(selectedPort, Is.GreaterThanOrEqualTo(EverydayPortSelector.DefaultFirstPort + 2));
        Assert.That(selectedPort, Is.LessThanOrEqualTo(EverydayPortSelector.DefaultLastPort));
    }

    [Test]
    public async Task FakePm3DeviceReturnsSeededMirrorsAndSupportsConditionalWrites()
    {
        await using var host = await FakePm3TestHost.CreateAsync();
        host.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await host.PairAsync());

        var mirrorResponse = await host.Client.GetAsync("/api/v1/hardware/page0/mirrors");
        var mirrors = await mirrorResponse.Content.ReadFromJsonAsync<Page0MirrorReadResponse>();

        Assert.That(mirrorResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(mirrors, Is.EqualTo(new Page0MirrorReadResponse(
            "v1", FakePm3Device.SeedBlock5Hex, FakePm3Device.SeedBlock6Hex)));

        var venus = EncodingSequences.Venus;
        Assert.That(venus.TryDecode(T55Block.FromHex(FakePm3Device.SeedBlock5Hex), out var rides), Is.True);
        Assert.That(rides, Is.EqualTo((uint)FakePm3Device.SeedRidesRemaining));
        Assert.That(venus.FriendlyName, Is.EqualTo(FakePm3Device.SeedSequenceName));

        var desired = venus.Encode(FakePm3Device.SeedRidesRemaining - 1).ToHex();
        var mutationResponse = await host.Client.PostAsJsonAsync(
            "/api/v1/hardware/page0/mutations",
            new Page0MutationRequest("v1",
            [
                new Page0Mutation(5, FakePm3Device.SeedBlock5Hex, desired),
                new Page0Mutation(6, FakePm3Device.SeedBlock6Hex, desired),
            ]));
        var mutation = await mutationResponse.Content.ReadFromJsonAsync<Page0MutationResponse>();

        Assert.That(mutationResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(mutation!.Status, Is.EqualTo("written"));

        mirrorResponse = await host.Client.GetAsync("/api/v1/hardware/page0/mirrors");
        mirrors = await mirrorResponse.Content.ReadFromJsonAsync<Page0MirrorReadResponse>();
        Assert.That(mirrors!.Block5, Is.EqualTo(desired));
        Assert.That(mirrors.Block6, Is.EqualTo(desired));
    }

    [Test]
    public async Task FakePm3StartDoesNotOpenUsb()
    {
        var device = FakePm3Device.CreateSeeded();
        await device.StartAsync();
        await device.DisposeAsync();
    }

    private static async Task<(int ExitCode, string Stdout, string Stderr)> RunBridgeProcessAsync(
        params string[] arguments)
    {
        using var process = CreateBridgeProcess(arguments, null);
        Assert.That(process.Start(), Is.True);
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        var exit = process.WaitForExitAsync();
        if (await Task.WhenAny(exit, Task.Delay(TimeSpan.FromSeconds(30))) != exit)
        {
            await StopProcessAsync(process);
            Assert.Fail("Bridge process did not exit within 30 seconds.");
        }

        return (process.ExitCode, await stdout, await stderr);
    }

    private static async Task<string> RunBridgeUntilOutputAsync(
        IReadOnlyDictionary<string, string?> environment,
        params string[] arguments)
    {
        using var process = CreateBridgeProcess(arguments, environment);
        var lineFound = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        process.OutputDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data?.StartsWith("Listening bind:", StringComparison.Ordinal) == true)
                lineFound.TrySetResult(eventArgs.Data);
        };
        process.ErrorDataReceived += (_, _) => { };
        Assert.That(process.Start(), Is.True);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            var completed = await Task.WhenAny(lineFound.Task, process.WaitForExitAsync(), Task.Delay(TimeSpan.FromSeconds(30)));
            if (completed != lineFound.Task)
            {
                await StopProcessAsync(process);
                Assert.Fail("Bridge process did not report its selected port within 30 seconds.");
            }

            return await lineFound.Task;
        }
        finally
        {
            await StopProcessAsync(process);
        }
    }

    private static async Task StopProcessAsync(Process process)
    {
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
        }
    }

    private static Process CreateBridgeProcess(
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string?>? environment)
    {
        var assemblyPath = Path.Combine(TestContext.CurrentContext.TestDirectory, "RidesBridge.dll");
        Assert.That(File.Exists(assemblyPath), Is.True, $"Expected built bridge at {assemblyPath}");
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = TestContext.CurrentContext.TestDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(assemblyPath);
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);
        startInfo.Environment["DOTNET_NOLOGO"] = "1";
        if (environment is not null)
        {
            foreach (var pair in environment)
                startInfo.Environment[pair.Key] = pair.Value;
        }
        return new Process { StartInfo = startInfo, EnableRaisingEvents = true };
    }

    private static TcpListener? HoldPortIfFree(int port)
    {
        try
        {
            var listener = new TcpListener(IPAddress.Any, port);
            listener.Start();
            return listener;
        }
        catch (SocketException)
        {
            return null;
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "ridesbridge-tests", Guid.NewGuid().ToString("N"));
        public TemporaryDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }

    private sealed class FakePm3TestHost : IAsyncDisposable
    {
        public HttpClient Client { get; }
        public FakePm3Device Device { get; }
        private readonly WebApplication _app;

        private FakePm3TestHost(WebApplication app, HttpClient client, FakePm3Device device)
        {
            _app = app;
            Client = client;
            Device = device;
        }

        public static async Task<FakePm3TestHost> CreateAsync()
        {
            var device = FakePm3Device.CreateSeeded();
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
                FakePm3Device = true,
            }, device);
            var app = builder.Build();
            app.MapRidesBridge();
            await app.StartAsync();
            return new FakePm3TestHost(app, app.GetTestClient(), device);
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
}
