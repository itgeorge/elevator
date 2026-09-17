using System.Net;
using NUnit.Framework;
using RidesBridge;

namespace RidesBridge.Tests;

public sealed class BonjourAdvertisementTests
{
    private const string BridgeId = "0123456789ABCDEF0123456789ABCDEF";

    [Test]
    public void DescriptorHasExactPurposeAndAllowlistedNonSecretTxt()
    {
        var descriptor = BonjourAdvertisementFactory.Create(
            new BridgeOptions { BindUrl = "http://192.168.1.20:5080" },
            BridgeId,
            [IPAddress.Parse("192.168.1.20"), IPAddress.Parse("8.8.8.8"), IPAddress.Loopback])[0];

        Assert.That(descriptor.ServiceType, Is.EqualTo("_elevator-rides._tcp"));
        Assert.That(descriptor.HttpUrl, Is.EqualTo(new Uri("http://192.168.1.20:5080/")));
        Assert.That(descriptor.Address, Is.EqualTo(IPAddress.Parse("192.168.1.20")));
        Assert.That(descriptor.Port, Is.EqualTo(5080));
        Assert.That(descriptor.Txt.Keys, Is.EquivalentTo(new[] { "type", "bridgeId", "apiVersion", "url" }));
        Assert.That(descriptor.Txt["type"], Is.EqualTo("elevator-rides"));
        Assert.That(descriptor.Txt["bridgeId"], Is.EqualTo(BridgeId));
        Assert.That(descriptor.Txt["apiVersion"], Is.EqualTo(BridgeOptions.ApiVersion));
        Assert.That(descriptor.Txt["url"], Is.EqualTo("http://192.168.1.20:5080/"));
        Assert.That(descriptor.Txt.Values, Has.None.EqualTo("123456"));
        Assert.That(descriptor.Txt.Values.Any(value => value.Contains("bearer", StringComparison.OrdinalIgnoreCase)), Is.False);
        Assert.That(descriptor.Txt.Values.Any(value => value.Contains("verifier", StringComparison.OrdinalIgnoreCase)), Is.False);
    }

    [Test]
    public void ExactPrivateBindAdvertisesExactlyThatAddress()
    {
        var options = new BridgeOptions { BindUrl = "http://10.0.0.7:5080" };
        var descriptors = BonjourAdvertisementFactory.Create(options, BridgeId, [
            IPAddress.Parse("10.0.0.8"), IPAddress.Parse("192.168.1.10"), IPAddress.Loopback]);

        Assert.That(descriptors.Select(d => d.HttpUrl), Is.EqualTo(new[] { new Uri("http://10.0.0.7:5080/") }));
    }

    [Test]
    public void WildcardAdvertisesEachSortedDistinctPrivateIpv4AndNeverPublicOrLoopback()
    {
        var options = new BridgeOptions { BindUrl = "http://0.0.0.0:5080" };
        var descriptors = BonjourAdvertisementFactory.Create(options, BridgeId, [
            IPAddress.Parse("192.168.1.20"),
            IPAddress.Parse("127.0.0.1"),
            IPAddress.Parse("8.8.8.8"),
            IPAddress.Parse("10.0.0.2"),
            IPAddress.Parse("192.168.1.20"),
            IPAddress.Parse("169.254.10.4"),
        ]);

        Assert.That(descriptors.Select(d => d.HttpUrl.AbsoluteUri), Is.EqualTo(new[]
        {
            "http://10.0.0.2:5080/",
            "http://169.254.10.4:5080/",
            "http://192.168.1.20:5080/",
        }));
        Assert.That(descriptors.Select(d => d.InstanceName).Distinct().Count(), Is.EqualTo(3));
        Assert.That(descriptors.All(d => d.Txt["url"] == d.HttpUrl.AbsoluteUri), Is.True);
    }

    [TestCase("http://127.0.0.1:5080")]
    [TestCase("http://localhost:5080")]
    public void LoopbackBindProducesNoAdvertisement(string bindUrl)
    {
        var descriptors = BonjourAdvertisementFactory.Create(
            new BridgeOptions { BindUrl = bindUrl }, BridgeId, [IPAddress.Parse("192.168.1.2")]);

        Assert.That(descriptors, Is.Empty);
    }

    [Test]
    public void InstanceNamesAreStableForIdentityAndUrlAndDistinctAcrossUrls()
    {
        var options = new BridgeOptions { BindUrl = "http://0.0.0.0:5080" };
        var addresses = new[] { IPAddress.Parse("192.168.1.20"), IPAddress.Parse("10.0.0.2") };
        var first = BonjourAdvertisementFactory.Create(options, BridgeId, addresses);
        var second = BonjourAdvertisementFactory.Create(options, BridgeId, addresses.Reverse());

        Assert.That(first.Select(x => x.InstanceName), Is.EqualTo(second.Select(x => x.InstanceName)));
        Assert.That(first[0].InstanceName, Does.Contain(BridgeId.ToLowerInvariant()));
        Assert.That(first[0].InstanceName.Length, Is.LessThanOrEqualTo(63));
        Assert.That(first[0].InstanceName, Is.Not.EqualTo(first[1].InstanceName));
    }

    [Test]
    public void DescriptorRejectsNonPrivateOrNonCanonicalUrl()
    {
        Assert.That(
            () => new BonjourServiceDescriptor("instance", new Uri("http://8.8.8.8:5080/"), BridgeId),
            Throws.TypeOf<BridgeConfigurationException>());
        Assert.That(
            () => new BonjourServiceDescriptor("instance", new Uri("http://192.168.1.2:5080/path"), BridgeId),
            Throws.TypeOf<BridgeConfigurationException>());
    }
}

