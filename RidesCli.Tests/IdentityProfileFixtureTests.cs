using System.Text.Json;
using NUnit.Framework;
using RidesCli;
using TestFixtures.IdentityProfiles;
using Tokens;

namespace RidesCli.Tests;

[TestFixture]
public sealed class IdentityProfileFixtureTests
{
    [Test]
    public void Identity_profile_fixture_has_the_stable_v1_schema()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(IdentityProfileFixtureSupport.FixturePath()));
        var root = document.RootElement;

        Assert.That(root.ValueKind, Is.EqualTo(JsonValueKind.Object));
        AssertProperties(root, "schemaVersion", "fixtureId", "profiles");
        Assert.That(root.GetProperty("schemaVersion").GetInt32(), Is.EqualTo(1));
        Assert.That(root.GetProperty("fixtureId").GetString(), Is.EqualTo("identity-profiles-v1"));

        foreach (var profile in root.GetProperty("profiles").EnumerateArray())
        {
            var keys = profile.EnumerateObject().Select(property => property.Name).ToHashSet();
            Assert.That(keys, Is.SupersetOf(new[]
            {
                "friendlyName", "rideSequence", "tokenId",
                "block1", "block2", "block3", "block4",
                "canReset", "resetImageFileName",
            }));
            if (profile.GetProperty("canReset").GetBoolean())
                Assert.That(keys, Does.Contain("resetImage"));
            else
                Assert.That(keys, Does.Not.Contain("resetImage"));
        }
    }

    [Test]
    public void Identity_profile_fixture_matches_token_identity_profiles_and_reset_images()
    {
        var fixture = IdentityProfileFixtureSupport.Load();

        Assert.That(fixture.Profiles.Select(profile => profile.FriendlyName),
            Is.EquivalentTo(TokenIdentityProfiles.All.Select(profile => profile.FriendlyName)));

        foreach (var entry in fixture.Profiles)
        {
            Assert.That(
                TokenIdentityProfiles.TryGetByFriendlyName(entry.FriendlyName, out var oracle),
                Is.True,
                entry.FriendlyName);
            Assert.That(oracle!.RideSequence.FriendlyName, Is.EqualTo(entry.RideSequence), entry.FriendlyName);
            Assert.That(oracle.TokenId, Is.EqualTo(entry.TokenId), entry.FriendlyName);
            Assert.That(oracle.Block1.ToHex(), Is.EqualTo(entry.Block1), entry.FriendlyName);
            Assert.That(oracle.Block2.ToHex(), Is.EqualTo(entry.Block2), entry.FriendlyName);
            Assert.That(oracle.Block3.ToHex(), Is.EqualTo(entry.Block3), entry.FriendlyName);
            Assert.That(oracle.Block4.ToHex(), Is.EqualTo(entry.Block4), entry.FriendlyName);
            Assert.That(oracle.CanReset, Is.EqualTo(entry.CanReset), entry.FriendlyName);
            Assert.That(oracle.ResetImageFileName, Is.EqualTo(entry.ResetImageFileName), entry.FriendlyName);

            if (!entry.CanReset)
            {
                Assert.That(entry.ResetImage, Is.Null, entry.FriendlyName);
                continue;
            }

            var blocks = ResetPage0BlocksLoader.Load(oracle);
            Assert.That(entry.ResetImage, Has.Length.EqualTo(8), entry.FriendlyName);
            Assert.That(entry.ResetImage, Is.EqualTo(blocks.Select(block => block.ToHex()).ToArray()), entry.FriendlyName);
            Assert.That(blocks[1], Is.EqualTo(oracle.Block1), entry.FriendlyName);
            Assert.That(blocks[2], Is.EqualTo(oracle.Block2), entry.FriendlyName);
            Assert.That(blocks[3], Is.EqualTo(oracle.Block3), entry.FriendlyName);
            Assert.That(blocks[4], Is.EqualTo(oracle.Block4), entry.FriendlyName);
        }
    }

    private static void AssertProperties(JsonElement element, params string[] names)
    {
        var actual = element.EnumerateObject().Select(property => property.Name).ToArray();
        Assert.That(actual, Is.EquivalentTo(names));
    }
}
