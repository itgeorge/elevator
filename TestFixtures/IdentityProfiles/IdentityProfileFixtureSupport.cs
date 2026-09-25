using System.Text.Json;
using System.Text.RegularExpressions;
using Tokens;

namespace TestFixtures.IdentityProfiles;

public static class IdentityProfileFixtureSupport
{
    public const string FixtureFileName = "identity-profiles-v1.json";

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static string FixturePath() => LocateFixture(FixtureFileName);

    public static IdentityProfileFixtureDocument Load()
    {
        var fixture = JsonSerializer.Deserialize<IdentityProfileFixtureDocument>(
            File.ReadAllText(FixturePath()),
            JsonOptions);
        if (fixture is null)
            throw new InvalidOperationException("Identity profile fixture deserialized to null.");

        return fixture;
    }

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
            var path = Path.Combine(directory.FullName, "TestFixtures", "IdentityProfiles", fileName);
            if (File.Exists(path))
                return path;
        }

        throw new FileNotFoundException($"Could not locate the checked-in identity profile fixture '{fileName}'.");
    }
}
