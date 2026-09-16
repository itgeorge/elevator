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
    public string DataDirectory { get; init; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ElevatorTokens", "RidesBridge");
    public string? PairedClientsPath { get; init; }
    public TimeSpan PairingLifetime { get; init; } = TimeSpan.FromMinutes(2);
    public TimeSpan OperationWaitTimeout { get; init; } = TimeSpan.FromSeconds(30);

    public string EffectivePairedClientsPath => PairedClientsPath ?? Path.Combine(DataDirectory, "paired-clients.json");

    public BridgeOptions Validate()
    {
        ValidateBindUrl(BindUrl);
        if (PairingLifetime <= TimeSpan.Zero || PairingLifetime > TimeSpan.FromMinutes(15))
            throw new BridgeConfigurationException("PairingLifetime must be greater than zero and no more than 15 minutes.");
        if (OperationWaitTimeout <= TimeSpan.Zero || OperationWaitTimeout > TimeSpan.FromMinutes(5))
            throw new BridgeConfigurationException("OperationWaitTimeout must be greater than zero and no more than 5 minutes.");
        if (string.IsNullOrWhiteSpace(DataDirectory) || !Path.IsPathFullyQualified(DataDirectory))
            throw new BridgeConfigurationException("DataDirectory must be an absolute path.");
        if (!string.IsNullOrWhiteSpace(PairedClientsPath) && !Path.IsPathFullyQualified(PairedClientsPath))
            throw new BridgeConfigurationException("PairedClientsPath must be an absolute path.");
        if (!Pm3AutoDiscover && string.IsNullOrWhiteSpace(Pm3Port))
            throw new BridgeConfigurationException("Pm3Port is required when Pm3AutoDiscover is false.");
        return this;
    }

    public static BridgeOptions FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var bindUrl = configuration["Bridge:BindUrl"] ?? configuration["BRIDGE_BIND_URL"] ?? DefaultBindUrl;
        var dataDirectory = configuration["Bridge:DataDirectory"]
            ?? configuration["BRIDGE_DATA_DIRECTORY"]
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ElevatorTokens", "RidesBridge");
        var pairedPath = configuration["Bridge:PairedClientsPath"] ?? configuration["BRIDGE_PAIRED_CLIENTS_PATH"];
        var port = configuration["Pm3:Port"] ?? configuration["PM3_PORT"];
        var auto = configuration["Pm3:AutoDiscover"] ?? configuration["PM3_AUTO_DISCOVER"];
        var lifetime = configuration["Bridge:PairingLifetimeSeconds"] ?? configuration["BRIDGE_PAIRING_LIFETIME_SECONDS"];
        var operationWait = configuration["Bridge:OperationWaitTimeoutSeconds"] ?? configuration["BRIDGE_OPERATION_WAIT_TIMEOUT_SECONDS"];

        if (!bool.TryParse(auto, out var autoDiscover))
            autoDiscover = true;
        if (!double.TryParse(lifetime, out var seconds))
            seconds = 120;
        if (!double.TryParse(operationWait, out var operationWaitSeconds))
            operationWaitSeconds = 30;

        return new BridgeOptions
        {
            BindUrl = bindUrl,
            DataDirectory = dataDirectory,
            PairedClientsPath = pairedPath,
            Pm3Port = port,
            Pm3AutoDiscover = autoDiscover,
            Pm3ClientPath = configuration["Pm3:ClientPath"] ?? configuration["PM3_CLIENT_PATH"],
            PairingLifetime = TimeSpan.FromSeconds(seconds),
            OperationWaitTimeout = TimeSpan.FromSeconds(operationWaitSeconds),
        };
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
        .Where(n => n.OperationalStatus == OperationalStatus.Up)
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
}
