using NUnit.Framework;
using RidesCli;
using TestFixtures.RideEncoding;
using Tokens;

namespace RidesCli.Tests;

[TestFixture]
public sealed class RideEncodingFixtureResolverTests
{
    [Test]
    public void Resolver_matches_every_complete_fixture_entry_for_all_sequences()
    {
        var fixture = RideEncodingFixtureSupport.Load();

        Assert.That(fixture.Sequences, Has.Length.EqualTo(11));
        foreach (var section in fixture.Sequences)
        {
            Assert.That(section.Encodings, Has.Length.EqualTo(501), section.Name);
            foreach (var entry in section.Encodings)
            {
                var block = RideEncodingFixtureSupport.ParseBlock(entry.Block);
                var result = RideBlockResolver.Resolve(block, block);

                Assert.That(result.Status, Is.EqualTo(RideReadStatus.Success), $"{section.Name}/{entry.Rides}");
                Assert.That(result.Rides, Is.EqualTo(entry.Rides), $"{section.Name}/{entry.Rides}");
                Assert.That(result.SourceBlock?.Value, Is.EqualTo(block.Value), $"{section.Name}/{entry.Rides}");
                Assert.That(result.SourceBlockNumber, Is.EqualTo(5), $"{section.Name}/{entry.Rides}");
                Assert.That(result.BlocksMatched, Is.True, $"{section.Name}/{entry.Rides}");
                Assert.That(result.WarningMessage, Is.Null, $"{section.Name}/{entry.Rides}");
            }
        }
    }

    [Test]
    public void Resolver_rejects_all_structurally_valid_but_out_of_application_range_entries()
    {
        var fixture = RideEncodingFixtureSupport.Load();

        Assert.That(fixture.RejectedEncodings, Has.Length.EqualTo(121));
        foreach (var entry in fixture.RejectedEncodings)
        {
            var block = RideEncodingFixtureSupport.ParseBlock(entry.Block);
            var result = RideBlockResolver.Resolve(block, block);

            Assert.That(result.Status, Is.EqualTo(RideReadStatus.UnknownEncodingSequence),
                $"{entry.Sequence}/{entry.Rides}");
            Assert.That(result.Rides, Is.Null, $"{entry.Sequence}/{entry.Rides}");
            Assert.That(result.SourceBlock?.Value, Is.EqualTo(block.Value), $"{entry.Sequence}/{entry.Rides}");
            Assert.That(result.SourceBlockNumber, Is.EqualTo(5), $"{entry.Sequence}/{entry.Rides}");
            Assert.That(result.BlocksMatched, Is.True, $"{entry.Sequence}/{entry.Rides}");
            Assert.That(result.WarningMessage, Is.Null, $"{entry.Sequence}/{entry.Rides}");
        }
    }

    [Test]
    public void Resolver_rejects_every_structurally_malformed_fixture_entry()
    {
        var fixture = RideEncodingFixtureSupport.Load();

        foreach (var entry in fixture.MalformedBlocks)
        {
            var block = RideEncodingFixtureSupport.ParseBlock(entry.Block);
            var result = RideBlockResolver.Resolve(block, block);

            Assert.That(result.Status, Is.EqualTo(RideReadStatus.UnknownEncodingSequence), entry.Name);
            Assert.That(result.Rides, Is.Null, entry.Name);
            Assert.That(result.SourceBlock?.Value, Is.EqualTo(block.Value), entry.Name);
            Assert.That(result.SourceBlockNumber, Is.EqualTo(5), entry.Name);
            Assert.That(result.BlocksMatched, Is.True, entry.Name);
            Assert.That(result.WarningMessage, Is.Null, entry.Name);
        }
    }

    [Test]
    public void Resolver_matches_every_fixture_mirror_case_including_source_and_warning_metadata()
    {
        var fixture = RideEncodingFixtureSupport.Load();
        var names = fixture.MirrorCases.Select(item => item.Name).ToArray();

        Assert.That(names, Is.Unique);
        Assert.That(names, Is.EquivalentTo(new[]
        {
            "mercury-matching-valid",
            "mercury-only-block-5-valid",
            "mercury-only-block-6-valid",
            "mercury-both-valid-block-6-wins",
            "mercury-neither-valid",
            "venus-both-valid-block-6-wins",
            "jupiter-matching-valid",
            "neptune-matching-valid",
            "neither-valid-unrelated",
        }));

        foreach (var fixtureCase in fixture.MirrorCases)
        {
            var result = RideBlockResolver.Resolve(
                RideEncodingFixtureSupport.ParseBlock(fixtureCase.Block5),
                RideEncodingFixtureSupport.ParseBlock(fixtureCase.Block6));
            var expected = fixtureCase.Expected;

            Assert.That(StatusName(result.Status), Is.EqualTo(expected.Status), fixtureCase.Name);
            Assert.That(result.Rides, Is.EqualTo(expected.Rides), fixtureCase.Name);
            Assert.That(result.SourceBlock?.ToHex(), Is.EqualTo(expected.SourceBlock), fixtureCase.Name);
            Assert.That(result.SourceBlockNumber, Is.EqualTo(expected.SourceBlockNumber), fixtureCase.Name);
            Assert.That(result.BlocksMatched, Is.EqualTo(expected.BlocksMatched), fixtureCase.Name);
            Assert.That(result.WarningMessage, Is.EqualTo(expected.WarningMessage), fixtureCase.Name);
        }
    }

    private static string StatusName(RideReadStatus status) => status switch
    {
        RideReadStatus.Success => "success",
        RideReadStatus.UnknownEncodingSequence => "unknownEncodingSequence",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };
}
