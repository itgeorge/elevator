using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using RidesBridge;

namespace RidesBridge.Tests;

[TestFixture]
public sealed class PairRelocationProofTests
{
    private const string BridgeId = "0123456789ABCDEF0123456789ABCDEF";
    private const string Url = "http://192.168.1.20:5080/";
    private const string Nonce = "000102030405060708090A0B0C0D0E0F101112131415161718191A1B1C1D1E1F";
    private const string Bearer = "bearer-vector-1🔐";

    [Test]
    public void CryptoVectorUsesUtf8VerifierAndExactNulSeparatedV1Transcripts()
    {
        Assert.That(PairRelocationProof.ComputeLocator(Bearer), Is.EqualTo(
            "5180065A046E3663B726F1677AE2C12DDB3A48B3E5FCE369AB218A538A2BF275"));
        Assert.That(PairRelocationProof.ComputeProof(Bearer, Nonce, BridgeId, Url), Is.EqualTo(
            "A124C6B859B167BBFD1FE1F03D21B4DDB089A169855588A7FEC19DE030CE3FB1"));
    }

    [Test]
    public void CryptoInputsRequireUppercaseFixedLengthHexNonceAndLocator()
    {
        Assert.That(PairRelocationProof.TryDecodeUpperHex(Nonce, 32, out var bytes), Is.True);
        Assert.That(bytes, Is.EqualTo(Enumerable.Range(0, 32).Select(i => (byte)i).ToArray()));
        Assert.That(PairRelocationProof.TryDecodeUpperHex(Nonce.ToLowerInvariant(), 32, out _), Is.False);
        Assert.That(PairRelocationProof.TryDecodeUpperHex(Nonce[..^2], 32, out _), Is.False);
        Assert.That(PairRelocationProof.TryDecodeUpperHex("Z" + Nonce[1..], 32, out _), Is.False);
    }

    [Test]
    public async Task EndpointReturnsExactProofContractAndIsStatelessAndUnauthenticated()
    {
        await using var host = await BridgeTestHost.CreateAsync(options: new BridgeOptions
        {
            BindUrl = Url.TrimEnd('/'),
        });
        var bearer = await host.PairAsync();
        var nonce = Nonce;
        var request = new PairProofRequest(PairRelocationProof.ComputeLocator(bearer), nonce, Url);

        // An Authorization header must not be required or used by this route.
        host.Client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", "Bearer deliberately-ignored");
        var actualBridgeId = host.App.Services.GetRequiredService<BridgeIdentityService>().Id;
        Assert.That(host.App.Services.GetRequiredService<IPairedClientRelocationStore>().TryCreateProof(
            request.Locator!, Convert.FromHexString(nonce), actualBridgeId,
            Url, BridgeOptions.ApiVersion, out _), Is.True);
        var first = await host.Client.PostAsJsonAsync("/api/v1/pair/proof", request);
        var second = await host.Client.PostAsJsonAsync("/api/v1/pair/proof", request);

        Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var body = await first.Content.ReadFromJsonAsync<PairProofResponse>();
        Assert.That(body, Is.EqualTo(new PairProofResponse(
            actualBridgeId, BridgeOptions.ApiVersion, nonce,
            PairRelocationProof.ComputeProof(bearer, nonce, actualBridgeId, Url))));
        Assert.That(await second.Content.ReadAsStringAsync(), Is.EqualTo(await first.Content.ReadAsStringAsync()));
        Assert.That(host.Device.ReadCalls, Is.EqualTo(0));

        using var json = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
        Assert.That(json.RootElement.EnumerateObject().Select(property => property.Name),
            Is.EqualTo(new[] { "bridgeId", "apiVersion", "nonce", "proof" }));
    }

