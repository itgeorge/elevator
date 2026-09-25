using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Net.Http.Json;
using NUnit.Framework;
using RidesBridge;

namespace RidesBridge.Tests;

[TestFixture]
public sealed class PairingTests
{
    [Test]
    public void IssueCode_IsSixNumericDigitsAndExpires()
    {
        var clock = new TestClock(DateTimeOffset.UtcNow);
        var service = new PairingCodeService(TimeSpan.FromSeconds(10), clock);
        var issued = service.IssueCode();
        Assert.That(issued.Value, Does.Match("^[0-9]{6}$"));
        Assert.That(issued.ExpiresAt, Is.EqualTo(clock.GetUtcNow().AddSeconds(10)));
        clock.Advance(TimeSpan.FromSeconds(11));
        Assert.That(service.TryRedeem(issued.Value), Is.False);
    }

    [Test]
    public void WrongAndReusedCodesAreRejected()
    {
        var service = new PairingCodeService(TimeSpan.FromMinutes(1));
        var code = service.IssueCode().Value;
        var wrong = code == "000000" ? "000001" : "000000";
        Assert.That(service.TryRedeem(wrong), Is.False);
        Assert.That(service.TryRedeem(code), Is.True);
        Assert.That(service.TryRedeem(code), Is.False);
    }

    [Test]
    public void ActiveCodeWrongAttemptsAreBoundedAndExhaustTheCode()
    {
        var service = new PairingCodeService(TimeSpan.FromMinutes(1), maxWrongAttempts: 3);
        var issued = service.IssueCode();
        var wrong = issued.Value == "000000" ? "000001" : "000000";

        Assert.That(service.GetActiveCode(), Is.EqualTo(issued));
        Assert.That(service.TryRedeem(wrong), Is.False);
        Assert.That(service.GetActiveCode(), Is.EqualTo(issued));
        Assert.That(service.TryRedeem(wrong), Is.False);
        Assert.That(service.IsActive, Is.True);
        Assert.That(service.TryRedeem(wrong), Is.False);
        Assert.That(service.IsActive, Is.False);
        Assert.That(service.GetActiveCode(), Is.Null);
        Assert.That(service.TryRedeem(issued.Value), Is.False);
    }

    [Test]
    public void IssuingCodeReplacesTheActiveCodeAndExposesOnlyItsExpiryToOperatorApi()
    {
        var clock = new TestClock(DateTimeOffset.UtcNow);
        var service = new PairingCodeService(TimeSpan.FromSeconds(10), clock);
        var first = service.GetActiveCode();
        var second = service.IssueCode();

        Assert.That(first, Is.Not.Null);
        Assert.That(second.Value, Does.Match("^[0-9]{6}$"));
        Assert.That(second.ExpiresAt, Is.EqualTo(clock.GetUtcNow().AddSeconds(10)));
        Assert.That(service.GetActiveCode(), Is.EqualTo(second));
        Assert.That(service.TryRedeem(first!.Value), Is.False);
        Assert.That(service.TryRedeem(second.Value), Is.True);
    }

