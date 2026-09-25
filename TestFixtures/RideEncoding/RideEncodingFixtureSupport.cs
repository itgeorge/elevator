using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Tokens;

namespace TestFixtures.RideEncoding;

public static class RideEncodingFixtureSupport
{
    public const string FixtureFileName = "ride-encoding-v2.json";

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static string FixturePath() => LocateFixture(FixtureFileName);

    public static RideEncodingFixtureDocument Load()
    {
        var fixture = JsonSerializer.Deserialize<RideEncodingFixtureDocument>(
            File.ReadAllText(FixturePath()),
            JsonOptions);
        if (fixture is null)
            throw new InvalidOperationException("Ride encoding fixture deserialized to null.");

        return fixture;
    }

    public static EncodingSequence GetOracleSequence(string name) =>
        EncodingSequences.All.Single(sequence =>
            string.Equals(sequence.FriendlyName, name, StringComparison.OrdinalIgnoreCase));

    public static T55Block ParseBlock(string value)
    {
        if (!Regex.IsMatch(value, "^[0-9A-F]{8}$"))
            throw new ArgumentException($"Block must be eight uppercase hex digits: {value}", nameof(value));

        return T55Block.FromHex(value);
    }

    private static string LocateFixture(string fileName)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, "TestFixtures", "RideEncoding", fileName);
            if (File.Exists(path))
                return path;
        }

        throw new FileNotFoundException($"Could not locate the checked-in ride encoding fixture '{fileName}'.");
    }
}
