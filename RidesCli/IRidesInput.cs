namespace RidesCli;

/// <summary>
/// Abstraction for reading interactive user input.
/// </summary>
public interface IRidesInput
{
    string? ReadLine();

    /// <summary>Read a line without echoing characters (for secrets).</summary>
    string? ReadSecretLine();
}
