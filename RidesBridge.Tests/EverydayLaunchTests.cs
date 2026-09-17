using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;
using RidesBridge;

namespace RidesBridge.Tests;

[TestFixture]
public sealed class EverydayLaunchTests
{
    [Test]
    public async Task ProcessHelpExitsWithoutStartingTheBridge()
    {
        var result = await RunBridgeProcessAsync("--help");

        Assert.That(result.ExitCode, Is.Zero);
        Assert.That(result.Stdout, Does.Contain("--everyday"));
        Assert.That(result.Stdout, Does.Contain("5080..5179"));
        Assert.That(result.Stdout, Does.Not.Contain("Pairing PIN:"));
    }

    [Test]
    public async Task ProcessRejectsMalformedEverydayFlagBeforeStartingTheBridge()
    {
        var result = await RunBridgeProcessAsync("--everyday=true");

        Assert.That(result.ExitCode, Is.EqualTo(2));
        Assert.That(result.Stderr, Does.Contain("--everyday"));
        Assert.That(result.Stdout, Does.Not.Contain("Pairing PIN:"));
    }

    [Test]
    public async Task ProcessSelectsAFreeEverydayPortWithoutStartingPm3Hardware()
    {
        using var temp = new TemporaryDirectory();
        using var first = HoldPortIfFree(EverydayPortSelector.DefaultFirstPort);
        using var second = HoldPortIfFree(EverydayPortSelector.DefaultFirstPort + 1);

        var line = await RunBridgeUntilOutputAsync(
            new Dictionary<string, string?> { ["Bridge__DataDirectory"] = Path.Combine(temp.Path, "state") },
            "--everyday");
        var bindUrl = line["Listening bind:".Length..].Trim();
        var selectedPort = new Uri(bindUrl).Port;

        Assert.That(selectedPort, Is.GreaterThanOrEqualTo(EverydayPortSelector.DefaultFirstPort + 2));
        Assert.That(selectedPort, Is.LessThanOrEqualTo(EverydayPortSelector.DefaultLastPort));
    }

    [Test]
    public void ParseAcceptsOnlyTheExactEverydayFlag()
    {
        var launch = BridgeLaunchOptions.Parse(["--everyday"]);

        Assert.That(launch.Everyday, Is.True);
        Assert.That(launch.ShowHelp, Is.False);
        Assert.That(launch.AspNetCoreArguments, Is.Empty);
    }

    [Test]
    public void ParseLeavesExistingNonEverydayArgumentsUntouched()
    {
        var launch = BridgeLaunchOptions.Parse(["--urls", "http://127.0.0.1:6000"]);

        Assert.That(launch.Everyday, Is.False);
        Assert.That(launch.AspNetCoreArguments, Is.EqualTo(new[] { "--urls", "http://127.0.0.1:6000" }));
    }

    [TestCase("--everyday=true")]
    [TestCase("--everyday=false")]
    [TestCase("--everyday=1")]
    [TestCase("--everydayish")]
    public void ParseRejectsMalformedEverydayFlag(string argument)
    {
        var error = Assert.Throws<BridgeLaunchConfigurationException>(() => BridgeLaunchOptions.Parse([argument]));

        Assert.That(error!.Message, Does.Contain("--everyday"));
    }

    [Test]
    public void ParseRejectsDuplicateOrAdditionalEverydayLaunchOptions()
    {
        Assert.That(
            () => BridgeLaunchOptions.Parse(["--everyday", "--everyday"]),
            Throws.TypeOf<BridgeLaunchConfigurationException>().With.Message.Contains("once"));
        Assert.That(
            () => BridgeLaunchOptions.Parse(["--everyday", "--unknown"]),
            Throws.TypeOf<BridgeLaunchConfigurationException>().With.Message.Contains("only"));
        Assert.That(
            () => BridgeLaunchOptions.Parse(["--everyday", "value"]),
            Throws.TypeOf<BridgeLaunchConfigurationException>().With.Message.Contains("only"));
    }

    [Test]
    public void ParseSupportsStandaloneHelp()
    {
        var launch = BridgeLaunchOptions.Parse(["--help"]);

        Assert.That(launch.ShowHelp, Is.True);
        Assert.That(launch.Everyday, Is.False);
        Assert.That(launch.AspNetCoreArguments, Is.Empty);
    }

    [Test]
    public void SelectsFirstBindablePortAcrossOccupiedGaps()
    {
        var probe = new RecordingPortProbe(5080, 5082);

        var selected = EverydayPortSelector.Select(probe, firstPort: 5080, lastPort: 5083);

        Assert.That(selected, Is.EqualTo(5081));
        Assert.That(probe.Attempts, Is.EqualTo(new[] { 5080, 5081 }));
    }

    [Test]
    public void SelectorReportsExhaustionAndValidatesRangeWithoutOverflow()
    {
        var probe = new RecordingPortProbe(65535);
        var exhaustion = Assert.Throws<PortSelectionException>(() =>
            EverydayPortSelector.Select(probe, firstPort: 65535, lastPort: 65535));
        Assert.That(exhaustion!.Message, Does.Contain("65535"));

        Assert.That(
            () => EverydayPortSelector.Select(probe, firstPort: 65536, lastPort: 65536),
            Throws.TypeOf<PortSelectionException>());
        Assert.That(
            () => EverydayPortSelector.Select(probe, firstPort: 5081, lastPort: 5080),
            Throws.TypeOf<PortSelectionException>());
    }

