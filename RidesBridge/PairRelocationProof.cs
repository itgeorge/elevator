using System.Security.Cryptography;
using System.Text;

namespace RidesBridge;

/// <summary>
/// Version-one token-bound Bonjour relocation proof.
///
/// The persisted paired-client value is V encoded as uppercase hexadecimal, where
/// V = SHA-256(UTF8(bearer)).  The server never persists or returns the bearer or
/// V's bytes.  A client computes:
///
///   locator = HEXUPPER(SHA256(ASCII("ridesbridge-relocation-locator-v1") || 00 || V))
///
/// The unauthenticated proof request carries only locator, a fresh 32-byte nonce
/// encoded as exactly 64 uppercase hexadecimal characters, and a canonical URL.
/// For a current reported URL, the response proof is:
///
///   HEXUPPER(HMAC-SHA256(V,
///       ASCII("ridesbridge-relocation-proof-v1") || 00 || nonce || 00 ||
///       ASCII(uppercase public bridge identifier) || 00 || ASCII(canonical URL) || 00 ||
///       ASCII(apiVersion)))
///
/// NUL bytes are literal single zero bytes and all other separators are omitted.
/// The URL is the ASCII absolute URI with its trailing root slash.  This is a
/// stateless challenge-response: nonce and proof are never stored, and replaying
/// the same request deterministically returns the same proof.
/// </summary>
public static class PairRelocationProof
{
    public const string LocatorContext = "ridesbridge-relocation-locator-v1";
    public const string ProofContext = "ridesbridge-relocation-proof-v1";
    public const int DigestBytes = 32;
    public const int HexDigestLength = DigestBytes * 2;

    public static string ComputeLocator(string bearer)
    {
        ArgumentNullException.ThrowIfNull(bearer);
        return ComputeLocatorFromVerifierBytes(SHA256.HashData(Encoding.UTF8.GetBytes(bearer)));
    }

    public static string ComputeProof(
        string bearer,
        string nonce,
        string bridgeId,
        string canonicalUrl,
        string apiVersion = BridgeOptions.ApiVersion)
    {
        ArgumentNullException.ThrowIfNull(bearer);
        if (!TryDecodeUpperHex(nonce, DigestBytes, out var nonceBytes))
            throw new ArgumentException("Nonce must be exactly 64 uppercase hexadecimal characters.", nameof(nonce));

        var verifierBytes = SHA256.HashData(Encoding.UTF8.GetBytes(bearer));
        return ComputeProofFromVerifierBytes(verifierBytes, nonceBytes, bridgeId, canonicalUrl, apiVersion);
    }

    internal static string ComputeLocatorFromVerifier(string verifier)
    {
        if (!TryDecodeVerifier(verifier, out var verifierBytes))
            throw new ArgumentException("The paired-client verifier is invalid.", nameof(verifier));
        return ComputeLocatorFromVerifierBytes(verifierBytes);
    }

    internal static bool TryDecodeVerifier(string verifier, out byte[] verifierBytes) =>
        TryDecodeUpperHex(verifier, DigestBytes, out verifierBytes);

    internal static string ComputeLocatorFromVerifierBytes(byte[] verifierBytes)
    {
        ArgumentNullException.ThrowIfNull(verifierBytes);
        if (verifierBytes.Length != DigestBytes)
            throw new ArgumentException("A verifier must contain 32 bytes.", nameof(verifierBytes));

        return Convert.ToHexString(SHA256.HashData(BuildTranscript(
            Encoding.ASCII.GetBytes(LocatorContext),
            verifierBytes)));
    }

    internal static string ComputeProofFromVerifierBytes(
        byte[] verifierBytes,
        byte[] nonceBytes,
        string bridgeId,
        string canonicalUrl,
        string apiVersion)
    {
        ArgumentNullException.ThrowIfNull(verifierBytes);
        ArgumentNullException.ThrowIfNull(nonceBytes);
        ArgumentNullException.ThrowIfNull(bridgeId);
        ArgumentNullException.ThrowIfNull(canonicalUrl);
        ArgumentNullException.ThrowIfNull(apiVersion);
        if (verifierBytes.Length != DigestBytes)
            throw new ArgumentException("A verifier must contain 32 bytes.", nameof(verifierBytes));
        if (nonceBytes.Length != DigestBytes)
            throw new ArgumentException("A nonce must contain 32 bytes.", nameof(nonceBytes));

        var uppercaseBridgeId = bridgeId.ToUpperInvariant();
        var data = BuildTranscript(
            Encoding.ASCII.GetBytes(ProofContext),
            nonceBytes,
            Encoding.ASCII.GetBytes(uppercaseBridgeId),
            Encoding.ASCII.GetBytes(canonicalUrl),
            Encoding.ASCII.GetBytes(apiVersion));
        using var hmac = new HMACSHA256(verifierBytes);
        return Convert.ToHexString(hmac.ComputeHash(data));
    }

    internal static bool TryDecodeUpperHex(string? value, int byteCount, out byte[] bytes)
    {
        bytes = [];
        if (value is null || value.Length != byteCount * 2
            || value.Any(c => !(c is >= '0' and <= '9' or >= 'A' and <= 'F')))
            return false;

        try
        {
            bytes = Convert.FromHexString(value);
            return bytes.Length == byteCount;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static byte[] BuildTranscript(params byte[][] fields)
    {
        var length = fields.Sum(field => field.Length + 1) - 1;
        var transcript = new byte[length];
        var offset = 0;
        for (var i = 0; i < fields.Length; i++)
        {
            fields[i].CopyTo(transcript, offset);
            offset += fields[i].Length;
            if (i + 1 < fields.Length)
                transcript[offset++] = 0;
        }
        return transcript;
    }
}
