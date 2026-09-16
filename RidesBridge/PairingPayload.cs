using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using QRCoder;

namespace RidesBridge;

/// <summary>
/// The only data placed in a first-pairing QR code. The PIN is short-lived and one-time, not a
/// bearer credential; PairingCodeService is the authoritative replay protection, so no separate
/// QR nonce is carried here.
/// </summary>
public sealed record PairingPayload(
    string Version,
    Uri BridgeUrl,
    string Pin,
    DateTimeOffset ExpiresAt,
    string BridgeId,
    string ApiVersion)
{
    public const string CurrentType = "ridesbridge-pairing";
    public const string CurrentVersion = "v1";
    private const int BridgeIdLength = 32;
    private static readonly string[] PropertyNames = ["type", "version", "url", "pin", "expiresAt", "bridgeId", "apiVersion"];

    /// <summary>Exact protocol discriminator; it prevents unrelated JSON from being treated as pairing data.</summary>
    public string Type { get; init; } = CurrentType;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = null, WriteIndented = false };

    public bool IsExpired(TimeProvider? clock = null) => ExpiresAt <= (clock ?? TimeProvider.System).GetUtcNow();

    public static string Serialize(PairingPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ValidateSyntax(payload);
        return JsonSerializer.Serialize(new PairingPayloadJson
        {
            Type = payload.Type,
            Version = payload.Version,
            Url = ValidateBridgeUrl(payload.BridgeUrl).ToString(),
            Pin = payload.Pin,
            ExpiresAt = payload.ExpiresAt,
            BridgeId = payload.BridgeId,
            ApiVersion = payload.ApiVersion,
        }, JsonOptions);
    }

    public static bool TryParse(string? json, TimeProvider? clock, out PairingPayload? payload, out string? error)
    {
        payload = null;
        error = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            error = "Pairing payload is empty.";
            return false;
        }
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                error = "Pairing payload must be a JSON object.";
                return false;
            }
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in root.EnumerateObject())
            {
                if (!PropertyNames.Contains(property.Name, StringComparer.Ordinal) || !seen.Add(property.Name))
                {
                    error = "Pairing payload contains an unsupported or duplicate property.";
                    return false;
                }
            }
            if (seen.Count != PropertyNames.Length)
            {
                error = "Pairing payload is missing a required property.";
                return false;
            }
            var value = JsonSerializer.Deserialize<PairingPayloadJson>(root.GetRawText(), JsonOptions) ?? throw new JsonException();
            if (!string.Equals(value.Type, CurrentType, StringComparison.Ordinal))
            {
                error = $"Unsupported pairing payload type; expected {CurrentType}.";
                return false;
            }
            if (value.Version is null || !string.Equals(value.Version, CurrentVersion, StringComparison.Ordinal))
            {
                error = $"Unsupported pairing payload version; expected {CurrentVersion}.";
                return false;
            }
            if (value.Url is null || !Uri.TryCreate(value.Url, UriKind.Absolute, out var url))
            {
                error = "Pairing payload URL is invalid.";
                return false;
            }
            url = ValidateBridgeUrl(url);
            if (value.Pin is null || !IsPin(value.Pin))
            {
                error = "Pairing payload PIN is invalid.";
                return false;
            }
            if (value.BridgeId is null || !IsBridgeId(value.BridgeId))
            {
                error = "Pairing payload bridge identity is invalid.";
                return false;
            }
            if (!string.Equals(value.ApiVersion, BridgeOptions.ApiVersion, StringComparison.Ordinal))
            {
                error = "Pairing payload API version is unsupported.";
                return false;
            }
            var parsed = new PairingPayload(value.Version, url, value.Pin, value.ExpiresAt, value.BridgeId, value.ApiVersion!)
            {
                Type = value.Type!,
            };
            if (parsed.IsExpired(clock))
            {
                error = "Pairing payload has expired.";
                return false;
            }
            payload = parsed;
            return true;
        }
        catch (UriFormatException) { error = "Pairing payload URL is invalid."; return false; }
        catch (JsonException) { error = "Pairing payload is not valid JSON."; return false; }
        catch (BridgeConfigurationException ex) { error = ex.Message; return false; }
        catch (FormatException) { error = "Pairing payload contains an invalid value."; return false; }
    }

    public static bool TryParse(string? json, out PairingPayload? payload, out string? error) => TryParse(json, TimeProvider.System, out payload, out error);

    /// <summary>Validates a client-usable local URL. Wildcard binds must first be replaced by a reported private interface URL.</summary>
    public static Uri ValidateBridgeUrl(Uri url)
    {
        ArgumentNullException.ThrowIfNull(url);
        if (!string.Equals(url.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            || url.UserInfo.Length != 0 || !string.IsNullOrEmpty(url.Query) || !string.IsNullOrEmpty(url.Fragment)
            || (url.AbsolutePath.Length > 1 && url.AbsolutePath != "/") || url.HostNameType != UriHostNameType.IPv4
            || url.Port is < 1 or > 65535 || (url.IsDefaultPort && !HasExplicitPort(url.OriginalString)))
            throw new BridgeConfigurationException("Pairing URL must use an explicit-port HTTP private IPv4 address with no credentials, query, fragment, or path.");

        if (!IPAddress.TryParse(url.Host, out var address)
            || address.AddressFamily != AddressFamily.InterNetwork
            || !BridgeOptions.IsPrivateIpv4(address))
            throw new BridgeConfigurationException("Pairing URL must use an explicit-port HTTP private IPv4 address; localhost, loopback, wildcard, public, and DNS hosts are not allowed.");
        return new UriBuilder(Uri.UriSchemeHttp, address.ToString(), url.Port).Uri;
    }

    private static void ValidateSyntax(PairingPayload payload)
    {
        if (!string.Equals(payload.Type, CurrentType, StringComparison.Ordinal))
            throw new BridgeConfigurationException($"Unsupported pairing payload type; expected {CurrentType}.");
        if (!string.Equals(payload.Version, CurrentVersion, StringComparison.Ordinal))
            throw new BridgeConfigurationException($"Unsupported pairing payload version; expected {CurrentVersion}.");
        ValidateBridgeUrl(payload.BridgeUrl);
        if (!IsPin(payload.Pin)) throw new BridgeConfigurationException("Pairing payload PIN must contain exactly six digits.");
        if (!IsBridgeId(payload.BridgeId)) throw new BridgeConfigurationException("Pairing payload bridge identity is invalid.");
        if (!string.Equals(payload.ApiVersion, BridgeOptions.ApiVersion, StringComparison.Ordinal))
            throw new BridgeConfigurationException("Pairing payload API version is unsupported.");
        if (payload.ExpiresAt == default) throw new BridgeConfigurationException("Pairing payload expiration is required.");
    }

    internal static bool IsPin(string? value) => value is { Length: 6 } && value.All(c => c is >= '0' and <= '9');
    internal static bool IsBridgeId(string? value) => value is { Length: BridgeIdLength } && value.All(Uri.IsHexDigit);

    private static bool HasExplicitPort(string value)
    {
        var schemeSeparator = value.IndexOf("//", StringComparison.Ordinal);
        if (schemeSeparator < 0) return false;
        var authority = value[(schemeSeparator + 2)..].Split(['/', '?', '#'], 2)[0];
        return authority.LastIndexOf(':') > 0;
    }

    private sealed class PairingPayloadJson
    {
        [JsonPropertyName("type")] public string? Type { get; init; }
        [JsonPropertyName("version")] public string? Version { get; init; }
        [JsonPropertyName("url")] public string? Url { get; init; }
        [JsonPropertyName("pin")] public string? Pin { get; init; }
        [JsonPropertyName("expiresAt")] public DateTimeOffset ExpiresAt { get; init; }
        [JsonPropertyName("bridgeId")] public string? BridgeId { get; init; }
        [JsonPropertyName("apiVersion")] public string? ApiVersion { get; init; }
    }
}

