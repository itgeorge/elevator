using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using NUnit.Framework;
using Tokens;

namespace Tokens.Tests;

[TestFixture]
public sealed class MercuryFixtureOracleTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    [Test]
    public void Mercury_fixture_has_the_stable_v1_schema()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(FixturePath()));
        var root = document.RootElement;

        Assert.That(root.ValueKind, Is.EqualTo(JsonValueKind.Object));
        AssertProperties(root, "schemaVersion", "fixtureId", "sequence", "boundaries", "encodings",
            "rejectedEncodings", "malformedBlocks", "mirrorCases");
        Assert.That(root.GetProperty("schemaVersion").GetInt32(), Is.EqualTo(1));
        Assert.That(root.GetProperty("fixtureId").GetString(), Is.EqualTo("mercury-v1"));

        var sequence = root.GetProperty("sequence");
        AssertProperties(sequence, "name", "zeroBlock", "rotation", "minRides", "maxRides");

        foreach (var entry in root.GetProperty("encodings").EnumerateArray())
            AssertProperties(entry, "rides", "block");
        foreach (var entry in root.GetProperty("rejectedEncodings").EnumerateArray())
            AssertProperties(entry, "rides", "block", "reason");
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
    public void Mercury_fixture_is_complete_unique_and_matches_the_independent_csharp_oracle()
    {
        var fixture = Load();

        Assert.That(fixture.Sequence.Name, Is.EqualTo("mercury"));
        Assert.That(fixture.Sequence.ZeroBlock, Is.EqualTo("CCC749CC"));
        Assert.That(fixture.Sequence.Rotation, Is.EqualTo(4));
        Assert.That(fixture.Sequence.MinRides, Is.EqualTo(0));
        Assert.That(fixture.Sequence.MaxRides, Is.EqualTo(500));

        var entriesByRide = fixture.Encodings.ToDictionary(entry => entry.Rides);
        Assert.That(fixture.Encodings, Has.Length.EqualTo(501));
        Assert.That(entriesByRide.Keys, Is.EquivalentTo(Enumerable.Range(0, 501)));
        Assert.That(fixture.Encodings.Select(entry => entry.Rides).Distinct().Count(), Is.EqualTo(501));
        Assert.That(fixture.Boundaries, Is.EqualTo(new uint[] { 0, 1, 7, 8, 127, 128, 255, 256, 383, 384, 500 }));
        Assert.That(fixture.Boundaries.Distinct().Count(), Is.EqualTo(fixture.Boundaries.Length));

        var blocks = new HashSet<uint>();
        foreach (var entry in fixture.Encodings)
        {
            var block = ParseBlock(entry.Block);
            Assert.That(blocks.Add(block.Value), Is.True, $"duplicate valid block {entry.Rides}");

            // This oracle is deliberately independent from EncodingSequence.Encode.
            Assert.That(block.Value, Is.EqualTo(EncodeMercuryOracle(entry.Rides)), $"fixture/{entry.Rides}");
            Assert.That(block.Value, Is.EqualTo(EncodingSequences.Mercury.Encode(entry.Rides).Value),
                $"C# production oracle/{entry.Rides}");
            Assert.That(EncodingSequences.Mercury.TryDecode(block, out var decoded), Is.True, $"decode/{entry.Rides}");
            Assert.That(decoded, Is.EqualTo(entry.Rides), $"decode/{entry.Rides}");
        }

        foreach (var boundary in fixture.Boundaries)
            Assert.That(entriesByRide[boundary].Block, Is.EqualTo(fixture.Encodings[(int)boundary].Block),
                $"boundary {boundary} must reference the complete vector");

        Assert.That(fixture.RejectedEncodings, Has.Length.EqualTo(11));
        Assert.That(fixture.RejectedEncodings.Select(entry => entry.Rides), Is.EquivalentTo(Enumerable.Range(501, 11)));
        var diagnosticCounterRange = new EncodingSequence("mercury-diagnostic", new T55Block(0xCCC749CC), 4, 0, 511);
        foreach (var entry in fixture.RejectedEncodings)
        {
            var block = ParseBlock(entry.Block);
            Assert.That(blocks.Add(block.Value), Is.True, $"rejected block duplicates another fixture block {entry.Rides}");
            Assert.That(entry.Reason, Is.EqualTo("application-range"));
            Assert.That(block.Value, Is.EqualTo(EncodeMercuryOracle(entry.Rides)), $"rejected/{entry.Rides}");
            Assert.That(diagnosticCounterRange.TryDecode(block, out var diagnosticRides), Is.True,
                $"rejected/{entry.Rides} must be structurally valid");
            Assert.That(diagnosticRides, Is.EqualTo(entry.Rides));
            Assert.That(EncodingSequences.Mercury.TryDecode(block, out _), Is.False,
                $"rejected/{entry.Rides} must fail Mercury's application range");
            Assert.That(EncodingSequences.TryDecode(block, out _, out _), Is.False,
                $"rejected/{entry.Rides} must fail the registered application range");
        }

        Assert.That(fixture.MalformedBlocks.Select(entry => entry.Name).Distinct().Count(),
            Is.EqualTo(fixture.MalformedBlocks.Length));
        foreach (var entry in fixture.MalformedBlocks)
        {
            var block = ParseBlock(entry.Block);
            Assert.That(blocks.Add(block.Value), Is.True, $"duplicate malformed block {entry.Name}");
            Assert.That(entry.Reason, Is.EqualTo("structural"));
            Assert.That(EncodingSequences.TryDecode(block, out _, out _), Is.False,
                $"malformed/{entry.Name} unexpectedly decoded");
            Assert.That(diagnosticCounterRange.TryDecode(block, out _), Is.False,
                $"malformed/{entry.Name} is not structurally malformed");
        }
    }

    [Test]
    public void Mercury_fixture_has_all_named_boundary_vectors()
    {
        var fixture = Load();
        var byRide = fixture.Encodings.ToDictionary(entry => entry.Rides);

        foreach (var rides in fixture.Boundaries)
        {
            Assert.That(byRide.ContainsKey(rides), Is.True, $"missing boundary vector {rides}");
            Assert.That(byRide[rides].Block, Does.Match("^[0-9A-F]{8}$"), $"boundary {rides}");
        }
    }

    private static MercuryFixture Load()
    {
        var fixture = JsonSerializer.Deserialize<MercuryFixture>(File.ReadAllText(FixturePath()), JsonOptions);
        Assert.That(fixture, Is.Not.Null);
        Assert.That(fixture!.Sequence, Is.Not.Null);
        Assert.That(fixture.Encodings, Is.Not.Null);
        Assert.That(fixture.RejectedEncodings, Is.Not.Null);
        Assert.That(fixture.MalformedBlocks, Is.Not.Null);
        return fixture;
    }

    private static T55Block ParseBlock(string value)
    {
        Assert.That(value, Does.Match("^[0-9A-F]{8}$"));
        return T55Block.FromHex(value);
    }

    private static uint EncodeMercuryOracle(uint rides)
    {
        var r = (byte)rides;
        var h = (byte)(rides >> 8);
        var rotated = (byte)((r << 4) | (r >> 4));
        var payload = (byte)(rotated ^ (h << 4));
        var delta = (payload & 0x08) != 0 ? 0xF3000000u : 0u;
        delta |= ((uint)h << 16) | ((uint)r << 8) | payload;
        return 0xCCC749CCu ^ delta;
    }

    private static string FixturePath()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, "TestFixtures", "RideEncoding", "mercury-v1.json");
            if (File.Exists(path))
                return path;
        }

        throw new FileNotFoundException("Could not locate the checked-in Mercury ride fixture.");
    }

    private static void AssertProperties(JsonElement element, params string[] expected)
    {
        Assert.That(element.EnumerateObject().Select(property => property.Name), Is.EquivalentTo(expected));
    }

    private sealed class MercuryFixture
    {
        public int SchemaVersion { get; set; }
        public string FixtureId { get; set; } = "";
        public MercurySequence Sequence { get; set; } = new();
        public uint[] Boundaries { get; set; } = [];
        public MercuryEncoding[] Encodings { get; set; } = [];
        public MercuryRejectedEncoding[] RejectedEncodings { get; set; } = [];
        public MercuryMalformedBlock[] MalformedBlocks { get; set; } = [];
        public MercuryMirrorCase[] MirrorCases { get; set; } = [];
    }

    private sealed class MercurySequence
    {
        public string Name { get; set; } = "";
        public string ZeroBlock { get; set; } = "";
        public byte Rotation { get; set; }
        public uint MinRides { get; set; }
        public uint MaxRides { get; set; }
    }

    private sealed class MercuryEncoding
    {
        public uint Rides { get; set; }
        public string Block { get; set; } = "";
    }

    private sealed class MercuryRejectedEncoding
    {
        public uint Rides { get; set; }
        public string Block { get; set; } = "";
        public string Reason { get; set; } = "";
    }

    private sealed class MercuryMalformedBlock
    {
        public string Name { get; set; } = "";
        public string Block { get; set; } = "";
        public string Reason { get; set; } = "";
    }

    private sealed class MercuryMirrorCase
    {
        public string Name { get; set; } = "";
        public string Block5 { get; set; } = "";
        public string Block6 { get; set; } = "";
        public MercuryExpectedResult Expected { get; set; } = new();
    }

    private sealed class MercuryExpectedResult
    {
        public string Status { get; set; } = "";
        public uint? Rides { get; set; }
        public string? SourceBlock { get; set; }
        public int? SourceBlockNumber { get; set; }
        public bool BlocksMatched { get; set; }
        public string? WarningMessage { get; set; }
    }
}
