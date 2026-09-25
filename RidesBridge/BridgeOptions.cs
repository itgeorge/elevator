using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using Microsoft.Extensions.Configuration;

namespace RidesBridge;

public sealed record BridgeOptions
{
    // Safe default: local-only binding avoids exposing the bridge until an operator explicitly
    // selects a hotspot/private address or wildcard bind for a physical run.
    public const string DefaultBindUrl = "http://127.0.0.1:5080";
    public const string ApiVersion = "v1";
    public const string BridgeVersion = "1.0.0";

    public string BindUrl { get; init; } = DefaultBindUrl;
    public string? Pm3Port { get; init; }
    public bool Pm3AutoDiscover { get; init; } = true;
    public string? Pm3ClientPath { get; init; }
    /// <summary>When true, the bridge uses an in-process fake PM3 and never opens USB.</summary>
    public bool FakePm3Device { get; init; }
    public static string DefaultDataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ElevatorTokens", "RidesBridge");

    public string DataDirectory { get; init; } = DefaultDataDirectory;
    public string? PairedClientsPath { get; init; }
    public string? BridgeIdentityPath { get; init; }
    /// <summary>Directory for temporary pairing QR PNGs; defaults to the private data directory.</summary>
    public string? PairingQrArtifactDirectory { get; init; }
    public TimeSpan PairingLifetime { get; init; } = TimeSpan.FromMinutes(2);
    /// <summary>Maximum time to wait for the single hardware-operation gate. Must remain below the iPad's 30-second request deadline.</summary>
    public TimeSpan OperationWaitTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Maximum time spent executing one hardware operation after it acquires the gate.
    /// The 20-second default leaves margin below the iPad's 30-second request deadline.
    /// </summary>
    public TimeSpan HardwareExecutionTimeout { get; init; } = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Independent budget for best-effort verification rollback after a mutation failure.
    /// The default keeps wait, execution, and recovery within the tablet's 30-second deadline.
    /// </summary>
    public TimeSpan HardwareRecoveryTimeout { get; init; } = TimeSpan.FromSeconds(4);

    public string EffectivePairedClientsPath => PairedClientsPath ?? Path.Combine(DataDirectory, "paired-clients.json");
    public string EffectiveBridgeIdentityPath => BridgeIdentityPath ?? Path.Combine(DataDirectory, "bridge-id");
    public string EffectivePairingQrArtifactDirectory => PairingQrArtifactDirectory ?? DataDirectory;

    public BridgeOptions Validate()
    {
        ValidateBindUrl(BindUrl);
        if (PairingLifetime <= TimeSpan.Zero || PairingLifetime > TimeSpan.FromMinutes(15))
            throw new BridgeConfigurationException("PairingLifetime must be greater than zero and no more than 15 minutes.");
        if (OperationWaitTimeout <= TimeSpan.Zero || OperationWaitTimeout >= TimeSpan.FromSeconds(30))
            throw new BridgeConfigurationException("OperationWaitTimeout must be greater than zero and less than 30 seconds.");
        if (HardwareExecutionTimeout <= TimeSpan.Zero || HardwareExecutionTimeout >= TimeSpan.FromSeconds(30))
            throw new BridgeConfigurationException("HardwareExecutionTimeout must be greater than zero and less than 30 seconds.");
        if (HardwareRecoveryTimeout <= TimeSpan.Zero || HardwareRecoveryTimeout == Timeout.InfiniteTimeSpan
            || HardwareRecoveryTimeout >= TimeSpan.FromSeconds(30))
            throw new BridgeConfigurationException("HardwareRecoveryTimeout must be finite, greater than zero, and less than 30 seconds.");
        if (OperationWaitTimeout + HardwareExecutionTimeout + HardwareRecoveryTimeout >= TimeSpan.FromSeconds(30))
            throw new BridgeConfigurationException("OperationWaitTimeout, HardwareExecutionTimeout, and HardwareRecoveryTimeout must total less than 30 seconds.");
        if (string.IsNullOrWhiteSpace(DataDirectory) || !Path.IsPathFullyQualified(DataDirectory))
            throw new BridgeConfigurationException("DataDirectory must be an absolute path.");
        if (!string.IsNullOrWhiteSpace(PairedClientsPath) && !Path.IsPathFullyQualified(PairedClientsPath))
            throw new BridgeConfigurationException("PairedClientsPath must be an absolute path.");
        if (!string.IsNullOrWhiteSpace(BridgeIdentityPath) && !Path.IsPathFullyQualified(BridgeIdentityPath))
            throw new BridgeConfigurationException("BridgeIdentityPath must be an absolute path.");
        if (PairingQrArtifactDirectory is not null
            && (string.IsNullOrWhiteSpace(PairingQrArtifactDirectory) || !Path.IsPathFullyQualified(PairingQrArtifactDirectory)))
            throw new BridgeConfigurationException("PairingQrArtifactDirectory must be a non-empty absolute path.");
        if (!FakePm3Device && !Pm3AutoDiscover && string.IsNullOrWhiteSpace(Pm3Port))
            throw new BridgeConfigurationException("Pm3Port is required when Pm3AutoDiscover is false.");
        return this;
    }

