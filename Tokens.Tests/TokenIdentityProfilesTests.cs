using NUnit.Framework;
using Tokens;

namespace Tokens.Tests;

[TestFixture]
[TestOf(typeof(TokenIdentityProfiles))]
public class TokenIdentityProfilesTests
{
    [Test]
    public void Canonical_profiles_map_to_expected_ride_sequences_and_token_ids()
    {
        Assert.That(TokenIdentityProfiles.Mercury.RideSequence, Is.EqualTo(EncodingSequences.Mercury));
        Assert.That(TokenIdentityProfiles.Mercury.TokenId, Is.EqualTo("9BFE0062-5BA4A3DE-D5D1D713-D5D1D713"));

        Assert.That(TokenIdentityProfiles.Venus.RideSequence, Is.EqualTo(EncodingSequences.Venus));
        Assert.That(TokenIdentityProfiles.Venus.TokenId, Is.EqualTo("43FE0062-5BA494A3-D6D1C733-D6D1C733"));

        Assert.That(TokenIdentityProfiles.Earth.RideSequence, Is.EqualTo(EncodingSequences.Earth));
        Assert.That(TokenIdentityProfiles.Earth.TokenId, Is.EqualTo("D3FE005D-522BC69D-650432F5-650432F5"));

        Assert.That(TokenIdentityProfiles.Pluto.RideSequence, Is.EqualTo(EncodingSequences.Pluto));
        Assert.That(TokenIdentityProfiles.Pluto.TokenId, Is.EqualTo("83FE002A-F100C064-A3045930-A3045930"));

        Assert.That(TokenIdentityProfiles.Mars.RideSequence, Is.EqualTo(EncodingSequences.Mars));
        Assert.That(TokenIdentityProfiles.Mars.TokenId, Is.EqualTo("C3FE0031-20C60722-B6D14924-B6D14924"));

        Assert.That(TokenIdentityProfiles.Jupiter.RideSequence, Is.EqualTo(EncodingSequences.Jupiter));
        Assert.That(TokenIdentityProfiles.Jupiter.TokenId, Is.EqualTo("EBFE002A-F100CC5B-A5045936-A5045936"));
        Assert.That(TokenIdentityProfiles.Jupiter.CanReset, Is.True);
    }

    [Test]
    public void Canonical_profiles_have_reset_image_file_names()
    {
        Assert.That(TokenIdentityProfiles.Mercury.ResetImageFileName, Is.EqualTo("default-500-rides.bin"));
        Assert.That(TokenIdentityProfiles.Venus.ResetImageFileName, Is.EqualTo("venus-0-rides.bin"));
        Assert.That(TokenIdentityProfiles.Earth.ResetImageFileName, Is.EqualTo("earth-0-rides.bin"));
        Assert.That(TokenIdentityProfiles.Pluto.ResetImageFileName, Is.EqualTo("pluto-0-rides.bin"));
        Assert.That(TokenIdentityProfiles.Mars.ResetImageFileName, Is.EqualTo("mars-0-rides.bin"));
        Assert.That(TokenIdentityProfiles.Jupiter.ResetImageFileName, Is.EqualTo("jupiter-0-rides.bin"));
        Assert.That(TokenIdentityProfiles.Mercury.CanReset, Is.True);
        Assert.That(TokenIdentityProfiles.Venus.CanReset, Is.True);
        Assert.That(TokenIdentityProfiles.Earth.CanReset, Is.True);
        Assert.That(TokenIdentityProfiles.Pluto.CanReset, Is.True);
        Assert.That(TokenIdentityProfiles.Mars.CanReset, Is.True);
        Assert.That(TokenIdentityProfiles.Jupiter.CanReset, Is.True);
    }

