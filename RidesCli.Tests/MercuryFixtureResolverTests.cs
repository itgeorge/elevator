using System.Text.Json;
using NUnit.Framework;
using RidesCli;
using Tokens;

namespace RidesCli.Tests;

[TestFixture]
public sealed class MercuryFixtureResolverTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    [Test]
    public void Resolver_matches_every_complete_mercury_fixture_entry()
    {
        var fixture = Load();

        Assert.That(fixture.Encodings, Has.Length.EqualTo(501));
        foreach (var entry in fixture.Encodings)
        {
            var block = T55Block.FromHex(entry.Block);
            var result = RideBlockResolver.Resolve(block, block);

            Assert.That(result.Status, Is.EqualTo(RideReadStatus.Success), $"{entry.Rides}");
            Assert.That(result.Rides, Is.EqualTo(entry.Rides), $"{entry.Rides}");
            Assert.That(result.SourceBlock?.Value, Is.EqualTo(block.Value), $"{entry.Rides}");
            Assert.That(result.SourceBlockNumber, Is.EqualTo(5), $"{entry.Rides}");
            Assert.That(result.BlocksMatched, Is.True, $"{entry.Rides}");
            Assert.That(result.WarningMessage, Is.Null, $"{entry.Rides}");
        }
    }

    [Test]
    public void Resolver_rejects_all_structurally_valid_but_out_of_application_range_entries()
    {
        var fixture = Load();

        Assert.That(fixture.RejectedEncodings, Has.Length.EqualTo(11));
        foreach (var entry in fixture.RejectedEncodings)
        {
            var block = T55Block.FromHex(entry.Block);
            var result = RideBlockResolver.Resolve(block, block);

            Assert.That(result.Status, Is.EqualTo(RideReadStatus.UnknownEncodingSequence), $"{entry.Rides}");
            Assert.That(result.Rides, Is.Null, $"{entry.Rides}");
            Assert.That(result.SourceBlock?.Value, Is.EqualTo(block.Value), $"{entry.Rides}");
            Assert.That(result.SourceBlockNumber, Is.EqualTo(5), $"{entry.Rides}");
            Assert.That(result.BlocksMatched, Is.True, $"{entry.Rides}");
            Assert.That(result.WarningMessage, Is.Null, $"{entry.Rides}");
        }
    }

    [Test]
    public void Resolver_rejects_every_structurally_malformed_fixture_entry()
    {
        var fixture = Load();

        foreach (var entry in fixture.MalformedBlocks)
        {
            var block = T55Block.FromHex(entry.Block);
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
        var fixture = Load();
        var names = fixture.MirrorCases.Select(item => item.Name).ToArray();

        Assert.That(names, Is.Unique);
        Assert.That(names, Is.EquivalentTo(new[]
        {
            "matching-valid",
            "only-block-5-valid",
            "only-block-6-valid",
            "both-valid-block-6-wins",
            "neither-valid",
        }));

        foreach (var fixtureCase in fixture.MirrorCases)
        {
            var result = RideBlockResolver.Resolve(
                T55Block.FromHex(fixtureCase.Block5),
                T55Block.FromHex(fixtureCase.Block6));
            var expected = fixtureCase.Expected;

            Assert.That(StatusName(result.Status), Is.EqualTo(expected.Status), fixtureCase.Name);
            Assert.That(result.Rides, Is.EqualTo(expected.Rides), fixtureCase.Name);
            Assert.That(result.SourceBlock?.ToHex(), Is.EqualTo(expected.SourceBlock), fixtureCase.Name);
            Assert.That(result.SourceBlockNumber, Is.EqualTo(expected.SourceBlockNumber), fixtureCase.Name);
            Assert.That(result.BlocksMatched, Is.EqualTo(expected.BlocksMatched), fixtureCase.Name);
            Assert.That(result.WarningMessage, Is.EqualTo(expected.WarningMessage), fixtureCase.Name);
        }
    }

    private static MercuryFixture Load()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(FixturePath()));
        Assert.That(document.RootElement.GetProperty("schemaVersion").GetInt32(), Is.EqualTo(1));
        Assert.That(document.RootElement.GetProperty("fixtureId").GetString(), Is.EqualTo("mercury-v1"));

        var fixture = JsonSerializer.Deserialize<MercuryFixture>(document.RootElement.GetRawText(), JsonOptions);
        Assert.That(fixture, Is.Not.Null);
        Assert.That(fixture!.Sequence.Name, Is.EqualTo("mercury"));
        Assert.That(fixture.Sequence.ZeroBlock, Is.EqualTo("CCC749CC"));
        Assert.That(fixture.Sequence.Rotation, Is.EqualTo(4));
        Assert.That(fixture.Sequence.MinRides, Is.EqualTo(0));
        Assert.That(fixture.Sequence.MaxRides, Is.EqualTo(500));
        return fixture;
    }

    private static string StatusName(RideReadStatus status) => status switch
    {
        RideReadStatus.Success => "success",
        RideReadStatus.UnknownEncodingSequence => "unknownEncodingSequence",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };

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

    private sealed class MercuryFixture
    {
        public MercurySequence Sequence { get; set; } = new();
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