    [Test]
    public async Task ConcurrentRedeemersCanConsumeOnlyOnce()
    {
        var service = new PairingCodeService(TimeSpan.FromMinutes(1));
        var code = service.IssueCode().Value;
        var results = await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => Task.Run(() => service.TryRedeem(code))));
        Assert.That(results.Count(r => r), Is.EqualTo(1));
    }

    [Test]
    public async Task StorePersistsOnlyVerifierAndSupportsRestartAndRevoke()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ridesbridge-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "paired.json");
        var secret = Convert.ToBase64String(Guid.NewGuid().ToByteArray());
        try
        {
            var first = new FilePairedClientStore(path);
            await first.AddAsync(secret);
            Assert.That(first.IsValid(secret), Is.True);
            var persisted = await File.ReadAllTextAsync(path);
            Assert.That(persisted, Does.Not.Contain(secret));
            var verifier = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
            Assert.That(persisted, Does.Contain(verifier));

            var restarted = new FilePairedClientStore(path);
            Assert.That(restarted.IsValid(secret), Is.True);
            Assert.That(await restarted.RevokeAsync(secret), Is.True);
            Assert.That(new FilePairedClientStore(path).IsValid(secret), Is.False);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Test]
    public async Task RequestLogsContainNeitherPinNorBearerToken()
    {
        var logs = new CapturingLoggerProvider();
        await using var host = await BridgeTestHost.CreateAsync(loggerProvider: logs);
        var pin = await host.IssuePinAsync();
        var response = await host.Client.PostAsJsonAsync("/api/v1/pair", new PairRequest(pin));
        var pair = await response.Content.ReadFromJsonAsync<PairResponse>();
        Assert.That(pair, Is.Not.Null);
        var allLogs = string.Join("\n", logs.Messages);
        Assert.That(allLogs, Does.Contain("/api/v1/pair"));
        Assert.That(allLogs, Does.Not.Contain(pin));
        Assert.That(allLogs, Does.Not.Contain(pair!.AccessToken));
    }

    [Test]
    public async Task EndpointPairingIssuesBearerAndHardwareRequiresIt()
    {
        await using var host = await BridgeTestHost.CreateAsync();
        var unauthorized = await host.Client.GetAsync("/api/v1/hardware/page0/block5");
        Assert.That(unauthorized.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        host.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "invalid-token");
        var invalid = await host.Client.GetAsync("/api/v1/hardware/page0/block5");
        Assert.That(invalid.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));

        var token = await host.PairAsync();
        host.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await host.Client.GetAsync("/api/v1/hardware/page0/block5");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var body = await response.Content.ReadFromJsonAsync<BlockReadResponse>();
        Assert.That(body, Is.EqualTo(new BlockReadResponse(5, "A1B2C3D4")));
    }

    [Test]
    public async Task PairStatusRequiresBearerAndAuthorizedStatusDoesNotTouchPm3()
    {
        await using var host = await BridgeTestHost.CreateAsync();

        var unauthorized = await host.Client.GetAsync("/api/v1/pair/status");
        Assert.That(unauthorized.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(host.Device.ReadCalls, Is.EqualTo(0));

        var token = await host.PairAsync();
        host.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var authorized = await host.Client.GetAsync("/api/v1/pair/status");

        Assert.That(authorized.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var body = await authorized.Content.ReadAsStringAsync();
        Assert.That(body, Does.Not.Contain(token));
        Assert.That(System.Text.Json.JsonSerializer.Deserialize<PairStatusResponse>(
                body, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)),
            Is.EqualTo(new PairStatusResponse("v1", true)));
        Assert.That(host.Device.ReadCalls, Is.EqualTo(0));
    }

    [Test]
    public async Task WrongExpiredAndReusedEndpointPinsDoNotIssueTokens()
    {
        await using var host = await BridgeTestHost.CreateAsync();
        var pin = await host.IssuePinAsync();
        var wrongPin = pin == "000000" ? "000001" : "000000";
        var wrong = await host.Client.PostAsJsonAsync("/api/v1/pair", new PairRequest(wrongPin));
        Assert.That(wrong.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));

        var first = await host.Client.PostAsJsonAsync("/api/v1/pair", new PairRequest(pin));
        Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var reused = await host.Client.PostAsJsonAsync("/api/v1/pair", new PairRequest(pin));
        Assert.That(reused.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task PairedBearerSurvivesBridgeRestart()
    {
        var path = Path.Combine(Path.GetTempPath(), "ridesbridge-tests", Guid.NewGuid().ToString("N"), "paired.json");
        try
        {
            string token;
            await using (var first = await BridgeTestHost.CreateAsync(path: path))
                token = await first.PairAsync();

            await using var restarted = await BridgeTestHost.CreateAsync(path: path);
            restarted.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var response = await restarted.Client.GetAsync("/api/v1/hardware/page0/block5");
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }
        finally
        {
            var directory = Path.GetDirectoryName(path);
            if (directory is not null && Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Test]
    public async Task RevokedBearerReturnsUnauthorizedAndHealthDoesNotTouchHardware()
    {
        await using var host = await BridgeTestHost.CreateAsync();
        var health = await host.Client.GetAsync("/api/v1/health");
        Assert.That(health.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(host.Device.ReadCalls, Is.EqualTo(0));

        var token = await host.PairAsync();
        host.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var revoke = await host.Client.PostAsync("/api/v1/pair/revoke", null);
        Assert.That(revoke.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        var revoked = await host.Client.GetAsync("/api/v1/hardware/page0/block5");
        Assert.That(revoked.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }
}