    public static BridgeOptions FromConfiguration(IConfiguration configuration) =>
        FromConfiguration(configuration, everydayMode: false);

    internal static BridgeOptions FromConfiguration(IConfiguration configuration, bool everydayMode)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var bindUrl = everydayMode
            ? DefaultBindUrl
            : configuration["Bridge:BindUrl"] ?? configuration["BRIDGE_BIND_URL"] ?? DefaultBindUrl;
        var dataDirectory = configuration["Bridge:DataDirectory"]
            ?? configuration["BRIDGE_DATA_DIRECTORY"]
            ?? DefaultDataDirectory;
        var pairedPath = configuration["Bridge:PairedClientsPath"]
            ?? configuration["BRIDGE_PAIRED_CLIENTS_PATH"];
        var identityPath = configuration["Bridge:IdentityPath"]
            ?? configuration["BRIDGE_IDENTITY_PATH"];
        var pairingQrArtifactDirectory = configuration["Bridge:PairingQrArtifactDirectory"]
            ?? configuration["BRIDGE_PAIRING_QR_ARTIFACT_DIRECTORY"];
        var port = everydayMode
            ? null
            : configuration["Pm3:Port"] ?? configuration["PM3_PORT"];
        var autoKey = configuration["Pm3:AutoDiscover"] is not null ? "Pm3:AutoDiscover" : "PM3_AUTO_DISCOVER";
        var auto = everydayMode ? null : configuration[autoKey];
        var autoDiscover = everydayMode || ParseBoolean(auto, autoKey, defaultValue: true);
        var pairingLifetime = ParseSeconds(configuration,
            "Bridge:PairingLifetimeSeconds", "BRIDGE_PAIRING_LIFETIME_SECONDS", 120);
        var operationWait = ParseSeconds(configuration,
            "Bridge:OperationWaitTimeoutSeconds", "BRIDGE_OPERATION_WAIT_TIMEOUT_SECONDS", 5);
        var hardwareExecution = ParseSeconds(configuration,
            "Bridge:HardwareExecutionTimeoutSeconds", "BRIDGE_HARDWARE_EXECUTION_TIMEOUT_SECONDS", 20);
        var hardwareRecovery = ParseSeconds(configuration,
            "Bridge:HardwareRecoveryTimeoutSeconds", "BRIDGE_HARDWARE_RECOVERY_TIMEOUT_SECONDS", 4);

