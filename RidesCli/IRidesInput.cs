namespace RidesCli;

/// <summary>
/// Abstraction for reading interactive user input.
/// </summary>
public interface IRidesInput
{
    string? ReadLine();
}
