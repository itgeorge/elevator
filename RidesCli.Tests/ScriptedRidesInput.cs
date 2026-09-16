using RidesCli;

namespace RidesCli.Tests;

public sealed class ScriptedRidesInput : IRidesInput
{
    private readonly Queue<string?> _responses;
    private readonly Queue<string?> _secretResponses;

    public ScriptedRidesInput(params string?[] responses)
        : this(responses, Array.Empty<string?>())
    {
    }

    public ScriptedRidesInput(IReadOnlyList<string?> responses, IReadOnlyList<string?> secretResponses)
    {
        _responses = new Queue<string?>(responses);
        _secretResponses = new Queue<string?>(secretResponses);
    }

    public Action? BeforeReadLine { get; set; }

    public Action? BeforeReadSecretLine { get; set; }

    public int ReadSecretLineCallCount { get; private set; }

    public string? ReadLine()
    {
        BeforeReadLine?.Invoke();
        return _responses.Count > 0 ? _responses.Dequeue() : string.Empty;
    }

    public string? ReadSecretLine()
    {
        BeforeReadSecretLine?.Invoke();
        ReadSecretLineCallCount++;
        return _secretResponses.Count > 0 ? _secretResponses.Dequeue() : string.Empty;
    }
}