public sealed class BonjourLifecycleTests
{
    private const string BridgeId = "0123456789ABCDEF0123456789ABCDEF";

    [Test]
    public async Task StartStopIsIdempotentAndPublishesOnlyDuringHostLifetime()
    {
        using var temp = new TemporaryDirectory();
        var options = new BridgeOptions
        {
            BindUrl = "http://192.168.1.20:5080",
            BridgeIdentityPath = Path.Combine(temp.Path, "bridge-id"),
        };
        var identity = new BridgeIdentityService(options.EffectiveBridgeIdentityPath);
        var device = new FakeBridgePm3Device();
        var publisher = new RecordingBonjourPublisher();
        var lifecycle = new BridgeLifecycleService(device, new BridgeOperationGate(), options, identity, publisher);

        await lifecycle.StartAsync(CancellationToken.None);
        await lifecycle.StartAsync(CancellationToken.None);
        Assert.That(device.StartCalls, Is.EqualTo(1));
        Assert.That(publisher.StartCalls, Is.EqualTo(1));
        Assert.That(publisher.Services, Has.Count.EqualTo(1));

        await lifecycle.StopAsync(CancellationToken.None);
        await lifecycle.StopAsync(CancellationToken.None);
        Assert.That(publisher.StopCalls, Is.EqualTo(1));
        Assert.That(publisher.DisposeCalls, Is.EqualTo(1));
        Assert.That(device.Disposed, Is.True);
    }

    [Test]
    public async Task StopBeforeStartPreventsALaterStartRace()
    {
        var device = new FakeBridgePm3Device();
        var publisher = new RecordingBonjourPublisher();
        var lifecycle = new BridgeLifecycleService(device, new BridgeOperationGate(),
            new BridgeOptions { BindUrl = "http://127.0.0.1:5080", DataDirectory = Path.GetTempPath() },
            null, publisher);

        await lifecycle.StopAsync(CancellationToken.None);
        var error = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await lifecycle.StartAsync(CancellationToken.None));

        Assert.That(error!.Message, Does.Contain("stopped"));
        Assert.That(device.StartCalls, Is.EqualTo(0));
        Assert.That(publisher.StartCalls, Is.EqualTo(0));
    }

    [Test]
    public async Task BonjourFailureDoesNotPreventDirectIpLifecycle()
    {
        using var temp = new TemporaryDirectory();
        var options = new BridgeOptions
        {
            BindUrl = "http://192.168.1.20:5080",
            BridgeIdentityPath = Path.Combine(temp.Path, "bridge-id"),
        };
        var device = new FakeBridgePm3Device();
        var publisher = new RecordingBonjourPublisher { StartException = new InvalidOperationException("publisher unavailable") };
        var lifecycle = new BridgeLifecycleService(
            device,
            new BridgeOperationGate(),
            options,
            new BridgeIdentityService(options.EffectiveBridgeIdentityPath),
            publisher);

        await lifecycle.StartAsync(CancellationToken.None);
        await lifecycle.StopAsync(CancellationToken.None);

        Assert.That(device.StartCalls, Is.EqualTo(1));
        Assert.That(device.Disposed, Is.True);
        Assert.That(publisher.StartCalls, Is.EqualTo(1));
        Assert.That(publisher.StopCalls, Is.EqualTo(1));
        Assert.That(publisher.DisposeCalls, Is.EqualTo(1));
    }

    [Test]
    public void DeviceStartupFailureFailsClosedAndCleansUpWithoutSwallowingError()
    {
        using var temp = new TemporaryDirectory();
        var options = new BridgeOptions
        {
            BindUrl = "http://192.168.1.20:5080",
            BridgeIdentityPath = Path.Combine(temp.Path, "bridge-id"),
        };
        var device = new FakeBridgePm3Device { StartException = new InvalidOperationException("device unavailable") };
        var publisher = new RecordingBonjourPublisher();
        var lifecycle = new BridgeLifecycleService(
            device,
            new BridgeOperationGate(),
            options,
            new BridgeIdentityService(options.EffectiveBridgeIdentityPath),
            publisher);

        var error = Assert.ThrowsAsync<BridgeStartupException>(async () => await lifecycle.StartAsync(CancellationToken.None));

        Assert.That(error!.Message, Does.Contain("startup failed"));
        Assert.That(error.InnerException!.Message, Does.Contain("device unavailable"));
        Assert.That(publisher.StartCalls, Is.EqualTo(0));
        Assert.That(publisher.StopCalls, Is.EqualTo(1));
        Assert.That(publisher.DisposeCalls, Is.EqualTo(1));
        Assert.That(device.Disposed, Is.True);
    }

    private sealed class RecordingBonjourPublisher : IBonjourPublisher
    {
        public int StartCalls;
        public int StopCalls;
        public int DisposeCalls;
        public IReadOnlyList<BonjourServiceDescriptor> Services { get; private set; } = [];
        public Exception? StartException { get; init; }

        public Task StartAsync(IReadOnlyList<BonjourServiceDescriptor> services, CancellationToken cancellationToken = default)
        {
            StartCalls++;
            Services = services.ToList();
            if (StartException is not null) throw StartException;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            StopCalls++;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            DisposeCalls++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ridesbridge-tests", Guid.NewGuid().ToString("N"));
        public TemporaryDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
