namespace RidesCli;

/// <summary>
/// Abstraction for writing output. Allows tests to capture output.
/// </summary>
public interface IRidesOutput
{
    void WriteLine(string line);
}
