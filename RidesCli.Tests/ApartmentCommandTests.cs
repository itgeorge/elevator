using System.Text;
using NUnit.Framework;
using RidesCli;
using Tokens;

namespace RidesCli.Tests;

public class ApartmentCommandTests
{
    private const string TestSecret = "phase0-test-secret";
    private static readonly T55Block Block3 = T55Block.FromHex("CCC749CC");

    private static byte[] SecretBytes => Encoding.UTF8.GetBytes(TestSecret);

    private static FakeRidesPm3Api CreatePm3WithEncodedApt(byte apt)
    {
        var block4 = ApartmentBlockCodec.Encode(SecretBytes, Block3, building: 0, apt);
        return FakeRidesPm3Api.WithBlocks3And4(Block3, block4);
    }

    private static RidesCommandHandler CreateHandler(
        FakeRidesPm3Api pm3,
        StringBuilderRidesOutput output,
        ApartmentSecretStore store,
        params string?[] secretResponses) =>
        new(pm3, output, new RidesConfig(), new ScriptedRidesInput([], secretResponses), store);

    [Test]
    public void AptSecret_replaceExistingSecret_overwritesCachedValue()
    {
        var output = new StringBuilderRidesOutput();
        var store = new ApartmentSecretStore();
        var input = new ScriptedRidesInput([], ["first-secret", "replacement-secret"]);
        var handler = new RidesCommandHandler(new FakeRidesPm3Api(), output, new RidesConfig(), input, store);

        handler.Execute(["aptsecret"]);
        handler.Execute(["aptsecret"]);

        Assert.That(store.TryGetSecret(out var secret), Is.True);
        Assert.That(Encoding.UTF8.GetString(secret), Is.EqualTo("replacement-secret"));
        Assert.That(input.ReadSecretLineCallCount, Is.EqualTo(2));
        Assert.That(output.Lines, Has.All.Not.Contains("first-secret"));
        Assert.That(output.Lines, Has.All.Not.Contains("replacement-secret"));
    }

    [Test]
    public void Apt_read_sealedValue_printsBuildingAndApt()
    {
        var output = new StringBuilderRidesOutput();
        var store = new ApartmentSecretStore();
        store.SetSecretFromUtf8(TestSecret);
        var pm3 = CreatePm3WithEncodedApt(42);
        var handler = CreateHandler(pm3, output, store);

        handler.Execute(["apt"]);

        Assert.That(output.Lines, Has.Some.EqualTo("building: 0, apt: 42"));
        Assert.That(pm3.WriteAndVerifyPage0BlocksCallCount, Is.EqualTo(0));
    }

    [Test]
    public void Apt_read_factoryMirror_printsNotEncoded()
    {
        var output = new StringBuilderRidesOutput();
        var store = new ApartmentSecretStore();
        store.SetSecretFromUtf8(TestSecret);
        var pm3 = FakeRidesPm3Api.WithBlocks3And4(Block3, Block3);
        var handler = CreateHandler(pm3, output, store);

        handler.Execute(["apt"]);

        Assert.That(output.Lines, Has.Some.EqualTo("Apartment not encoded in block 4."));
    }

    [Test]
    public void Apt_read_junkBlock4_printsNotEncoded()
    {
        var output = new StringBuilderRidesOutput();
        var store = new ApartmentSecretStore();
        store.SetSecretFromUtf8(TestSecret);
        var pm3 = FakeRidesPm3Api.WithBlocks3And4(Block3, T55Block.FromHex("DEADBEEF"));
        var handler = CreateHandler(pm3, output, store);

        handler.Execute(["apt"]);

        Assert.That(output.Lines, Has.Some.EqualTo("Apartment not encoded in block 4."));
    }

    [Test]
    public void Apt_read_missingSecret_promptsThenDecodes()
    {
        var output = new StringBuilderRidesOutput();
        var store = new ApartmentSecretStore();
        var pm3 = CreatePm3WithEncodedApt(17);
        var handler = CreateHandler(pm3, output, store, TestSecret);

        handler.Execute(["apt"]);

        Assert.That(output.Lines, Has.Some.EqualTo("Enter apartment secret:"));
        Assert.That(output.Lines, Has.Some.EqualTo("building: 0, apt: 17"));
        Assert.That(store.HasSecret, Is.True);
    }