    [Test]
    public void RealTcpProbeReleasesCheckSocket()
    {
        using var reservation = new TcpListener(IPAddress.Loopback, 0);
        reservation.Start();
        var port = ((IPEndPoint)reservation.LocalEndpoint).Port;
        reservation.Stop();

        var selected = EverydayPortSelector.Select(new TcpPortAvailabilityProbe(), port, port);

        Assert.That(selected, Is.EqualTo(port));
        using var listener = new TcpListener(IPAddress.Any, port);
        Assert.That(() => listener.Start(), Throws.Nothing);
        listener.Stop();
    }

    [Test]
    public void NoEverydayModePreservesExistingBindAndFixedPm3Configuration()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Bridge:BindUrl"] = "http://127.0.0.1:5090",
            ["Pm3:Port"] = "/dev/cu.usbmodem-test",
            ["Pm3:AutoDiscover"] = "false",
        }).Build();

        var options = BridgeOptions.FromConfiguration(configuration);

        Assert.That(options.BindUrl, Is.EqualTo("http://127.0.0.1:5090"));
        Assert.That(options.Pm3Port, Is.EqualTo("/dev/cu.usbmodem-test"));
        Assert.That(options.Pm3AutoDiscover, Is.False);
    }

    [Test]
    public void EverydayOptionsOwnBindAndPm3SettingsButPreserveDurablePathOverrides()
    {
        using var temp = new TemporaryDirectory();
        var configuredData = Path.Combine(temp.Path, "data");
        var configuredStore = Path.Combine(temp.Path, "store.json");
        var configuredIdentity = Path.Combine(temp.Path, "identity");
        var environmentData = Path.Combine(temp.Path, "environment-data");
        var environmentStore = Path.Combine(temp.Path, "environment-store.json");
        var environmentIdentity = Path.Combine(temp.Path, "environment-identity");
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Bridge:BindUrl"] = "not-a-bind-url",
            ["Bridge:DataDirectory"] = configuredData,
            ["BRIDGE_DATA_DIRECTORY"] = environmentData,
            ["Bridge:PairedClientsPath"] = configuredStore,
            ["BRIDGE_PAIRED_CLIENTS_PATH"] = environmentStore,
            ["Bridge:IdentityPath"] = configuredIdentity,
            ["BRIDGE_IDENTITY_PATH"] = environmentIdentity,
            ["Pm3:Port"] = "/dev/fixed-port",
            ["Pm3:AutoDiscover"] = "false",
        }).Build();

        var options = EverydayLaunchMode.CreateOptions(configuration, 5091);

        Assert.That(options.BindUrl, Is.EqualTo("http://0.0.0.0:5091"));
        Assert.That(options.Pm3AutoDiscover, Is.True);
        Assert.That(options.Pm3Port, Is.Null);
        Assert.That(options.DataDirectory, Is.EqualTo(configuredData));
        Assert.That(options.EffectivePairedClientsPath, Is.EqualTo(configuredStore));
        Assert.That(options.EffectiveBridgeIdentityPath, Is.EqualTo(configuredIdentity));
        Assert.That(() => options.Validate(), Throws.Nothing);
    }

    [Test]
    public void EverydayDoesNotInventDurablePathAliases()
    {
        using var temp = new TemporaryDirectory();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Bridge:DataDirectory"] = temp.Path,
            ["Bridge:StorePath"] = Path.Combine(temp.Path, "unsupported-store.json"),
            ["Bridge:BridgeIdentityPath"] = Path.Combine(temp.Path, "unsupported-identity"),
        }).Build();

        var options = EverydayLaunchMode.CreateOptions(configuration, 5092);

        Assert.That(options.EffectivePairedClientsPath,
            Is.EqualTo(Path.Combine(temp.Path, "paired-clients.json")));
        Assert.That(options.EffectiveBridgeIdentityPath,
            Is.EqualTo(Path.Combine(temp.Path, "bridge-id")));
    }

    [Test]
    public void BonjourUsesTheSelectedEverydayPort()
    {
        var options = EverydayLaunchMode.CreateOptions(
            new ConfigurationBuilder().Build(), 5092);

        var descriptor = BonjourAdvertisementFactory.Create(
            options,
            "0123456789ABCDEF0123456789ABCDEF",
            [IPAddress.Parse("192.168.1.20")])[0];

        Assert.That(descriptor.Port, Is.EqualTo(5092));
        Assert.That(descriptor.HttpUrl, Is.EqualTo(new Uri("http://192.168.1.20:5092/")));
        Assert.That(descriptor.Txt[BonjourServiceDescriptor.UrlTxtKey], Is.EqualTo("http://192.168.1.20:5092/"));
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

    private sealed class RecordingPortProbe(params int[] occupied) : IPortAvailabilityProbe
    {
        private readonly HashSet<int> _occupied = occupied.ToHashSet();
        public List<int> Attempts { get; } = [];
        public bool CanBind(int port)
        {
            Attempts.Add(port);
            return !_occupied.Contains(port);
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "ridesbridge-tests", Guid.NewGuid().ToString("N"));
        public TemporaryDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
