using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using RidesBridge;

namespace RidesBridge.Tests;

[TestFixture]
public sealed class PairingPayloadTests
{
    [Test]
    public void PayloadRoundTripsEscapedJsonAndHasOnlyPublicContractFields()
    {
        var payload = new PairingPayload(PairingPayload.CurrentVersion, new Uri("http://192.168.1.20:5080/"), "012345", DateTimeOffset.Parse("2030-01-02T03:04:05Z"), "0123456789ABCDEF0123456789ABCDEF", BridgeOptions.ApiVersion);
        var json = PairingPayload.Serialize(payload);
        var escaped = json.Replace("http://", "http:\\/\\/", StringComparison.Ordinal);
        Assert.That(PairingPayload.TryParse(escaped, new TestClock(DateTimeOffset.Parse("2029-01-01T00:00:00Z")), out var parsed, out var error), Is.True, error);
        Assert.That(parsed, Is.EqualTo(payload));
        using var document = JsonDocument.Parse(json);
        Assert.That(document.RootElement.EnumerateObject().Select(p => p.Name), Is.EquivalentTo(new[] { "type", "version", "url", "pin", "expiresAt", "bridgeId", "apiVersion" }));
        Assert.That(document.RootElement.GetProperty("type").GetString(), Is.EqualTo(PairingPayload.CurrentType));
    }

    [Test]
    public void PayloadHasExactTypeAndVersionAndRejectsBearerOrUnknownMaterial()
    {
        var payload = CreatePayload();
        var json = PairingPayload.Serialize(payload);
        Assert.That(json, Does.Contain($"\"type\":\"{PairingPayload.CurrentType}\""));
        Assert.That(json, Does.Not.Contain("accessToken"));
        Assert.That(json, Does.Not.Contain("bearer"));
        Assert.That(json, Does.Not.Contain("verifier"));
        var missingType = json.Replace($"\"type\":\"{PairingPayload.CurrentType}\",", string.Empty, StringComparison.Ordinal);
        Assert.That(PairingPayload.TryParse(missingType, new TestClock(DateTimeOffset.UtcNow), out _, out var missingTypeError), Is.False);
        Assert.That(missingTypeError, Does.Contain("property"));
        var wrongType = json.Replace(PairingPayload.CurrentType, "other-pairing", StringComparison.Ordinal);
        Assert.That(PairingPayload.TryParse(wrongType, new TestClock(DateTimeOffset.UtcNow), out _, out var typeError), Is.False);
        Assert.That(typeError, Does.Contain("type"));
        var wrongVersion = json.Replace("\"v1\"", "\"v2\"", StringComparison.Ordinal);
        Assert.That(PairingPayload.TryParse(wrongVersion, new TestClock(DateTimeOffset.UtcNow), out _, out var versionError), Is.False);
        Assert.That(versionError, Does.Contain("version"));
        foreach (var field in new[] { "accessToken", "verifier", "nonce" })
        {
            var withUnknown = json.TrimEnd('}') + $",\"{field}\":\"secret\"}}";
            Assert.That(PairingPayload.TryParse(withUnknown, new TestClock(DateTimeOffset.UtcNow), out _, out var extraError), Is.False);
            Assert.That(extraError, Does.Contain("property"));
        }
    }

    [Test]
    public void PayloadExpirationAndOneTimeRedemptionRemainPairingCodeServiceOwned()
    {
        var clock = new TestClock(DateTimeOffset.UtcNow);
        var pairing = new PairingCodeService(TimeSpan.FromSeconds(10), clock);
        var code = pairing.GetActiveCode()!;
        var payload = new PairingPayload(PairingPayload.CurrentVersion, new Uri("http://192.168.1.20:5080/"), code.Value, code.ExpiresAt, "0123456789ABCDEF0123456789ABCDEF", BridgeOptions.ApiVersion);
        Assert.That(PairingPayload.TryParse(PairingPayload.Serialize(payload), clock, out _, out _), Is.True);
        Assert.That(pairing.TryRedeem(code.Value), Is.True);
        Assert.That(pairing.TryRedeem(code.Value), Is.False);
        clock.Advance(TimeSpan.FromSeconds(11));
        Assert.That(PairingPayload.TryParse(PairingPayload.Serialize(payload), clock, out _, out var error), Is.False);
        Assert.That(error, Does.Contain("expired"));
    }

