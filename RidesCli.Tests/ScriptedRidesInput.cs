using RidesCli;

namespace RidesCli.Tests;

public sealed class ScriptedRidesInput : IRidesInput
{
    private readonly Queue<string?> _responses;

    public ScriptedRidesInput(params string?[] responses)
    {
        _responses = new Queue<string?>(responses);
    }

    public Action? BeforeReadLine { get; set; }

    public string? ReadLine()
    {
        BeforeReadLine?.Invoke();
        return _responses.Count > 0 ? _responses.Dequeue() : string.Empty;
    }
}