    [Test]
    public void Variant_profiles_map_to_expected_ride_sequences_and_token_ids()
    {
        Assert.That(TokenIdentityProfiles.Venus21Ff.RideSequence, Is.EqualTo(EncodingSequences.Venus));
        Assert.That(TokenIdentityProfiles.Venus21Ff.TokenId, Is.EqualTo("21FF0031-5BA494A3-D6D1C733-D6D1C733"));

        Assert.That(TokenIdentityProfiles.EarthA457.RideSequence, Is.EqualTo(EncodingSequences.Earth));
        Assert.That(TokenIdentityProfiles.EarthA457.TokenId, Is.EqualTo("D3FE005D-A4578D3A-650432F5-650432F5"));
    }

    [Test]
    public void Variant_profiles_have_no_reset_image()
    {
        Assert.That(TokenIdentityProfiles.Venus21Ff.ResetImageFileName, Is.Null);
        Assert.That(TokenIdentityProfiles.EarthA457.ResetImageFileName, Is.Null);
        Assert.That(TokenIdentityProfiles.Venus21Ff.CanReset, Is.False);
        Assert.That(TokenIdentityProfiles.EarthA457.CanReset, Is.False);
    }

    [Test]
    public void TryGetByFriendlyName_finds_profiles_case_insensitively()
    {
        Assert.That(TokenIdentityProfiles.TryGetByFriendlyName("venus21ff", out var venus21ff), Is.True);
        Assert.That(venus21ff, Is.EqualTo(TokenIdentityProfiles.Venus21Ff));

        Assert.That(TokenIdentityProfiles.TryGetByFriendlyName("EARTH-A457", out var earthA457), Is.True);
        Assert.That(earthA457, Is.EqualTo(TokenIdentityProfiles.EarthA457));

        Assert.That(TokenIdentityProfiles.TryGetByFriendlyName("pluto", out var pluto), Is.True);
        Assert.That(pluto, Is.EqualTo(TokenIdentityProfiles.Pluto));

        Assert.That(TokenIdentityProfiles.TryGetByFriendlyName("JUPITER", out var jupiter), Is.True);
        Assert.That(jupiter, Is.EqualTo(TokenIdentityProfiles.Jupiter));

        Assert.That(TokenIdentityProfiles.TryGetByFriendlyName("neptune", out _), Is.False);
    }

    [Test]
    public void TryGetByTokenId_finds_canonical_and_variant_profiles()
    {
        Assert.That(
            TokenIdentityProfiles.TryGetByTokenId("21FF0031-5BA494A3-D6D1C733-D6D1C733", out var venus21ff),
            Is.True);
        Assert.That(venus21ff, Is.EqualTo(TokenIdentityProfiles.Venus21Ff));

        Assert.That(
            TokenIdentityProfiles.TryGetByTokenId("D3FE005D-A4578D3A-650432F5-650432F5", out var earthA457),
            Is.True);
        Assert.That(earthA457, Is.EqualTo(TokenIdentityProfiles.EarthA457));
    }

    [Test]
    public void Resettable_contains_only_profiles_with_reset_images()
    {
        Assert.That(TokenIdentityProfiles.Resettable, Has.Count.EqualTo(6));
        Assert.That(TokenIdentityProfiles.Resettable, Does.Contain(TokenIdentityProfiles.Mercury));
        Assert.That(TokenIdentityProfiles.Resettable, Does.Contain(TokenIdentityProfiles.Venus));
        Assert.That(TokenIdentityProfiles.Resettable, Does.Contain(TokenIdentityProfiles.Earth));
        Assert.That(TokenIdentityProfiles.Resettable, Does.Contain(TokenIdentityProfiles.Pluto));
        Assert.That(TokenIdentityProfiles.Resettable, Does.Contain(TokenIdentityProfiles.Mars));
        Assert.That(TokenIdentityProfiles.Resettable, Does.Contain(TokenIdentityProfiles.Jupiter));
        Assert.That(TokenIdentityProfiles.Resettable, Has.None.EqualTo(TokenIdentityProfiles.Venus21Ff));
        Assert.That(TokenIdentityProfiles.Resettable, Has.None.EqualTo(TokenIdentityProfiles.EarthA457));
    }
}
