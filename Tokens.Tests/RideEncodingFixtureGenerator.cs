using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using NUnit.Framework;
using TestFixtures.RideEncoding;
using Tokens;

namespace Tokens.Tests;

/// <summary>One-off generator for the checked-in ride-encoding-v2.json fixture.</summary>
[TestFixture]
public sealed class RideEncodingFixtureGenerator
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    [Test]
    [Explicit("Run manually to regenerate TestFixtures/RideEncoding/ride-encoding-v2.json")]
    public void Generate_ride_encoding_v2_fixture()
    {
        var fixture = BuildFixture();
        var outputPath = Path.Combine(RepositoryRoot(), "TestFixtures", "RideEncoding", "ride-encoding-v2.json");
        File.WriteAllText(outputPath, JsonSerializer.Serialize(fixture, WriteOptions) + Environment.NewLine);
        TestContext.WriteLine($"Wrote {outputPath}");
    }

    internal static RideEncodingFixtureDocument BuildFixture()
    {
        var boundaries = new uint[] { 0, 1, 7, 8, 127, 128, 255, 256, 383, 384, 500 };
        var sequences = EncodingSequences.All
            .Select(sequence => new RideEncodingSequenceSection
            {
                Name = sequence.FriendlyName,
                ZeroBlock = sequence.ZeroBlock.ToHex(),
                Rotation = sequence.Rotation,
                MinRides = sequence.MinRides,
                MaxRides = sequence.MaxRides,
                Encodings = Enumerable.Range((int)sequence.MinRides, (int)(sequence.MaxRides - sequence.MinRides + 1))
                    .Select(rides => new RideEncodingEntry
                    {
                        Rides = (uint)rides,
                        Block = sequence.Encode((uint)rides).ToHex(),
                    })
                    .ToArray(),
            })
            .ToArray();

        var rejectedEncodings = EncodingSequences.All
            .SelectMany(sequence =>
            {
                var diagnostic = new EncodingSequence(
                    $"{sequence.FriendlyName}-diagnostic",
                    sequence.ZeroBlock,
                    sequence.Rotation,
                    0,
                    511);

                return Enumerable.Range(501, 11)
                    .Select(rides => new RideEncodingRejectedEntry
                    {
                        Sequence = sequence.FriendlyName,
                        Rides = (uint)rides,
                        Block = diagnostic.Encode((uint)rides).ToHex(),
                        Reason = "application-range",
                    });
            })
            .ToArray();

        return new RideEncodingFixtureDocument
        {
            SchemaVersion = 2,
            FixtureId = "ride-encoding-v2",
            Boundaries = boundaries,
            Sequences = sequences,
            RejectedEncodings = rejectedEncodings,
            MalformedBlocks = BuildMalformedBlocks(),
            MirrorCases = BuildMirrorCases(),
        };
    }

    private static RideEncodingMalformedBlock[] BuildMalformedBlocks()
    {
        return
        [
            new RideEncodingMalformedBlock
            {
                Name = "mercury-wrong-f3-toggle",
                Block = "3FC7414C",
                Reason = "structural",
            },
            new RideEncodingMalformedBlock
            {
                Name = "mercury-wrong-payload",
                Block = "CCC70059",
                Reason = "structural",
            },
            new RideEncodingMalformedBlock
            {
                Name = "mercury-wrong-high-counter-byte",
                Block = "CCC60058",
                Reason = "structural",
            },
            new RideEncodingMalformedBlock
            {
                Name = "venus-wrong-payload",
                Block = "48C70048",
                Reason = "structural",
            },
            new RideEncodingMalformedBlock
            {
                Name = "jupiter-wrong-high-counter-byte",
                Block = "8D124980",
                Reason = "structural",
            },
            new RideEncodingMalformedBlock
            {
                Name = "neptune-wrong-payload",
                Block = "8F1200B0",
                Reason = "structural",
            },
            new RideEncodingMalformedBlock
            {
                Name = "unrelated-block",
                Block = "DEAD1234",
                Reason = "structural",
            },
        ];
    }

    private static RideEncodingMirrorCase[] BuildMirrorCases()
    {
        var mercury73 = EncodingSequences.Mercury.Encode(73);
        var mercury42 = EncodingSequences.Mercury.Encode(42);
        var mercury80 = EncodingSequences.Mercury.Encode(80);
        var venus255 = EncodingSequences.Venus.Encode(255);
        var venus256 = EncodingSequences.Venus.Encode(256);
        var jupiter128 = EncodingSequences.Jupiter.Encode(128);
        var neptune500 = EncodingSequences.Neptune.Encode(500);

        return
        [
            new RideEncodingMirrorCase
            {
                Name = "mercury-matching-valid",
                Block5 = mercury73.ToHex(),
                Block6 = mercury73.ToHex(),
                Expected = Success(73, mercury73, 5, true, null),
            },
            new RideEncodingMirrorCase
            {
                Name = "mercury-only-block-5-valid",
                Block5 = mercury73.ToHex(),
                Block6 = "CCC70001",
                Expected = Success(73, mercury73, 5, false, "Warning: blocks 5 and 6 differ; using block 5."),
            },
            new RideEncodingMirrorCase
            {
                Name = "mercury-only-block-6-valid",
                Block5 = "CCC70000",
                Block6 = mercury42.ToHex(),
                Expected = Success(42, mercury42, 6, false, "Warning: blocks 5 and 6 differ; using block 6."),
            },
            new RideEncodingMirrorCase
            {
                Name = "mercury-both-valid-block-6-wins",
                Block5 = mercury73.ToHex(),
                Block6 = mercury80.ToHex(),
                Expected = Success(80, mercury80, 6, false, "Warning: blocks 5 and 6 differ; using block 6 (80 rides)."),
            },
            new RideEncodingMirrorCase
            {
                Name = "mercury-neither-valid",
                Block5 = "CCC70000",
                Block6 = "CCC70001",
                Expected = Unknown("CCC70000", 5, false),
            },
            new RideEncodingMirrorCase
            {
                Name = "venus-both-valid-block-6-wins",
                Block5 = venus256.ToHex(),
                Block6 = venus255.ToHex(),
                Expected = Success(255, venus255, 6, false, "Warning: blocks 5 and 6 differ; using block 6 (255 rides)."),
            },
            new RideEncodingMirrorCase
            {
                Name = "jupiter-matching-valid",
                Block5 = jupiter128.ToHex(),
                Block6 = jupiter128.ToHex(),
                Expected = Success(128, jupiter128, 5, true, null),
            },
            new RideEncodingMirrorCase
            {
                Name = "neptune-matching-valid",
                Block5 = neptune500.ToHex(),
                Block6 = neptune500.ToHex(),
                Expected = Success(500, neptune500, 5, true, null),
            },
            new RideEncodingMirrorCase
            {
                Name = "neither-valid-unrelated",
                Block5 = "DEAD1234",
                Block6 = "BEEF5678",
                Expected = Unknown("DEAD1234", 5, false),
            },
        ];
    }

    private static RideEncodingExpectedResult Success(
        uint rides,
        T55Block sourceBlock,
        int sourceBlockNumber,
        bool blocksMatched,
        string? warningMessage) =>
        new()
        {
            Status = "success",
            Rides = rides,
            SourceBlock = sourceBlock.ToHex(),
            SourceBlockNumber = sourceBlockNumber,
            BlocksMatched = blocksMatched,
            WarningMessage = warningMessage,
        };

    private static RideEncodingExpectedResult Unknown(string sourceBlock, int sourceBlockNumber, bool blocksMatched) =>
        new()
        {
            Status = "unknownEncodingSequence",
            Rides = null,
            SourceBlock = sourceBlock,
            SourceBlockNumber = sourceBlockNumber,
            BlocksMatched = blocksMatched,
            WarningMessage = null,
        };

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, "TestFixtures", "RideEncoding");
            if (Directory.Exists(path))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate repository root from test output directory.");
    }
}
