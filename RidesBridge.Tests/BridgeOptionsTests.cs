using Microsoft.Extensions.Configuration;
using NUnit.Framework;
using RidesBridge;

namespace RidesBridge.Tests;

[TestFixture]
public sealed class BridgeOptionsTests
{
    [TestCase("http://127.0.0.1:5080")]
    [TestCase("http://localhost:8080")]
    [TestCase("http://192.168.1.20:5000")]
    [TestCase("http://0.0.0.0:5000")]
    public void ValidateBindUrl_AllowsLocalAndPrivateHttp(string url)
    {
        Assert.That(() => BridgeOptions.ValidateBindUrl(url), Throws.Nothing);
    }

    [TestCase("")]
    [TestCase("not-a-url")]
    [TestCase("https://192.168.1.2:5000")]
    [TestCase("http://192.168.1.2")]
    [TestCase("http://8.8.8.8:5000")]
    [TestCase("http://bridge.example:5000")]
    [TestCase("http://192.168.1.2:5000/api")]
    public void ValidateBindUrl_RejectsMalformedUnsupportedOrPublic(string url)
    {
        var ex = Assert.Throws<BridgeConfigurationException>(() => BridgeOptions.ValidateBindUrl(url));
        Assert.That(ex!.Message, Does.Contain("BindUrl"));
    }

    [Test]
    public void Validate_DefaultHardwareTimeoutsLeaveMarginBelowTabletDeadline()
    {
        var options = new BridgeOptions { BindUrl = "http://127.0.0.1:5080", DataDirectory = Path.GetTempPath() };

        Assert.That(options.OperationWaitTimeout, Is.EqualTo(TimeSpan.FromSeconds(5)));
        Assert.That(options.HardwareExecutionTimeout, Is.EqualTo(TimeSpan.FromSeconds(20)));
        Assert.That(options.HardwareRecoveryTimeout, Is.EqualTo(TimeSpan.FromSeconds(4)));
        Assert.That(options.OperationWaitTimeout + options.HardwareExecutionTimeout + options.HardwareRecoveryTimeout,
            Is.EqualTo(TimeSpan.FromSeconds(29)));
        Assert.That(() => options.Validate(), Throws.Nothing);
    }

    [TestCase(0)]
    [TestCase(30)]
    [TestCase(31)]
    public void Validate_RejectsHardwareExecutionTimeoutThatIsNotBelowTabletDeadline(double seconds)
    {
        var ex = Assert.Throws<BridgeConfigurationException>(() => new BridgeOptions
        {
            BindUrl = "http://127.0.0.1:5080",
            DataDirectory = Path.GetTempPath(),
            HardwareExecutionTimeout = TimeSpan.FromSeconds(seconds),
        }.Validate());

        Assert.That(ex!.Message, Does.Contain("HardwareExecutionTimeout"));
    }

    [TestCase(0)]
    [TestCase(30)]
    [TestCase(31)]
    public void Validate_RejectsGateWaitTimeoutThatCouldOutliveTabletRequest(double seconds)
    {
        var ex = Assert.Throws<BridgeConfigurationException>(() => new BridgeOptions
        {
            BindUrl = "http://127.0.0.1:5080",
            DataDirectory = Path.GetTempPath(),
            OperationWaitTimeout = TimeSpan.FromSeconds(seconds),
        }.Validate());

        Assert.That(ex!.Message, Does.Contain("OperationWaitTimeout"));
    }

