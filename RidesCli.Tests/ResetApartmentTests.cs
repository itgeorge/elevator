using System.Text;
using NUnit.Framework;
using RidesCli;
using Tokens;

namespace RidesCli.Tests;

public class ResetApartmentTests
{
    private const string TestSecret = "phase0-test-secret";
    private static readonly byte[] SecretBytes = Encoding.UTF8.GetBytes(TestSecret);

    private static FakeRidesPm3Api CreateMercuryWithSealedApt(uint rides, byte apt)
    {
        var pm3 = FakeRidesPm3Api.WithSequenceRides(EncodingSequences.Mercury, rides);
        var block3 = TokenIdentityProfiles.Mercury.Block3;
        var block4 = ApartmentBlockCodec.Encode(SecretBytes, block3, building: 0, apt);
        pm3.WithPage0Block(4, block4);
        return pm3;
    }

    private static RidesCommandHandler CreateHandler(
        FakeRidesPm3Api pm3,
        StringBuilderRidesOutput output,
        ApartmentSecretStore? store = null,
        params string?[] promptResponses) =>
        new(pm3, output, new RidesConfig(), new ScriptedRidesInput(promptResponses), store ?? new ApartmentSecretStore());

    [Test]
    public void Reset_sealedAptWithoutResetApt_preservesBlock4AndResetsRides()
    {
        var output = new StringBuilderRidesOutput();
        var store = new ApartmentSecretStore();
        store.SetSecretFromUtf8(TestSecret);
        var pm3 = CreateMercuryWithSealedApt(73, 42);
        var sealedBlock4Hex = pm3.GetBlockHex(4);
        var handler = CreateHandler(pm3, output, store, "y");

        handler.Execute(["reset", "--sequence", "mercury"]);

        Assert.That(output.Lines, Has.Some.Contains("Preserving apartment in block 4"));
        Assert.That(output.Lines, Has.Some.Contains("resetting ride blocks only"));
        Assert.That(output.Lines, Has.Some.EqualTo("Success."));
        Assert.That(pm3.GetBlockHex(4), Is.EqualTo(sealedBlock4Hex));
        Assert.That(pm3.WrittenBlocks, Is.EqualTo(new uint[] { 5, 6 }));
        Assert.That(pm3.GetBlockHex(5), Is.EqualTo(EncodingSequences.Mercury.Encode(0).ToHex()));
        Assert.That(pm3.GetBlockHex(6), Is.EqualTo(EncodingSequences.Mercury.Encode(0).ToHex()));
    }

    [Test]
    public void Reset_sealedAptWithResetApt_writesProfileBlock4()
    {
        var output = new StringBuilderRidesOutput();
        var store = new ApartmentSecretStore();
        store.SetSecretFromUtf8(TestSecret);
        var pm3 = CreateMercuryWithSealedApt(73, 42);
        var handler = CreateHandler(pm3, output, store, "y");

        handler.Execute(["reset", "--sequence", "mercury", "--resetapt"]);

        Assert.That(output.Lines, Has.None.Contains("Preserving apartment in block 4"));
        Assert.That(output.Lines, Has.Some.EqualTo("Success."));
        Assert.That(pm3.GetBlockHex(4), Is.EqualTo(TokenIdentityProfiles.Mercury.Block4.ToHex()));
        Assert.That(pm3.WrittenBlocks, Does.Contain(4u));
    }

    [Test]
    public void Reset_profileMirrorBlock4_keepsExistingResetBehavior()
    {
        var output = new StringBuilderRidesOutput();
        var pm3 = FakeRidesPm3Api.WithSequenceRides(EncodingSequences.Mercury, 73);
        var handler = CreateHandler(pm3, output, promptResponses: "y");

        handler.Execute(["reset", "--sequence", "venus"]);

        Assert.That(output.Lines, Has.Some.EqualTo("Success."));
        Assert.That(pm3.WrittenBlocks, Is.EqualTo(new uint[] { 1, 2, 3, 4, 5, 6 }));
        Assert.That(pm3.GetBlockHex(4), Is.EqualTo("D6D1C733"));
    }

    [Test]
    public void Reset_sameIdentityWithSealedBlock4_onlyResetsRideBlocks()
    {
        var output = new StringBuilderRidesOutput();
        var store = new ApartmentSecretStore();
        store.SetSecretFromUtf8(TestSecret);
        var pm3 = CreateMercuryWithSealedApt(73, 99);
        var sealedBlock4Hex = pm3.GetBlockHex(4);
        Assert.That(sealedBlock4Hex, Is.Not.EqualTo(TokenIdentityProfiles.Mercury.Block4.ToHex()));
        var handler = CreateHandler(pm3, output, store, "y");

        handler.Execute(["reset", "--sequence", "mercury"]);

        Assert.That(output.Lines, Has.Some.Contains("resetting ride blocks only"));
        Assert.That(output.Lines, Has.Some.Contains("Preserving apartment in block 4"));
        Assert.That(pm3.WrittenBlocks, Is.EqualTo(new uint[] { 5, 6 }));
        Assert.That(pm3.GetBlockHex(4), Is.EqualTo(sealedBlock4Hex));
    }

