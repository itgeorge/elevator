namespace Pm3UsbApi;

/// <summary>
/// LF chip families recognized by native detect but not supported by elevator tooling.
/// </summary>
public enum Pm3LfChipFamily
{
    Em410x,
    NonT55Lf,
}

/// <summary>
/// Human-readable names for unsupported LF chip families.
/// </summary>
public static class Pm3LfChipFamilyNames
{
    public static string Name(Pm3LfChipFamily family) => family switch
    {
        Pm3LfChipFamily.Em410x => "EM410x",
        Pm3LfChipFamily.NonT55Lf => "non-T55 LF",
        _ => family.ToString(),
    };

    public static string FormatCardId(Pm3LfChipFamily family, ulong cardId) => family switch
    {
        Pm3LfChipFamily.Em410x => cardId.ToString("X10"),
        Pm3LfChipFamily.NonT55Lf => cardId == 0 ? "unknown" : cardId.ToString("X"),
        _ => cardId.ToString("X"),
    };
}
