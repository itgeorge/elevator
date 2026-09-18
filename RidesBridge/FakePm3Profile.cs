using Microsoft.Extensions.Configuration;

namespace RidesBridge;

/// <summary>Deterministic fake PM3 seed selected for <c>--fake-pm3</c> launch mode.</summary>
public enum FakePm3Profile
{
    /// <summary>Known Venus mirrors at 180 rides (default).</summary>
    Venus,

    /// <summary>Empty antenna: scan and chip-dependent reads throw <see cref="BridgeHardwareError.NoChip"/>.</summary>
    NoChip,

    /// <summary>Chip present but scan tune fails with <see cref="BridgeHardwareError.TuneFailed"/>.</summary>
    TuneFailed,

    /// <summary>Chip present but page-0 scan read fails with <see cref="BridgeHardwareError.ReadFailed"/>.</summary>
    ReadFailed,

    /// <summary>Undecodable mirrors (DEADBEEF/FACECAFE) for Concept A unknown → missing dump smoke.</summary>
    Unknown,
}

public static class FakePm3ProfileResolver
{
    public const string ConfigurationKey = "Bridge:FakePm3Profile";
    public const string EnvironmentKey = "RIDES_FAKE_PM3_PROFILE";

    public static FakePm3Profile Resolve(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var raw = configuration[ConfigurationKey] ?? configuration[EnvironmentKey];
        if (string.IsNullOrWhiteSpace(raw))
            return FakePm3Profile.Venus;
        return Parse(raw.Trim());
    }

    public static FakePm3Device CreateDevice(FakePm3Profile profile) => profile switch
    {
        FakePm3Profile.Venus => FakePm3Device.CreateSeeded(),
        FakePm3Profile.NoChip => FakePm3Device.CreateNoChip(),
        FakePm3Profile.TuneFailed => FakePm3Device.CreateTuneFailed(),
        FakePm3Profile.ReadFailed => FakePm3Device.CreateReadFailed(),
        FakePm3Profile.Unknown => FakePm3Device.CreateUnknownMirrorsSeeded(),
        _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, "Unsupported fake PM3 profile."),
    };

    public static string GetDisplayName(FakePm3Profile profile) => profile switch
    {
        FakePm3Profile.Venus => "venus",
        FakePm3Profile.NoChip => "no-chip",
        FakePm3Profile.TuneFailed => "tune-failed",
        FakePm3Profile.ReadFailed => "read-failed",
        FakePm3Profile.Unknown => "unknown",
        _ => profile.ToString().ToLowerInvariant(),
    };

    private static FakePm3Profile Parse(string value)
    {
        if (IsProfile(value, "venus", "default"))
            return FakePm3Profile.Venus;
        if (IsProfile(value, "no-chip", "nochip", "no_chip"))
            return FakePm3Profile.NoChip;
        if (IsProfile(value, "tune-failed", "tunefailed", "lf_tune_failed"))
            return FakePm3Profile.TuneFailed;
        if (IsProfile(value, "read-failed", "readfailed", "page0_read_failed"))
            return FakePm3Profile.ReadFailed;
        if (IsProfile(value, "unknown", "unknown-mirrors", "unknown_mirrors"))
            return FakePm3Profile.Unknown;

        throw new BridgeConfigurationException(
            $"{ConfigurationKey} / {EnvironmentKey} must be venus, default, no-chip, tune-failed, read-failed, or unknown; received '{value}'.");
    }

    private static bool IsProfile(string value, params string[] names)
    {
        foreach (var name in names)
        {
            if (string.Equals(value, name, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
