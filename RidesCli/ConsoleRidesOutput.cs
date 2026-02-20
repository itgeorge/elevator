namespace RidesCli;

/// <summary>
/// Writes output to Console.
/// </summary>
public sealed class ConsoleRidesOutput : IRidesOutput
{
    public void WriteLine(string line) => Console.WriteLine(line);
}
