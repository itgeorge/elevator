using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using NUnit.Framework;
using TestFixtures.RideEncoding;
using Tokens;

namespace Tokens.Tests;

[TestFixture]
public sealed class RideEncodingFixtureOracleTests
{
    private static readonly string[] ExpectedSequenceNames =
    [
        "mercury", "venus", "earth", "pluto", "mars",
        "jupiter", "saturn", "uranus", "neptune", "charon", "nix",
    ];

    [Test]
    public void Ride_encoding_fixture_has_the_stable_v2_schema()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(RideEncodingFixtureSupport.FixturePath()));
        var root = document.RootElement;

        Assert.That(root.ValueKind, Is.EqualTo(JsonValueKind.Object));
        AssertProperties(root, "schemaVersion", "fixtureId", "boundaries", "sequences",
            "rejectedEncodings", "malformedBlocks", "mirrorCases");
        Assert.That(root.GetProperty("schemaVersion").GetInt32(), Is.EqualTo(2));
        Assert.That(root.GetProperty("fixtureId").GetString(), Is.EqualTo("ride-encoding-v2"));

        foreach (var sequence in root.GetProperty("sequences").EnumerateArray())
        {
            AssertProperties(sequence, "name", "zeroBlock", "rotation", "minRides", "maxRides", "encodings");
            foreach (var entry in sequence.GetProperty("encodings").EnumerateArray())
                AssertProperties(entry, "rides", "block");
        }

        foreach (var entry in root.GetProperty("rejectedEncodings").EnumerateArray())
            AssertProperties(entry, "sequence", "rides", "block", "reason");
        foreach (var entry in root.GetProperty("malformedBlocks").EnumerateArray())
            AssertProperties(entry, "name", "block", "reason");
        foreach (var entry in root.GetProperty("mirrorCases").EnumerateArray())
        {
            AssertProperties(entry, "name", "block5", "block6", "expected");
            AssertProperties(entry.GetProperty("expected"), "status", "rides", "sourceBlock",
                "sourceBlockNumber", "blocksMatched", "warningMessage");
        }
    }

    [Test]
    public void Ride_encoding_fixture_matches_every_registered_sequence_and_encoding()
    {
        var fixture = RideEncodingFixtureSupport.Load();

        Assert.That(fixture.Boundaries, Is.EqualTo(new uint[] { 0, 1, 7, 8, 127, 128, 255, 256, 383, 384, 500 }));
        Assert.That(fixture.Boundaries.Distinct().Count(), Is.EqualTo(fixture.Boundaries.Length));
        Assert.That(fixture.Sequences.Select(sequence => sequence.Name), Is.EquivalentTo(ExpectedSequenceNames));
        Assert.That(fixture.Sequences.Select(sequence => sequence.Name).Distinct().Count(),
            Is.EqualTo(fixture.Sequences.Length));

        var globalBlocks = new Dictionary<uint, (string Sequence, uint Rides)>();
        foreach (var section in fixture.Sequences)
        {
            var oracle = RideEncodingFixtureSupport.GetOracleSequence(section.Name);

            Assert.That(section.ZeroBlock, Is.EqualTo(oracle.ZeroBlock.ToHex()), section.Name);
            Assert.That(section.Rotation, Is.EqualTo(oracle.Rotation), section.Name);
            Assert.That(section.MinRides, Is.EqualTo(oracle.MinRides), section.Name);
            Assert.That(section.MaxRides, Is.EqualTo(oracle.MaxRides), section.Name);
            Assert.That(section.Encodings, Has.Length.EqualTo(501), section.Name);

            var entriesByRide = section.Encodings.ToDictionary(entry => entry.Rides);
            Assert.That(entriesByRide.Keys, Is.EquivalentTo(Enumerable.Range(0, 501)), section.Name);
            Assert.That(section.Encodings.Select(entry => entry.Rides).Distinct().Count(), Is.EqualTo(501), section.Name);

            var localBlocks = new HashSet<uint>();
            foreach (var entry in section.Encodings)
            {
                var block = RideEncodingFixtureSupport.ParseBlock(entry.Block);
                Assert.That(localBlocks.Add(block.Value), Is.True, $"{section.Name}/{entry.Rides} duplicate within sequence");
                Assert.That(block.Value, Is.EqualTo(oracle.Encode(entry.Rides).Value),
                    $"fixture/{section.Name}/{entry.Rides}");
                Assert.That(oracle.TryDecode(block, out var decoded), Is.True, $"decode/{section.Name}/{entry.Rides}");
                Assert.That(decoded, Is.EqualTo(entry.Rides), $"decode/{section.Name}/{entry.Rides}");

                if (!globalBlocks.TryAdd(block.Value, (section.Name, entry.Rides)))
                {
                    var other = globalBlocks[block.Value];
                    Assert.Fail(
                        $"cross-sequence collision: {section.Name}/{entry.Rides} and {other.Sequence}/{other.Rides} encode as {entry.Block}");
                }
            }

            foreach (var boundary in fixture.Boundaries)
                Assert.That(entriesByRide[boundary].Block, Is.EqualTo(section.Encodings[(int)boundary].Block),
                    $"{section.Name} boundary {boundary}");
        }
    }

    [Test]
    public void Ride_encoding_fixture_rejected_entries_are_structurally_valid_but_out_of_application_range()
    {
        var fixture = RideEncodingFixtureSupport.Load();
        var knownBlocks = CollectAllFixtureBlocks(fixture);

        Assert.That(fixture.RejectedEncodings, Has.Length.EqualTo(11 * ExpectedSequenceNames.Length));
        Assert.That(fixture.RejectedEncodings.Select(entry => entry.Sequence).Distinct(),
            Is.EquivalentTo(ExpectedSequenceNames));

        foreach (var sequenceName in ExpectedSequenceNames)
        {
            var rejected = fixture.RejectedEncodings.Where(entry => entry.Sequence == sequenceName).ToArray();
            Assert.That(rejected, Has.Length.EqualTo(11), sequenceName);
            Assert.That(rejected.Select(entry => entry.Rides), Is.EquivalentTo(Enumerable.Range(501, 11)), sequenceName);
        }

        foreach (var entry in fixture.RejectedEncodings)
        {
            var oracle = RideEncodingFixtureSupport.GetOracleSequence(entry.Sequence);
            var diagnostic = new EncodingSequence(
                $"{entry.Sequence}-diagnostic",
                oracle.ZeroBlock,
                oracle.Rotation,
                0,
                RideCounterCodec.MaxCounter);
            var block = RideEncodingFixtureSupport.ParseBlock(entry.Block);

            Assert.That(knownBlocks.Add(block.Value), Is.True, $"rejected block duplicates another fixture block {entry.Sequence}/{entry.Rides}");
            Assert.That(entry.Reason, Is.EqualTo("application-range"));
            Assert.That(block.Value, Is.EqualTo(diagnostic.Encode(entry.Rides).Value),
                $"rejected/{entry.Sequence}/{entry.Rides}");
            Assert.That(diagnostic.TryDecode(block, out var diagnosticRides), Is.True,
                $"rejected/{entry.Sequence}/{entry.Rides} must be structurally valid");
            Assert.That(diagnosticRides, Is.EqualTo(entry.Rides));
            Assert.That(oracle.TryDecode(block, out _), Is.False,
                $"rejected/{entry.Sequence}/{entry.Rides} must fail application range");
            Assert.That(EncodingSequences.TryDecode(block, out _, out _), Is.False,
                $"rejected/{entry.Sequence}/{entry.Rides} must fail the registered application range");
        }
    }

    [Test]
    public void Ride_encoding_fixture_malformed_blocks_are_structurally_invalid()
    {
        var fixture = RideEncodingFixtureSupport.Load();
        var knownBlocks = CollectAllFixtureBlocks(fixture);

        Assert.That(fixture.MalformedBlocks.Select(entry => entry.Name).Distinct().Count(),
            Is.EqualTo(fixture.MalformedBlocks.Length));

        foreach (var entry in fixture.MalformedBlocks)
        {
            var block = RideEncodingFixtureSupport.ParseBlock(entry.Block);
            Assert.That(knownBlocks.Add(block.Value), Is.True, $"duplicate malformed block {entry.Name}");
            Assert.That(entry.Reason, Is.EqualTo("structural"));
            Assert.That(EncodingSequences.TryDecode(block, out _, out _), Is.False,
                $"malformed/{entry.Name} unexpectedly decoded");
        }
    }

    [Test]
    public void Ride_encoding_fixture_matches_the_checked_in_generator_output()
    {
        var checkedIn = RideEncodingFixtureSupport.Load();
        var generated = RideEncodingFixtureGenerator.BuildFixture();

        Assert.That(generated.SchemaVersion, Is.EqualTo(checkedIn.SchemaVersion));
        Assert.That(generated.FixtureId, Is.EqualTo(checkedIn.FixtureId));
        Assert.That(generated.Boundaries, Is.EqualTo(checkedIn.Boundaries));
        Assert.That(generated.Sequences.Select(sequence => sequence.Name),
            Is.EquivalentTo(checkedIn.Sequences.Select(sequence => sequence.Name)));
        Assert.That(generated.RejectedEncodings.Length, Is.EqualTo(checkedIn.RejectedEncodings.Length));
        Assert.That(generated.MalformedBlocks.Select(block => block.Name),
            Is.EquivalentTo(checkedIn.MalformedBlocks.Select(block => block.Name)));
        Assert.That(generated.MirrorCases.Select(caseItem => caseItem.Name),
            Is.EquivalentTo(checkedIn.MirrorCases.Select(caseItem => caseItem.Name)));

        foreach (var generatedSequence in generated.Sequences)
        {
            var checkedInSequence = checkedIn.Sequences.Single(sequence => sequence.Name == generatedSequence.Name);
            Assert.That(generatedSequence.ZeroBlock, Is.EqualTo(checkedInSequence.ZeroBlock), generatedSequence.Name);
            Assert.That(generatedSequence.Rotation, Is.EqualTo(checkedInSequence.Rotation), generatedSequence.Name);
            Assert.That(generatedSequence.MinRides, Is.EqualTo(checkedInSequence.MinRides), generatedSequence.Name);
            Assert.That(generatedSequence.MaxRides, Is.EqualTo(checkedInSequence.MaxRides), generatedSequence.Name);
            Assert.That(generatedSequence.Encodings.Select(entry => (entry.Rides, entry.Block)),
                Is.EquivalentTo(checkedInSequence.Encodings.Select(entry => (entry.Rides, entry.Block))),
                generatedSequence.Name);
        }
    }

    private static HashSet<uint> CollectAllFixtureBlocks(RideEncodingFixtureDocument fixture)
    {
        var blocks = new HashSet<uint>();
        foreach (var section in fixture.Sequences)
        {
            foreach (var entry in section.Encodings)
                blocks.Add(RideEncodingFixtureSupport.ParseBlock(entry.Block).Value);
        }

        return blocks;
    }

    private static void AssertProperties(JsonElement element, params string[] expected)
    {
        Assert.That(element.EnumerateObject().Select(property => property.Name), Is.EquivalentTo(expected));
    }
}
