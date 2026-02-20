namespace RidesCli.Tests;

public sealed class StringBuilderRidesOutput : IRidesOutput
{
    private readonly List<string> _lines = [];

    public IReadOnlyList<string> Lines => _lines;

    public void WriteLine(string line) => _lines.Add(line);

    public void Clear() => _lines.Clear();
}