    [Test]
    public void PayloadFactoryUsesOnlyReportedPrivateUrlsAndNeverAdvertisesWildcardOrLoopback()
    {
        var options = new BridgeOptions { BindUrl = "http://0.0.0.0:5080" };
        var code = new PairingCode("123456", DateTimeOffset.UtcNow.AddMinutes(1));
        var payloads = PairingPayloadFactory.CreateForReportedUrls(options, code, "0123456789ABCDEF0123456789ABCDEF", new[] { IPAddress.Parse("127.0.0.1"), IPAddress.Parse("10.20.30.40"), IPAddress.Parse("192.168.1.9"), IPAddress.Parse("8.8.8.8") });
        Assert.That(payloads.Select(p => p.BridgeUrl), Is.EqualTo(new[] { new Uri("http://10.20.30.40:5080/"), new Uri("http://192.168.1.9:5080/") }));
        Assert.That(payloads.All(p => p.Pin == code.Value), Is.True);
        var loopback = new BridgeOptions { BindUrl = "http://127.0.0.1:5080" };
        Assert.That(PairingPayloadFactory.CreateForReportedUrls(loopback, code, "0123456789ABCDEF0123456789ABCDEF", [IPAddress.Parse("10.0.0.1")]), Is.Empty);
    }

    [Test]
    public void PairingUrlValidationAllowsOnlyPrivateIpv4WithoutAuthorityExtras()
    {
        Assert.That(PairingPayload.ValidateBridgeUrl(new Uri("http://192.168.1.20:5080/")), Is.EqualTo(new Uri("http://192.168.1.20:5080/")));
        foreach (var hostile in new[]
        {
            "http://localhost:5080/",
            "http://127.0.0.1:5080/",
            "http://0.0.0.0:5080/",
            "http://255.255.255.255:5080/",
            "http://8.8.8.8:5080/",
            "http://bridge.example:5080/",
            "http://[::1]:5080/",
            "http://192.168.1.20:5080/path",
            "http://192.168.1.20:5080/?next=secret",
            "http://user:pass@192.168.1.20:5080/",
            "https://192.168.1.20:5080/",
        })
            Assert.That(() => PairingPayload.ValidateBridgeUrl(new Uri(hostile)), Throws.TypeOf<BridgeConfigurationException>(), hostile);
    }

    [Test]
    public void ParsedQrPayloadRejectsHostileUrlHostsAndAuthorities()
    {
        var json = PairingPayload.Serialize(CreatePayload());
        foreach (var hostile in new[]
        {
            "http://localhost:5080/",
            "http://127.0.0.1:5080/",
            "http://0.0.0.0:5080/",
            "http://8.8.8.8:5080/",
            "http://bridge.example:5080/",
            "http://[::1]:5080/",
            "http://user:pass@192.168.1.20:5080/",
            "http://192.168.1.20:5080/unsafe",
        })
        {
            using var source = JsonDocument.Parse(json);
            var rewritten = source.RootElement.EnumerateObject()
                .Select(property => property.Name == "url"
                    ? $"\"url\":\"{hostile}\""
                    : $"\"{property.Name}\":{property.Value.GetRawText()}");
            var hostileJson = "{" + string.Join(",", rewritten) + "}";
            Assert.That(PairingPayload.TryParse(hostileJson, out _, out var error), Is.False, hostile);
            Assert.That(error, Does.Contain("Pairing URL"), hostile);
        }
    }