public static class PairingPayloadFactory
{
    public static IReadOnlyList<PairingPayload> CreateForReportedUrls(
        BridgeOptions options,
        PairingCode pairingCode,
        string bridgeId,
        IEnumerable<IPAddress> addresses,
        TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(pairingCode);
        ArgumentNullException.ThrowIfNull(addresses);
        if (!PairingPayload.IsPin(pairingCode.Value))
            throw new BridgeConfigurationException("The active pairing PIN is invalid.");
        if (!PairingPayload.IsBridgeId(bridgeId))
            throw new BridgeConfigurationException("The bridge identity is invalid.");
        if (pairingCode.ExpiresAt <= (clock ?? TimeProvider.System).GetUtcNow())
            return [];

        return options.GetReportedUrls(addresses)
            .Select(url => new PairingPayload(
                PairingPayload.CurrentVersion,
                PairingPayload.ValidateBridgeUrl(url),
                pairingCode.Value,
                pairingCode.ExpiresAt,
                bridgeId,
                BridgeOptions.ApiVersion))
            .ToList();
    }
}

public sealed class BridgeIdentityService
{
    private const int IdentityBytes = 16;
    private readonly string _path;

    public BridgeIdentityService(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A bridge identity path is required.", nameof(path));
        _path = Path.GetFullPath(path);
        Id = LoadOrCreate();
    }

    public string Id { get; }

