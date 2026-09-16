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
