namespace TestFixtures.IdentityProfiles;

public sealed class IdentityProfileFixtureDocument
{
    public int SchemaVersion { get; init; }
    public string FixtureId { get; init; } = string.Empty;
    public IdentityProfileFixtureEntry[] Profiles { get; init; } = [];
}

public sealed class IdentityProfileFixtureEntry
{
    public string FriendlyName { get; init; } = string.Empty;
    public string RideSequence { get; init; } = string.Empty;
    public string TokenId { get; init; } = string.Empty;
    public string Block1 { get; init; } = string.Empty;
    public string Block2 { get; init; } = string.Empty;
    public string Block3 { get; init; } = string.Empty;
    public string Block4 { get; init; } = string.Empty;
    public bool CanReset { get; init; }
    public string? ResetImageFileName { get; init; }
    public string[]? ResetImage { get; init; }
}
