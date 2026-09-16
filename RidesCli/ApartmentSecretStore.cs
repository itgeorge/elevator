using System.Text;

namespace RidesCli;

/// <summary>
/// In-memory apartment secret cache for the current process.
/// </summary>
public sealed class ApartmentSecretStore
{
    private byte[]? _secret;

    public bool HasSecret => _secret is not null;

    public void SetSecret(ReadOnlySpan<byte> secret)
    {
        _secret = secret.ToArray();
    }

    public void SetSecretFromUtf8(string secret)
    {
        SetSecret(Encoding.UTF8.GetBytes(secret));
    }

    public bool TryGetSecret(out ReadOnlySpan<byte> secret)
    {
        if (_secret is null)
        {
            secret = default;
            return false;
        }

        secret = _secret;
        return true;
    }

    public void Clear() => _secret = null;
}