        return new BridgeOptions
        {
            BindUrl = bindUrl,
            DataDirectory = dataDirectory,
            PairedClientsPath = pairedPath,
            BridgeIdentityPath = identityPath,
            PairingQrArtifactDirectory = pairingQrArtifactDirectory,
            Pm3Port = port,
            Pm3AutoDiscover = autoDiscover,
            Pm3ClientPath = configuration["Pm3:ClientPath"] ?? configuration["PM3_CLIENT_PATH"],
            PairingLifetime = pairingLifetime,
            OperationWaitTimeout = operationWait,
            HardwareExecutionTimeout = hardwareExecution,
            HardwareRecoveryTimeout = hardwareRecovery,
        };
    }

    private static bool ParseBoolean(string? value, string key, bool defaultValue)
    {
        if (value is null)
            return defaultValue;
        if (bool.TryParse(value, out var parsed))
            return parsed;
        throw new BridgeConfigurationException($"{key} must be true or false.");
    }

    private static TimeSpan ParseSeconds(IConfiguration configuration, string primaryKey, string environmentKey, double defaultSeconds)
    {
        var key = configuration[primaryKey] is not null ? primaryKey : environmentKey;
        var value = configuration[key];
        if (value is null)
            return TimeSpan.FromSeconds(defaultSeconds);
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
            || !double.IsFinite(seconds))
            throw new BridgeConfigurationException($"{key} must be a finite number of seconds.");
        try
        {
            return TimeSpan.FromSeconds(seconds);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new BridgeConfigurationException($"{key} is outside the supported time span.", ex);
        }
    }

    public static void ValidateBindUrl(string? bindUrl)
    {
        if (string.IsNullOrWhiteSpace(bindUrl) || !Uri.TryCreate(bindUrl, UriKind.Absolute, out var uri))
            throw new BridgeConfigurationException("BindUrl must be an absolute HTTP URL with an explicit port.");
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
            throw new BridgeConfigurationException("BindUrl must use HTTP; HTTPS termination is outside this local bridge.");
        if (uri.UserInfo.Length != 0 || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)
            || (uri.AbsolutePath.Length > 1 && uri.AbsolutePath != "/"))
            throw new BridgeConfigurationException("BindUrl must contain only an HTTP host and port, without credentials, query, or path.");
        if (!HasExplicitPort(bindUrl) || uri.Port is < 1 or > 65535)
            throw new BridgeConfigurationException("BindUrl must specify a TCP port from 1 through 65535.");
        if (uri.HostNameType == UriHostNameType.IPv6 || uri.HostNameType == UriHostNameType.Dns && !uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            throw new BridgeConfigurationException("BindUrl must use localhost, loopback, wildcard, or a private IPv4 address; public/DNS binds are rejected.");

        if (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            return;
        if (!IPAddress.TryParse(uri.Host, out var address) || address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            throw new BridgeConfigurationException("BindUrl must use localhost, loopback, wildcard, or a private IPv4 address; public/DNS binds are rejected.");
        if (!IsAllowedBindAddress(address))
            throw new BridgeConfigurationException($"BindUrl address {address} is not a local/private IPv4 address.");
    }

    public static IReadOnlyList<Uri> GetUsablePrivateIpv4Urls(int port)
    {
        var addresses = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Select(a => a.Address);
        return GetUsablePrivateIpv4Urls(port, addresses);
    }

    public static IReadOnlyList<Uri> GetUsablePrivateIpv4Urls(int port, IEnumerable<IPAddress> addresses)
    {
        if (port is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(port));
        ArgumentNullException.ThrowIfNull(addresses);

        return addresses
            .Where(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            .Where(IsPrivateIpv4)
            .Distinct()
            .OrderBy(a => a.ToString(), StringComparer.Ordinal)
            .Select(a => new Uri($"http://{a}:{port}/"))
            .ToList();
    }

    public int BindPort
    {
        get
        {
            ValidateBindUrl(BindUrl);
            return new Uri(BindUrl).Port;
        }
    }

    public IReadOnlyList<Uri> GetReportedUrls() => GetReportedUrls(GetActiveIpv4Addresses());

    /// <summary>
    /// Computes URLs that are reachable under the configured bind semantics.
    /// A wildcard bind can serve every matching interface; a specific bind can serve only itself.
    /// </summary>
    public IReadOnlyList<Uri> GetReportedUrls(IEnumerable<IPAddress> addresses)
    {
        ValidateBindUrl(BindUrl);
        ArgumentNullException.ThrowIfNull(addresses);

        var bindUri = new Uri(BindUrl);
        if (bindUri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || !IPAddress.TryParse(bindUri.Host, out var bindAddress))
            return [];
        if (IPAddress.IsLoopback(bindAddress))
            return [];
        if (bindAddress.Equals(IPAddress.Any))
            return GetUsablePrivateIpv4Urls(bindUri.Port, addresses);
        if (IsPrivateIpv4(bindAddress))
            return [new Uri($"http://{bindAddress}:{bindUri.Port}/")];
        return [];
    }

    private static IEnumerable<IPAddress> GetActiveIpv4Addresses() => NetworkInterface.GetAllNetworkInterfaces()
        // A wildcard listener should not publish addresses from a down, point-to-point,
        // or non-multicast interface. Haukcode.Mdns advertises over multicast links, so
        // those addresses cannot be reached by a Bonjour browser even though HTTP may be
        // bound to the wildcard socket.
        .Where(n => n.OperationalStatus == OperationalStatus.Up
            && n.SupportsMulticast
            && n.NetworkInterfaceType is not NetworkInterfaceType.Loopback
            and not NetworkInterfaceType.Ppp
            and not NetworkInterfaceType.Tunnel)
        .SelectMany(n => n.GetIPProperties().UnicastAddresses)
        .Select(a => a.Address);

    internal static bool IsPrivateIpv4(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes.Length == 4 && (bytes[0] == 10
            || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
            || (bytes[0] == 192 && bytes[1] == 168)
            || (bytes[0] == 169 && bytes[1] == 254));
    }

    private static bool IsAllowedBindAddress(IPAddress address) => IPAddress.IsLoopback(address)
        || address.Equals(IPAddress.Any)
        || IsPrivateIpv4(address);

    private static bool HasExplicitPort(string value)
    {
        var authority = value[(value.IndexOf("//", StringComparison.Ordinal) + 2)..];
        authority = authority.Split(['/', '?', '#'], 2)[0];
        return authority.LastIndexOf(':') > 0;
    }
}

public sealed class BridgeConfigurationException : Exception
{
    public BridgeConfigurationException(string message) : base(message) { }
    public BridgeConfigurationException(string message, Exception innerException) : base(message, innerException) { }
}