    [Test]
    public void Apt_read_failedSecretEntry_printsErrorAndDoesNotWrite()
    {
        var output = new StringBuilderRidesOutput();
        var store = new ApartmentSecretStore();
        var pm3 = CreatePm3WithEncodedApt(17);
        var handler = CreateHandler(pm3, output, store, (string?)null);

        handler.Execute(["apt"]);

        Assert.That(output.Lines, Has.Some.Contains("cancelled"));
        Assert.That(pm3.WrittenBlocks, Is.Empty);
    }

    [Test]
    public void Apt_write_encodesBlock4Only()
    {
        var output = new StringBuilderRidesOutput();
        var store = new ApartmentSecretStore();
        store.SetSecretFromUtf8(TestSecret);
        var pm3 = FakeRidesPm3Api.WithBlocks3And4(Block3, Block3);
        var originalBlock5 = pm3.GetBlockHex(5);
        var handler = CreateHandler(pm3, output, store);

        handler.Execute(["apt", "99"]);

        Assert.That(pm3.WrittenBlocks, Is.EqualTo(new uint[] { 4 }));
        Assert.That(pm3.GetBlockHex(5), Is.EqualTo(originalBlock5));
        Assert.That(
            ApartmentBlockCodec.TryDecode(SecretBytes, Block3, T55Block.FromHex(pm3.GetBlockHex(4)), out var payload),
            Is.True);
        Assert.That(payload.Apt, Is.EqualTo(99));
        Assert.That(payload.Building, Is.EqualTo(0));
    }

    [Test]
    public void Apt_write_usesCurrentBlock3InCodec()
    {
        var output = new StringBuilderRidesOutput();
        var store = new ApartmentSecretStore();
        store.SetSecretFromUtf8(TestSecret);
        var alternateBlock3 = T55Block.FromHex("48C74948");
        var pm3 = FakeRidesPm3Api.WithBlocks3And4(alternateBlock3, alternateBlock3);
        var handler = CreateHandler(pm3, output, store);

        handler.Execute(["apt", "5"]);

        var expected = ApartmentBlockCodec.Encode(SecretBytes, alternateBlock3, building: 0, apt: 5);
        Assert.That(pm3.GetBlockHex(4), Is.EqualTo(expected.ToHex()));
    }

    [Test]
    public void Apt_write_readbackRoundTrips()
    {
        var output = new StringBuilderRidesOutput();
        var store = new ApartmentSecretStore();
        store.SetSecretFromUtf8(TestSecret);
        var pm3 = FakeRidesPm3Api.WithBlocks3And4(Block3, Block3);
        var handler = CreateHandler(pm3, output, store);

        handler.Execute(["apt", "123"]);
        output.Clear();
        handler.Execute(["apt"]);

        Assert.That(output.Lines, Has.Some.EqualTo("building: 0, apt: 123"));
    }

    [TestCase("abc")]
    [TestCase("256")]
    [TestCase("-1")]
    public void Apt_write_invalidValue_printsUsageErrorAndDoesNotWrite(string value)
    {
        var output = new StringBuilderRidesOutput();
        var store = new ApartmentSecretStore();
        store.SetSecretFromUtf8(TestSecret);
        var pm3 = FakeRidesPm3Api.WithBlocks3And4(Block3, Block3);
        var handler = CreateHandler(pm3, output, store);

        handler.Execute(["apt", value]);

        Assert.That(output.Lines, Has.Some.EqualTo("Usage: apt [<0-255>]"));
        Assert.That(pm3.WrittenBlocks, Is.Empty);
    }

    [Test]
    public void Apt_write_failedSecretEntry_printsErrorAndDoesNotWrite()
    {
        var output = new StringBuilderRidesOutput();
        var store = new ApartmentSecretStore();
        var pm3 = FakeRidesPm3Api.WithBlocks3And4(Block3, Block3);
        var handler = CreateHandler(pm3, output, store, "");

        handler.Execute(["apt", "42"]);

        Assert.That(output.Lines, Has.Some.Contains("cannot be empty"));
        Assert.That(pm3.WrittenBlocks, Is.Empty);
    }