    private string LoadOrCreate()
    {
        var directory = Path.GetDirectoryName(_path);
        if (string.IsNullOrEmpty(directory))
            throw new BridgeConfigurationException("Bridge identity path has no directory.");
        Directory.CreateDirectory(directory);

        // FileShare.None is a crash-released cross-process creation lock. It also makes the
        // concurrent-constructor test exercise the same path as separate bridge processes.
        using var creationLock = AcquireCreationLock();
        if (File.Exists(_path))
            return ReadExisting();

        Span<byte> bytes = stackalloc byte[IdentityBytes];
        RandomNumberGenerator.Fill(bytes);
        var identity = Convert.ToHexString(bytes);
        var tempPath = $"{_path}.{Guid.NewGuid():N}.tmp";
        try
        {
            WriteCompleteTempFile(tempPath, identity);
            try
            {
                // This overload is an atomic rename and deliberately does not overwrite. A
                // process that loses the race must observe only the already-published final.
                File.Move(tempPath, _path);
                return identity;
            }
            catch (IOException) when (File.Exists(_path))
            {
                // Keep this fallback for an external creator that does not use our lock. The
                // destination is read only after the no-overwrite publication has completed.
                return ReadExisting();
            }
        }
        finally
        {
            // A crash can leave an orphan, but a live constructor always cleans up its own
            // unpublished candidate. Never remove another process's candidate here.
            try { File.Delete(tempPath); }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private FileStream AcquireCreationLock()
    {
        var lockPath = _path + ".lock";
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (true)
        {
            try
            {
                return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1);
            }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                Thread.Sleep(10);
            }
            catch (IOException ex)
            {
                throw new BridgeConfigurationException($"Bridge identity lock is unavailable: {ex.Message}");
            }
        }
    }

    private static void WriteCompleteTempFile(string path, string identity)
    {
        var bytes = System.Text.Encoding.ASCII.GetBytes(identity + Environment.NewLine);
        using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            options: FileOptions.WriteThrough);
        stream.Write(bytes, 0, bytes.Length);
        stream.Flush(flushToDisk: true);
    }

    private string ReadExisting()
    {
        var existing = File.ReadAllText(_path).Trim();
        if (!PairingPayload.IsBridgeId(existing))
            throw new BridgeConfigurationException("Bridge identity file is invalid.");
        return existing.ToUpperInvariant();
    }
}

public sealed class TerminalQrRenderer
{
    private const string DarkForeground = "\u001b[30m";
    private const string LightForeground = "\u001b[37m";
    private const string DarkBackground = "\u001b[40m";
    private const string LightBackground = "\u001b[47m";
    private const string Reset = "\u001b[0m";
    private const char UpperHalfBlock = '\u2580';

    public string Render(string payload)
    {
        var modules = GetLogicalMatrix(payload);
        var output = new System.Text.StringBuilder();
        for (var y = 0; y < modules.Length; y += 2)
        {
            var bottomExists = y + 1 < modules.Length;
            for (var x = 0; x < modules[y].Length; x++)
            {
                var top = modules[y][x];
                var bottom = bottomExists && modules[y + 1][x];
                output.Append(top ? DarkForeground : LightForeground)
                    .Append(bottom ? DarkBackground : LightBackground)
                    .Append(UpperHalfBlock)
                    .Append(Reset);
            }
            if (y + 2 < modules.Length) output.Append('\n');
        }
        return output.ToString();
    }

    /// <summary>Returns the encoder's complete matrix, including QRCoder's quiet zone.</summary>
    internal bool[][] GetLogicalMatrix(string payload)
    {
        if (string.IsNullOrEmpty(payload))
            throw new ArgumentException("A QR payload is required.", nameof(payload));

        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.M);
        var matrix = data.ModuleMatrix;
        if (matrix.Count == 0 || matrix.Any(row => row.Length != matrix.Count))
            throw new InvalidOperationException("QR encoder returned an invalid module matrix.");

        return matrix
            .Select(row => Enumerable.Range(0, row.Length).Select(row.Get).ToArray())
            .ToArray();
    }
}

public static class BridgeTerminalDisplay
{
    public static void Write(
        BridgeOptions options,
        PairingCodeService pairing,
        string bridgeId,
        TextWriter output,
        IEnumerable<IPAddress>? addresses = null,
        TerminalQrRenderer? renderer = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(pairing);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(bridgeId);
        renderer ??= new TerminalQrRenderer();

        var pairingCode = pairing.GetActiveCode() ?? pairing.IssueCode();
        output.WriteLine($"RidesBridge API {BridgeOptions.ApiVersion} listening.");
        output.WriteLine($"Pairing PIN: {pairingCode.Value} (expires {pairingCode.ExpiresAt:O})");

        var payloads = addresses is null
            ? PairingPayloadFactory.CreateForReportedUrls(options, pairingCode, bridgeId, GetActiveAddresses())
            : PairingPayloadFactory.CreateForReportedUrls(options, pairingCode, bridgeId, addresses);
        if (payloads.Count == 0)
        {
            output.WriteLine("No non-loopback private URL is available for QR pairing; use the displayed PIN with a manual URL.");
            return;
        }

        foreach (var payload in payloads)
        {
            output.WriteLine($"Reachable private URL: {payload.BridgeUrl}");
            output.WriteLine($"Pairing QR for {payload.BridgeUrl}:");
            output.WriteLine(renderer.Render(PairingPayload.Serialize(payload)));
        }
    }

    private static IEnumerable<IPAddress> GetActiveAddresses() =>
        System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Select(a => a.Address);
}
