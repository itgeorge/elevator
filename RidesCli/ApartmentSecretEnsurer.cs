namespace RidesCli;

/// <summary>
/// Prompts for and caches the apartment secret when it is not already available.
/// </summary>
public sealed class ApartmentSecretEnsurer
{
    private readonly ApartmentSecretStore _store;
    private readonly IRidesInput _input;
    private readonly IRidesOutput _output;

    public ApartmentSecretEnsurer(ApartmentSecretStore store, IRidesInput input, IRidesOutput output)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _output = output ?? throw new ArgumentNullException(nameof(output));
    }

    /// <summary>
    /// Returns the cached secret, prompting once when missing.
    /// </summary>
    public bool EnsureSecret(out ReadOnlySpan<byte> secret)
    {
        if (_store.TryGetSecret(out secret))
            return true;

        _output.WriteLine("Enter apartment secret:");
        var line = _input.ReadSecretLine();
        if (line is null)
        {
            _output.WriteLine("Error: apartment secret entry cancelled.");
            secret = default;
            return false;
        }

        if (line.Length == 0)
        {
            _output.WriteLine("Error: apartment secret cannot be empty.");
            secret = default;
            return false;
        }

        _store.SetSecretFromUtf8(line);
        return _store.TryGetSecret(out secret);
    }
}