    [Test]
    public async Task WrongLocatorRevokedWrongUrlAndMalformedRequestsAreSameGenericFailure()
    {
        await using var host = await BridgeTestHost.CreateAsync(options: new BridgeOptions
        {
            BindUrl = Url.TrimEnd('/'),
        });
        var bearer = await host.PairAsync();
        var valid = new PairProofRequest(PairRelocationProof.ComputeLocator(bearer), Nonce, Url);

        var wrongLocator = await host.Client.PostAsJsonAsync("/api/v1/pair/proof",
            valid with { Locator = new string('A', 64) });
        var wrongUrl = await host.Client.PostAsJsonAsync("/api/v1/pair/proof",
            valid with { Url = "http://192.168.1.21:5080/" });
        var malformedNonce = await host.Client.PostAsJsonAsync("/api/v1/pair/proof",
            valid with { Nonce = Nonce.ToLowerInvariant() });
        var malformedJson = await host.Client.PostAsync("/api/v1/pair/proof",
            new StringContent("{\"locator\":\"secret\"}"));
        var unknownField = await host.Client.PostAsync("/api/v1/pair/proof",
            new StringContent(JsonSerializer.Serialize(new
            {
                locator = valid.Locator,
                nonce = valid.Nonce,
                url = valid.Url,
                extra = "rejected",
            })));

        var expected = await wrongLocator.Content.ReadAsStringAsync();
        foreach (var response in new[] { wrongUrl, malformedNonce, malformedJson, unknownField })
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(await response.Content.ReadAsStringAsync(), Is.EqualTo(expected));
        }

        host.Client.DefaultRequestHeaders.Authorization = new("Bearer", bearer);
        Assert.That((await host.Client.PostAsync("/api/v1/pair/revoke", null)).StatusCode,
            Is.EqualTo(HttpStatusCode.NoContent));
        var revoked = await host.Client.PostAsJsonAsync("/api/v1/pair/proof", valid);
        Assert.That(revoked.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(await revoked.Content.ReadAsStringAsync(), Is.EqualTo(expected));
        Assert.That(host.Device.ReadCalls, Is.EqualTo(0));
    }

    [Test]
    public async Task EndpointLogsOnlyPathAndStatusAndNeverRequestSecrets()
    {
        var logs = new CapturingLoggerProvider();
        await using var host = await BridgeTestHost.CreateAsync(
            loggerProvider: logs,
            options: new BridgeOptions { BindUrl = Url.TrimEnd('/') });
        var bearer = await host.PairAsync();
        var request = new PairProofRequest(
            PairRelocationProof.ComputeLocator(bearer), Nonce, Url);

        var response = await host.Client.PostAsJsonAsync("/api/v1/pair/proof", request);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var text = string.Join("\n", logs.Messages);
        var verifier = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(bearer)));
        Assert.That(text, Does.Contain("/api/v1/pair/proof"));
        Assert.That(text, Does.Not.Contain(bearer));
        Assert.That(text, Does.Not.Contain(verifier));
    }

    [Test]
    public void WildcardReportedUrlMembershipIsAddressSpecificAndSorted()
    {
        var options = new BridgeOptions { BindUrl = "http://0.0.0.0:5080" };
        var urls = options.GetReportedUrls([
            IPAddress.Parse("192.168.1.20"), IPAddress.Parse("10.0.0.2"), IPAddress.Loopback]);
        Assert.That(urls.Select(url => url.AbsoluteUri), Is.EqualTo(new[]
        {
            "http://10.0.0.2:5080/", "http://192.168.1.20:5080/",
        }));
        Assert.That(urls, Does.Not.Contain(new Uri("http://192.168.1.21:5080/")));
    }

    [Test]
    public void ExistingVerifierFileRemainsTheUppercaseSha256HexContract()
    {
        var verifier = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(Bearer)));
        Assert.That(verifier, Is.EqualTo(
            "AFC3C8DED19528CCEC362EF11FC576C75717FCE94E417A71D6BA00503D9C2C78"));
    }

    [Test]
    public void MalformedOrOversizedVerifierStoresFailClosedBeforeAnyProofScan()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ridesbridge-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var malformedPath = Path.Combine(directory, "malformed.json");
            File.WriteAllText(malformedPath, "[{\"verifier\":null,\"createdAt\":\"2026-01-01T00:00:00Z\",\"revoked\":false}]");
            var malformed = Assert.Throws<BridgeConfigurationException>(() => new FilePairedClientStore(malformedPath));
            Assert.That(malformed!.Message, Does.Not.Contain("null"));

            var oversizedPath = Path.Combine(directory, "oversized.json");
            var records = Enumerable.Range(0, FilePairedClientStore.MaximumRecords + 1)
                .Select(index => new PairedClientRecord(
                    Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                        System.Text.Encoding.UTF8.GetBytes($"token-{index}"))),
                    DateTimeOffset.UtcNow,
                    false))
                .ToArray();
            File.WriteAllText(oversizedPath, JsonSerializer.Serialize(records));
            Assert.Throws<BridgeConfigurationException>(() => new FilePairedClientStore(oversizedPath));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
