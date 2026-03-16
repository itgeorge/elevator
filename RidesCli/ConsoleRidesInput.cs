namespace RidesCli;

/// <summary>
/// Reads input from Console.
/// </summary>
public sealed class ConsoleRidesInput : IRidesInput
{
    public string? ReadLine() => Console.ReadLine();
}