    [TestCase("Bridge:PairingLifetimeSeconds", "not-a-number")]
    [TestCase("Bridge:OperationWaitTimeoutSeconds", "not-a-number")]
    [TestCase("Bridge:HardwareExecutionTimeoutSeconds", "not-a-number")]
    [TestCase("Bridge:HardwareRecoveryTimeoutSeconds", "not-a-number")]
    [TestCase("PM3_AUTO_DISCOVER", "not-a-boolean")]
    public void FromConfiguration_RejectsMalformedConfiguredValues(string key, string value)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [key] = value,
        }).Build();

        var ex = Assert.Throws<BridgeConfigurationException>(() => BridgeOptions.FromConfiguration(configuration));

        Assert.That(ex!.Message, Does.Contain(key));
    }

    [Test]
    public void FromConfiguration_ReadsHardwareTimeoutsIncludingRecoveryEnvironmentKey()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Bridge:OperationWaitTimeoutSeconds"] = "7",
            ["Bridge:HardwareExecutionTimeoutSeconds"] = "19",
            ["BRIDGE_HARDWARE_RECOVERY_TIMEOUT_SECONDS"] = "3",
        }).Build();

        var options = BridgeOptions.FromConfiguration(configuration);

        Assert.That(options.OperationWaitTimeout, Is.EqualTo(TimeSpan.FromSeconds(7)));
        Assert.That(options.HardwareExecutionTimeout, Is.EqualTo(TimeSpan.FromSeconds(19)));
        Assert.That(options.HardwareRecoveryTimeout, Is.EqualTo(TimeSpan.FromSeconds(3)));
    }

    [TestCase(0)]
    [TestCase(30)]
    [TestCase(31)]
    public void Validate_RejectsInvalidHardwareRecoveryTimeout(double seconds)
    {
        var ex = Assert.Throws<BridgeConfigurationException>(() => new BridgeOptions
        {
            BindUrl = "http://127.0.0.1:5080",
            DataDirectory = Path.GetTempPath(),
            HardwareRecoveryTimeout = TimeSpan.FromSeconds(seconds),
        }.Validate());

        Assert.That(ex!.Message, Does.Contain("HardwareRecoveryTimeout"));
    }

    [Test]
    public void Validate_RejectsCombinedHardwareBudgetsAtTabletDeadline()
    {
        var ex = Assert.Throws<BridgeConfigurationException>(() => new BridgeOptions
        {
            BindUrl = "http://127.0.0.1:5080",
            DataDirectory = Path.GetTempPath(),
            OperationWaitTimeout = TimeSpan.FromSeconds(5),
            HardwareExecutionTimeout = TimeSpan.FromSeconds(20),
            HardwareRecoveryTimeout = TimeSpan.FromSeconds(5),
        }.Validate());

        Assert.That(ex!.Message, Does.Contain("total less than 30 seconds"));
    }

    [Test]
    public void Validate_RejectsRelativeBridgeIdentityPath()
    {
        var ex = Assert.Throws<BridgeConfigurationException>(() => new BridgeOptions
        {
            BindUrl = "http://127.0.0.1:5080",
            DataDirectory = Path.GetTempPath(),
            BridgeIdentityPath = "bridge-id",
        }.Validate());
        Assert.That(ex!.Message, Does.Contain("BridgeIdentityPath"));
    }

    [Test]
    public void Validate_RequiresExplicitPortWhenAutoDiscoveryDisabled()
    {
        var ex = Assert.Throws<BridgeConfigurationException>(() => new BridgeOptions
        {
            BindUrl = "http://127.0.0.1:5080",
            DataDirectory = Path.GetTempPath(),
            Pm3AutoDiscover = false,
        }.Validate());
        Assert.That(ex!.Message, Does.Contain("Pm3Port"));
    }

    [Test]
    public void ReportedPrivateUrls_AreNonLoopbackIpv4AndUseConfiguredPort()
    {
        var urls = BridgeOptions.GetUsablePrivateIpv4Urls(54321, [
            System.Net.IPAddress.Parse("127.0.0.1"),
            System.Net.IPAddress.Parse("10.20.30.40"),
            System.Net.IPAddress.Parse("8.8.8.8"),
        ]);
        Assert.That(urls, Is.EqualTo(new[] { new Uri("http://10.20.30.40:54321/") }));
    }

    [Test]
    public void ReportedUrls_ForWildcardBindUseOnlyActualPrivateInterfaceAddresses()
    {
        var options = new BridgeOptions { BindUrl = "http://0.0.0.0:54321" };
        var urls = options.GetReportedUrls([
            System.Net.IPAddress.Parse("127.0.0.1"),
            System.Net.IPAddress.Parse("10.20.30.40"),
            System.Net.IPAddress.Parse("192.168.1.9"),
            System.Net.IPAddress.Parse("8.8.8.8"),
        ]);
        Assert.That(urls, Is.EqualTo(new[]
        {
            new Uri("http://10.20.30.40:54321/"),
            new Uri("http://192.168.1.9:54321/"),
        }));
    }

    [TestCase("http://192.168.1.9:54321")]
    [TestCase("http://10.20.30.40:54321")]
    public void ReportedUrls_ForSpecificPrivateBindAdvertiseOnlyThatBind(string bindUrl)
    {
        var options = new BridgeOptions { BindUrl = bindUrl };
        var urls = options.GetReportedUrls([
            System.Net.IPAddress.Parse("10.20.30.40"),
            System.Net.IPAddress.Parse("192.168.1.9"),
        ]);
        Assert.That(urls, Is.EqualTo(new[] { new Uri(bindUrl + "/") }));
    }

    [TestCase("http://127.0.0.1:54321")]
    [TestCase("http://localhost:54321")]
    public void ReportedUrls_ForLoopbackBindAdvertiseNoPrivateUrl(string bindUrl)
    {
        var options = new BridgeOptions { BindUrl = bindUrl };
        Assert.That(options.GetReportedUrls([
            System.Net.IPAddress.Parse("10.20.30.40"),
        ]), Is.Empty);
    }
}
