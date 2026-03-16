using RidesCli;

namespace RidesCli.Tests;

public sealed class ScriptedRidesInput : IRidesInput
{
    private readonly Queue<string?> _responses;

    public ScriptedRidesInput(params string?[] responses)
    {
        _responses = new Queue<string?>(responses);
    }

    public string? ReadLine() => _responses.Count > 0 ? _responses.Dequeue() : string.Empty;
}