    [Test]
    public void Apt_write_success_printsEncodedResult()
    {
        var output = new StringBuilderRidesOutput();
        var store = new ApartmentSecretStore();
        store.SetSecretFromUtf8(TestSecret);
        var pm3 = FakeRidesPm3Api.WithBlocks3And4(Block3, Block3);
        var handler = CreateHandler(pm3, output, store);

        handler.Execute(["apt", "42"]);

        Assert.That(output.Lines, Has.Some.EqualTo("Apartment encoded: building 0, apt 42."));
    }

    [Test]
    public void Apt_tooManyArgs_printsUsage()
    {
        var output = new StringBuilderRidesOutput();
        var store = new ApartmentSecretStore();
        store.SetSecretFromUtf8(TestSecret);
        var handler = CreateHandler(new FakeRidesPm3Api(), output, store);

        handler.Execute(["apt", "1", "2"]);

        Assert.That(output.Lines, Has.Some.EqualTo("Usage: apt [<0-255>]"));
    }

    [Test]
    public void Read_withSecretSet_andSealedApt_alsoDisplaysApartment()
    {
        var output = new StringBuilderRidesOutput();
        var store = new ApartmentSecretStore();
        store.SetSecretFromUtf8(TestSecret);
        var block3 = TokenIdentityProfiles.Mercury.Block3;
        var block4 = ApartmentBlockCodec.Encode(SecretBytes, block3, building: 0, apt: 64);
        var pm3 = FakeRidesPm3Api.WithSequenceRides(EncodingSequences.Mercury, 120)
            .WithPage0Block(4, block4);
        var handler = CreateHandler(pm3, output, store);

        handler.Execute(["read"]);

        Assert.That(output.Lines, Has.Some.EqualTo("rides remaining: 120"));
        Assert.That(output.Lines, Has.Some.EqualTo("sequence: mercury"));
        Assert.That(output.Lines, Has.Some.EqualTo("building: 0, apt: 64"));
        Assert.That(output.Lines, Has.None.Contains("Enter apartment secret"));
    }

    [Test]
    public void Read_withSecretSet_andUnsealedApt_displaysNotEncoded()
    {
        var output = new StringBuilderRidesOutput();
        var store = new ApartmentSecretStore();
        store.SetSecretFromUtf8(TestSecret);
        var pm3 = FakeRidesPm3Api.WithSequenceRides(EncodingSequences.Mercury, 50);
        var handler = CreateHandler(pm3, output, store);

        handler.Execute(["read"]);

        Assert.That(output.Lines, Has.Some.EqualTo("rides remaining: 50"));
        Assert.That(output.Lines, Has.Some.EqualTo("Apartment not encoded in block 4."));
    }

    [Test]
    public void Read_withoutSecret_doesNotDisplayApartmentOrPrompt()
    {
        var output = new StringBuilderRidesOutput();
        var store = new ApartmentSecretStore();
        var block3 = TokenIdentityProfiles.Mercury.Block3;
        var block4 = ApartmentBlockCodec.Encode(SecretBytes, block3, building: 0, apt: 64);
        var pm3 = FakeRidesPm3Api.WithSequenceRides(EncodingSequences.Mercury, 120)
            .WithPage0Block(4, block4);
        var input = new ScriptedRidesInput([], []);
        var handler = new RidesCommandHandler(pm3, output, new RidesConfig(), input, store);

        handler.Execute(["read"]);

        Assert.That(output.Lines, Has.Some.EqualTo("rides remaining: 120"));
        Assert.That(output.Lines, Has.None.EqualTo("building: 0, apt: 64"));
        Assert.That(output.Lines, Has.None.Contains("Apartment not encoded"));
        Assert.That(output.Lines, Has.None.Contains("Enter apartment secret"));
        Assert.That(input.ReadSecretLineCallCount, Is.EqualTo(0));
    }
}