    [Test]
    public void Reset_crossProfileWithSealedApt_resealsApartmentOntoNewBlock3()
    {
        var output = new StringBuilderRidesOutput();
        var store = new ApartmentSecretStore();
        store.SetSecretFromUtf8(TestSecret);
        var pm3 = CreateMercuryWithSealedApt(500, 64);
        var oldSealedBlock4Hex = pm3.GetBlockHex(4);
        var handler = CreateHandler(pm3, output, store);

        handler.Execute(["reset", "-f", "--sequence", "venus"]);

        Assert.That(output.Lines, Has.Some.Contains("Preserving apartment in block 4"));
        Assert.That(output.Lines, Has.Some.EqualTo("Success."));
        Assert.That(pm3.GetBlockHex(1), Is.EqualTo(TokenIdentityProfiles.Venus.Block1.ToHex()));
        Assert.That(pm3.GetBlockHex(2), Is.EqualTo(TokenIdentityProfiles.Venus.Block2.ToHex()));
        Assert.That(pm3.GetBlockHex(3), Is.EqualTo(TokenIdentityProfiles.Venus.Block3.ToHex()));
        Assert.That(pm3.WrittenBlocks, Does.Contain(4u));
        Assert.That(pm3.GetBlockHex(4), Is.Not.EqualTo(oldSealedBlock4Hex));

        var expectedBlock4 = ApartmentBlockCodec.Encode(
            SecretBytes,
            TokenIdentityProfiles.Venus.Block3,
            building: 0,
            apt: 64);
        Assert.That(pm3.GetBlockHex(4), Is.EqualTo(expectedBlock4.ToHex()));

        handler.Execute(["apt"]);
        Assert.That(output.Lines, Has.Some.EqualTo("building: 0, apt: 64"));
        Assert.That(output.Lines, Has.None.EqualTo("Apartment not encoded in block 4."));
    }

    [Test]
    public void Reset_secretMissingWithDivergentBlock4_preservesBlock4AndWarns()
    {
        var output = new StringBuilderRidesOutput();
        var pm3 = CreateMercuryWithSealedApt(73, 17);
        var sealedBlock4Hex = pm3.GetBlockHex(4);
        var handler = CreateHandler(pm3, output, promptResponses: "y");

        handler.Execute(["reset", "--sequence", "venus"]);

        Assert.That(output.Lines, Has.Some.Contains("apartment secret not available"));
        Assert.That(output.Lines, Has.Some.Contains("preserving block 4"));
        Assert.That(output.Lines, Has.Some.EqualTo("Success."));
        Assert.That(pm3.GetBlockHex(4), Is.EqualTo(sealedBlock4Hex));
        Assert.That(pm3.WrittenBlocks, Is.EqualTo(new uint[] { 1, 2, 3, 5, 6 }));
    }

    [Test]
    public void Reset_forceWithoutResetApt_preservesSealedApt()
    {
        var output = new StringBuilderRidesOutput();
        var store = new ApartmentSecretStore();
        store.SetSecretFromUtf8(TestSecret);
        var pm3 = CreateMercuryWithSealedApt(73, 55);
        var sealedBlock4Hex = pm3.GetBlockHex(4);
        var handler = CreateHandler(pm3, output, store);

        handler.Execute(["reset", "-f", "--sequence", "mercury"]);

        Assert.That(output.Lines, Has.Some.Contains("Preserving apartment in block 4"));
        Assert.That(output.Lines, Has.Some.EqualTo("Success."));
        Assert.That(pm3.GetBlockHex(4), Is.EqualTo(sealedBlock4Hex));
        Assert.That(pm3.WrittenBlocks, Does.Not.Contain(4u));
        Assert.That(pm3.ReadPage0BlockCallCount, Is.GreaterThanOrEqualTo(6));
    }

    [Test]
    public void Reset_sameIdentityWithJunkBlock4AndSecret_restoresProfileBlock4()
    {
        var output = new StringBuilderRidesOutput();
        var store = new ApartmentSecretStore();
        store.SetSecretFromUtf8(TestSecret);
        var pm3 = FakeRidesPm3Api.WithSequenceRides(EncodingSequences.Mercury, 73)
            .WithPage0Block(4, T55Block.FromHex("DEADBEEF"));
        var handler = CreateHandler(pm3, output, store, "y");

        handler.Execute(["reset", "--sequence", "mercury"]);

        Assert.That(output.Lines, Has.None.Contains("Preserving apartment in block 4"));
        Assert.That(output.Lines, Has.Some.EqualTo("Success."));
        Assert.That(pm3.GetBlockHex(4), Is.EqualTo(TokenIdentityProfiles.Mercury.Block4.ToHex()));
        Assert.That(pm3.WrittenBlocks, Is.EqualTo(new uint[] { 4, 5, 6 }));
    }

    [Test]
    public void Reset_resetAptFlag_parsedFromArgs()
    {
        var output = new StringBuilderRidesOutput();
        var store = new ApartmentSecretStore();
        store.SetSecretFromUtf8(TestSecret);
        var pm3 = CreateMercuryWithSealedApt(73, 12);
        var handler = CreateHandler(pm3, output, store, "y");

        handler.Execute(["reset", "--resetapt", "--sequence", "mercury"]);

        Assert.That(pm3.GetBlockHex(4), Is.EqualTo(TokenIdentityProfiles.Mercury.Block4.ToHex()));
    }
}
