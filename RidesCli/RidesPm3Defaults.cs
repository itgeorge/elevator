namespace RidesCli;

/// <summary>
/// PM3 tuning defaults used by RidesCli (native LF tune sample count).
/// </summary>
internal static class RidesPm3Defaults
{
    /// <summary>
    /// Fewer samples than library default for faster tune during ride operations.
    /// </summary>
    public const int LfTuneSampleCount = 20;
}