    [Test]
    public void BridgeIdentityIsStableAcrossProviderRestartAndCleansItsPublishedCandidate()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ridesbridge-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "bridge-id");
        try
        {
            var first = new BridgeIdentityService(path);
            var second = new BridgeIdentityService(path);
            Assert.That(first.Id, Is.EqualTo(second.Id));
            Assert.That(first.Id, Does.Match("^[0-9A-F]{32}$"));
            Assert.That(File.ReadAllText(path), Does.Not.Contain("token"));
            Assert.That(Directory.GetFiles(directory, "bridge-id.*.tmp"), Is.Empty);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Test]
    public void ConcurrentIdentityConstructorsPublishOneCompleteIdentityAndCleanOwnTemps()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ridesbridge-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "bridge-id");
        try
        {
            var identities = Enumerable.Range(0, 32)
                .AsParallel()
                .Select(_ => new BridgeIdentityService(path).Id)
                .ToArray();
            Assert.That(identities.Distinct().Count(), Is.EqualTo(1));
            Assert.That(File.ReadAllText(path).Trim(), Is.EqualTo(identities[0]));
            Assert.That(Directory.GetFiles(directory, "bridge-id.*.tmp"), Is.Empty);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Test]
    public void OrphanIdentityTempIsNeverReadAsFinalAndInvalidFinalFailsSafely()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ridesbridge-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "bridge-id");
        try
        {
            Directory.CreateDirectory(directory);
            var orphan = path + ".orphan.tmp";
            File.WriteAllText(orphan, "BADPARTIAL");
            var identity = new BridgeIdentityService(path).Id;
            Assert.That(identity, Does.Match("^[0-9A-F]{32}$"));
            Assert.That(File.Exists(orphan), Is.True, "an unpublished candidate belongs to no constructor and must not be deleted");

            File.WriteAllText(path, "not-an-identity\n");
            Assert.That(() => new BridgeIdentityService(path), Throws.TypeOf<BridgeConfigurationException>());
            Assert.That(File.ReadAllText(path), Is.EqualTo("not-an-identity\n"));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Test]
    public void PairingQrArtifactIsLosslessPngSizedForTheCompleteQuietZonedMatrixAndOwnerOnly()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ridesbridge-tests", Guid.NewGuid().ToString("N"));
        var payload = CreatePayload() with { Pin = "123456" };
        PairingQrArtifactLease? lease = null;
        try
        {
            lease = new PairingQrArtifactLease(
                new BridgeOptions { BindUrl = "http://127.0.0.1:5080", PairingQrArtifactDirectory = directory },
                [payload]);
            var path = lease.ArtifactPaths.Single();
            var bytes = File.ReadAllBytes(path);
            Assert.That(bytes.AsSpan(0, 8).ToArray(), Is.EqualTo(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }));
            Assert.That(BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(16, 4)), Is.EqualTo((uint)(new TerminalQrRenderer().GetLogicalMatrix(PairingPayload.Serialize(payload)).Length * PairingQrArtifactLease.PixelsPerModule)));
            Assert.That(BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(20, 4)), Is.EqualTo(BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(16, 4))));
            Assert.That(BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(8, 4)), Is.EqualTo(13u));
            Assert.That(System.Text.Encoding.ASCII.GetString(bytes, 12, 4), Is.EqualTo("IHDR"));
            Assert.That(path, Does.Not.Contain(payload.Pin));
            Assert.That(path, Does.Not.Contain("token"));
            Assert.That(path, Does.Not.Contain("verifier"));
            Assert.That(Directory.GetFiles(directory, "*.tmp"), Is.Empty);
            if (!OperatingSystem.IsWindows())
            {
                var mode = File.GetUnixFileMode(path);
                Assert.That(mode, Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite));
            }
        }
        finally
        {
            lease?.Dispose();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Test]
    public async Task PairingQrArtifactLeaseExpiresWithInjectedClockAndCancellationIsAwaitable()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ridesbridge-tests", Guid.NewGuid().ToString("N"));
        var clock = new TestClock(DateTimeOffset.UtcNow);
        var payload = CreatePayload() with { ExpiresAt = clock.GetUtcNow().AddSeconds(5) };
        PairingQrArtifactLease? lease = null;
        using var cancellation = new CancellationTokenSource();
        Task? watcher = null;
        try
        {
            lease = new PairingQrArtifactLease(
                new BridgeOptions { BindUrl = "http://127.0.0.1:5080", PairingQrArtifactDirectory = directory },
                [payload],
                clock,
                (_, cancellationToken) => Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken));
            watcher = lease.RunAsync(cancellation.Token);
            clock.Advance(TimeSpan.FromSeconds(6));
            Assert.That(lease.CleanupIfExpired(), Is.True);
            Assert.That(lease.ArtifactPaths.All(path => !File.Exists(path)), Is.True);
            Assert.That(watcher.IsCompleted, Is.False, "RunAsync remains owned by its caller until cancellation.");
            cancellation.Cancel();
            await watcher;
        }
        finally
        {
            cancellation.Cancel();
            if (watcher is not null) await watcher;
            lease?.Dispose();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Test]
    public void ArtifactStartupReplacesOnlyItsPredictableFilesAndLoopbackCreatesNothing()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ridesbridge-tests", Guid.NewGuid().ToString("N"));
        var unrelated = Path.Combine(directory, "keep-me.txt");
        var similarlyNamedUnrelated = Path.Combine(directory, PairingQrArtifactLease.ArtifactFilePrefix + "not-a-private-url.png");
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(unrelated, "unrelated");
            File.WriteAllText(similarlyNamedUnrelated, "unrelated");
            var options = new BridgeOptions { BindUrl = "http://0.0.0.0:5080", PairingQrArtifactDirectory = directory };
            using (var first = PairingQrArtifactLease.CreateForReportedUrls(
                       options,
                       new PairingCode("123456", DateTimeOffset.UtcNow.AddMinutes(1)),
                       "0123456789ABCDEF0123456789ABCDEF",
                       [IPAddress.Parse("10.0.0.1"), IPAddress.Parse("192.168.1.2")])!)
            {
                Assert.That(first.ArtifactPaths, Has.Count.EqualTo(2));
            }
            var stalePath = Path.Combine(directory, PairingQrArtifactLease.GetArtifactFileName(new Uri("http://10.0.0.1:5080/")));
            File.WriteAllBytes(stalePath, [1, 2, 3]);

            using var second = PairingQrArtifactLease.CreateForReportedUrls(
                options,
                new PairingCode("654321", DateTimeOffset.UtcNow.AddMinutes(1)),
                "0123456789ABCDEF0123456789ABCDEF",
                [IPAddress.Parse("10.0.0.1"), IPAddress.Parse("192.168.1.2")]);
            Assert.That(second, Is.Not.Null);
            var secondLease = second!;
            Assert.That(secondLease.ArtifactPaths.All(File.Exists), Is.True);
            Assert.That(Directory.GetFiles(directory, PairingQrArtifactLease.ArtifactFilePrefix + "*.png"), Has.Length.EqualTo(3));
            Assert.That(File.Exists(unrelated), Is.True);
            Assert.That(File.Exists(similarlyNamedUnrelated), Is.True);
            secondLease.Dispose();
            Assert.That(secondLease.ArtifactPaths.All(path => !File.Exists(path)), Is.True);

            var loopbackDirectory = Path.Combine(directory, "loopback");
            var loopback = PairingQrArtifactLease.CreateForReportedUrls(
                new BridgeOptions { BindUrl = "http://127.0.0.1:5080", PairingQrArtifactDirectory = loopbackDirectory },
                new PairingCode("111111", DateTimeOffset.UtcNow.AddMinutes(1)),
                "0123456789ABCDEF0123456789ABCDEF",
                [IPAddress.Parse("10.0.0.1")]);
            Assert.That(loopback, Is.Null);
            Assert.That(Directory.Exists(loopbackDirectory), Is.False);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Test]
    public void ArtifactDisplayPrintsPathsAndRetainsThePinFallbackWithoutPrintingPayloadBytes()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ridesbridge-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var options = new BridgeOptions { BindUrl = "http://0.0.0.0:5080", PairingQrArtifactDirectory = directory };
            var pairing = new PairingCodeService(TimeSpan.FromMinutes(1));
            var code = pairing.GetActiveCode()!;
            using var lease = PairingQrArtifactLease.CreateForReportedUrls(
                options,
                code,
                "0123456789ABCDEF0123456789ABCDEF",
                [IPAddress.Parse("10.0.0.1"), IPAddress.Parse("192.168.1.2")]);
            using var output = new StringWriter();
            BridgeTerminalDisplay.Write(options, pairing, "0123456789ABCDEF0123456789ABCDEF", output, artifactLease: lease);
            var text = output.ToString();
            Assert.That(text, Does.Contain($"Pairing PIN: {code.Value}"));
            Assert.That(text, Does.Contain("Pairing QR artifact: "));
            Assert.That(text, Does.Not.Contain("\u001b["));
            Assert.That(text, Does.Not.Contain("accessToken"));
            Assert.That(text, Does.Not.Contain("verifier"));
            Assert.That(text, Does.Not.Contain(PairingPayload.Serialize(lease!.Payloads[0])));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Test]
    public void TerminalRendererUsesExplicitColorsAndPreservesQRCoderQuietZone()
    {
        const string payload = "hello";
        var renderer = new TerminalQrRenderer();
        var first = renderer.Render(payload);
        Assert.That(first, Is.EqualTo(renderer.Render(payload)));
        Assert.That(first, Does.Not.Contain(payload));
        Assert.That(first, Does.Contain("\u001b[30m"));
        Assert.That(first, Does.Contain("\u001b[37m"));
        Assert.That(first, Does.Contain("\u001b[40m"));
        Assert.That(first, Does.Contain("\u001b[47m"));
        Assert.That(first, Does.Contain("\u2580"));

        var logical = renderer.GetLogicalMatrix(payload);
        Assert.That(logical.Length, Is.EqualTo(29), "QRCoder v1 is 21 modules plus its existing four-module quiet zone on each side");
        Assert.That(logical.All(row => row.Length == 29), Is.True);
        Assert.That(logical.Take(4).SelectMany(row => row), Is.All.False);
        Assert.That(logical.Skip(4).All(row => row.Take(4).All(cell => !cell) && row.Skip(25).All(cell => !cell)), Is.True);

        var parsed = ParseRenderedMatrix(first, logical[0].Length, logical.Length);
        Assert.That(parsed, Is.EqualTo(logical));
        Assert.That(() => renderer.Render(string.Empty), Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void FullPairingPayloadFitsTypicalTerminalAndReconstructsExactly()
    {
        var renderer = new TerminalQrRenderer();
        var serialized = PairingPayload.Serialize(CreatePayload());
        var source = renderer.GetLogicalMatrix(serialized);
        var rendered = renderer.Render(serialized);
        var lines = rendered.Split('\n');

        Assert.That(source.Length, Is.EqualTo(65));
        Assert.That(lines.Length, Is.LessThanOrEqualTo(35));
        Assert.That(lines.Max(VisibleWidth), Is.LessThanOrEqualTo(70));
        Assert.That(rendered, Does.Contain("\u001b[30m"));
        Assert.That(rendered, Does.Contain("\u001b[37m"));
        Assert.That(rendered, Does.Contain("\u001b[40m"));
        Assert.That(rendered, Does.Contain("\u001b[47m"));
        Assert.That(ParseRenderedMatrix(rendered, source[0].Length, source.Length), Is.EqualTo(source));
    }

    [Test]
    public void TerminalDisplayRendersOneQrPerReportedPrivateUrlWithoutChangingTheCurrentCode()
    {
        var options = new BridgeOptions { BindUrl = "http://0.0.0.0:5080" };
        var pairing = new PairingCodeService(TimeSpan.FromMinutes(1));
        var code = pairing.GetActiveCode()!;
        using var output = new StringWriter();
        BridgeTerminalDisplay.Write(options, pairing, "0123456789ABCDEF0123456789ABCDEF", output, [IPAddress.Parse("10.0.0.1"), IPAddress.Parse("192.168.1.2")]);
        var text = output.ToString();
        Assert.That(text, Does.Contain("Reachable private URL: http://10.0.0.1:5080/"));
        Assert.That(text, Does.Contain("Reachable private URL: http://192.168.1.2:5080/"));
        Assert.That(text, Does.Contain("Pairing QR for http://10.0.0.1:5080/"));
        Assert.That(text, Does.Contain("Pairing QR for http://192.168.1.2:5080/"));
        Assert.That(pairing.GetActiveCode(), Is.EqualTo(code));
    }

    [Test]
    public void TerminalDisplayKeepsManualFlowAndIsSafeForLoopbackOrNoninteractiveTests()
    {
        var options = new BridgeOptions { BindUrl = "http://127.0.0.1:5080" };
        var pairing = new PairingCodeService(TimeSpan.FromMinutes(1));
        using var output = new StringWriter();
        BridgeTerminalDisplay.Write(options, pairing, "0123456789ABCDEF0123456789ABCDEF", output, [IPAddress.Parse("10.0.0.1")]);
        Assert.That(output.ToString(), Does.Contain("Pairing PIN:"));
        Assert.That(output.ToString(), Does.Contain("No non-loopback private URL"));
        Assert.That(output.ToString(), Does.Not.Contain("Pairing QR"));
    }

    [Test]
    public async Task ReusingTheSameQrPayloadThroughPairEndpointIsRejectedByTheServerOwnedOneTimePin()
    {
        await using var host = await BridgeTestHost.CreateAsync();
        var pairingCode = host.App.Services.GetRequiredService<PairingCodeService>().GetActiveCode()!;
        var payload = new PairingPayload(
            PairingPayload.CurrentVersion,
            new Uri("http://192.168.1.20:5080/"),
            pairingCode.Value,
            pairingCode.ExpiresAt,
            "0123456789ABCDEF0123456789ABCDEF",
            BridgeOptions.ApiVersion);
        Assert.That(PairingPayload.TryParse(PairingPayload.Serialize(payload), out var parsed, out var error), Is.True, error);

        // The PIN is server-owned and one-time; a separate QR nonce is unnecessary.
        var first = await host.Client.PostAsJsonAsync("/api/v1/pair", new PairRequest(parsed!.Pin));
        var second = await host.Client.PostAsJsonAsync("/api/v1/pair", new PairRequest(parsed.Pin));
        Assert.That(first.StatusCode, Is.EqualTo(System.Net.HttpStatusCode.OK));
        Assert.That(second.StatusCode, Is.EqualTo(System.Net.HttpStatusCode.Unauthorized));
    }

    private static bool[][] ParseRenderedMatrix(string rendered, int expectedWidth, int expectedHeight)
    {
        const string darkForeground = "\u001b[30m";
        const string lightForeground = "\u001b[37m";
        const string darkBackground = "\u001b[40m";
        const string lightBackground = "\u001b[47m";
        const string glyph = "\u2580";
        const string reset = "\u001b[0m";
        var lines = rendered.Split('\n');
        Assert.That(lines.Length, Is.EqualTo((expectedHeight + 1) / 2));
        var rows = new List<bool[]>();

        foreach (var line in lines)
        {
            var top = new List<bool>();
            var bottom = new List<bool>();
            for (var offset = 0; offset < line.Length;)
            {
                var topDark = line.AsSpan(offset).StartsWith("\u001b[30m", StringComparison.Ordinal);
                var topLight = line.AsSpan(offset).StartsWith("\u001b[37m", StringComparison.Ordinal);
                if (!topDark && !topLight)
                    throw new AssertionException($"Missing explicit foreground color at offset {offset}.");
                offset += topDark ? darkForeground.Length : lightForeground.Length;

                var bottomDark = line.AsSpan(offset).StartsWith(darkBackground, StringComparison.Ordinal);
                var bottomLight = line.AsSpan(offset).StartsWith(lightBackground, StringComparison.Ordinal);
                if (!bottomDark && !bottomLight)
                    throw new AssertionException($"Missing explicit background color at offset {offset}.");
                offset += bottomDark ? darkBackground.Length : lightBackground.Length;

                if (!line.AsSpan(offset).StartsWith(glyph, StringComparison.Ordinal))
                    throw new AssertionException($"Unexpected terminal QR glyph at offset {offset}.");
                offset += glyph.Length;
                if (!line.AsSpan(offset).StartsWith(reset, StringComparison.Ordinal))
                    throw new AssertionException($"Missing terminal QR reset at offset {offset}.");
                offset += reset.Length;
                top.Add(topDark);
                bottom.Add(bottomDark);
            }

            Assert.That(top.Count, Is.EqualTo(expectedWidth));
            rows.Add(top.ToArray());
            if (rows.Count < expectedHeight)
                rows.Add(bottom.ToArray());
            else
                Assert.That(bottom, Is.All.False, "An odd-height QR must use a light synthetic bottom row.");
        }

        Assert.That(rows.Count, Is.EqualTo(expectedHeight));
        return rows.ToArray();
    }

    private static int VisibleWidth(string line)
    {
        var width = 0;
        for (var offset = 0; offset < line.Length;)
        {
            if (line[offset] == '\u001b')
            {
                var terminator = line.IndexOf('m', offset);
                Assert.That(terminator, Is.GreaterThan(offset));
                offset = terminator + 1;
            }
            else
            {
                width++;
                offset++;
            }
        }
        return width;
    }

    private static PairingPayload CreatePayload() => new(PairingPayload.CurrentVersion, new Uri("http://192.168.1.20:5080/"), "123456", DateTimeOffset.UtcNow.AddMinutes(1), "0123456789ABCDEF0123456789ABCDEF", BridgeOptions.ApiVersion);
}
