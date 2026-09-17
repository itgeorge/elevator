using System.Collections.ObjectModel;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Haukcode.Mdns;

namespace RidesBridge;

/// <summary>One client-usable Bonjour service instance and its public TXT contract.</summary>
public sealed record BonjourServiceDescriptor
{
    public const string ServiceTypeName = "_elevator-rides._tcp";
    public const string TypeTxtKey = "type";
    public const string BridgeIdTxtKey = "bridgeId";
    public const string ApiVersionTxtKey = "apiVersion";
    public const string UrlTxtKey = "url";
    public const string TypeTxtValue = "elevator-rides";

    public BonjourServiceDescriptor(string instanceName, Uri httpUrl, string bridgeId)
    {
        if (string.IsNullOrWhiteSpace(instanceName) || instanceName.Length > 63
            || instanceName.Any(c => c > 0x7f || !(char.IsLetterOrDigit(c) || c == '-')))
            throw new BridgeConfigurationException("Bonjour instance name must be an ASCII DNS label of no more than 63 characters.");
        if (!PairingPayload.IsBridgeId(bridgeId))
            throw new BridgeConfigurationException("Bonjour bridge identifier is invalid.");
        if (httpUrl is null)
            throw new ArgumentNullException(nameof(httpUrl));

        var canonicalUrl = CanonicalPrivateHttpUrl(httpUrl);
        InstanceName = instanceName;
        BridgeId = bridgeId.ToUpperInvariant();
        HttpUrl = canonicalUrl;
        Address = IPAddress.Parse(canonicalUrl.Host);
        Port = canonicalUrl.Port;
        Txt = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [TypeTxtKey] = TypeTxtValue,
            [BridgeIdTxtKey] = BridgeId,
            [ApiVersionTxtKey] = BridgeOptions.ApiVersion,
            [UrlTxtKey] = canonicalUrl.AbsoluteUri,
        });
    }

    public string InstanceName { get; }
    public string ServiceType => ServiceTypeName;
    public string BridgeId { get; }
    public Uri HttpUrl { get; }
    public IPAddress Address { get; }
    public int Port { get; }
    public IReadOnlyDictionary<string, string> Txt { get; }

    internal static Uri CanonicalPrivateHttpUrl(Uri url)
    {
        if (!url.IsAbsoluteUri || !string.Equals(url.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            || !HasExplicitPort(url) || url.UserInfo.Length != 0
            || !string.IsNullOrEmpty(url.Query) || !string.IsNullOrEmpty(url.Fragment)
            || url.AbsolutePath != "/" || !IPAddress.TryParse(url.Host, out var address)
            || address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork
            || !BridgeOptions.IsPrivateIpv4(address) || url.Port is < 1 or > 65535)
            throw new BridgeConfigurationException("Bonjour URL must be a canonical explicit-port private IPv4 HTTP URL.");

        return new Uri($"http://{address}:{url.Port}/", UriKind.Absolute);
    }

    internal static bool HasExplicitPort(Uri url)
    {
        var original = url.OriginalString;
        var schemeSeparator = original.IndexOf("://", StringComparison.Ordinal);
        if (schemeSeparator < 0) return false;
        var authorityStart = schemeSeparator + 3;
        var authorityEnd = original.IndexOfAny(['/','?','#'], authorityStart);
        if (authorityEnd < 0) authorityEnd = original.Length;
        var authority = original[authorityStart..authorityEnd];
        var colon = authority.LastIndexOf(':');
        return colon > 0 && colon < authority.Length - 1
            && authority[(colon + 1)..].All(c => c is >= '0' and <= '9');
    }
}

public static class BonjourAdvertisementFactory
{
    public static IReadOnlyList<BonjourServiceDescriptor> Create(
        BridgeOptions options,
        string bridgeId,
        IEnumerable<IPAddress>? activeAddresses = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        if (!PairingPayload.IsBridgeId(bridgeId))
            throw new BridgeConfigurationException("Bonjour bridge identifier is invalid.");

        var urls = activeAddresses is null
            ? options.GetReportedUrls()
            : options.GetReportedUrls(activeAddresses);
        return urls
            .Select(url => new BonjourServiceDescriptor(CreateInstanceName(bridgeId, url), url, bridgeId))
            .ToList();
    }

    private static string CreateInstanceName(string bridgeId, Uri url)
    {
        // The persistent 128-bit value is only a public bridge identifier, not an
        // authenticator. The URL digest makes instances distinct and stable when wildcard
        // binding exposes several addresses.
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url.AbsoluteUri)))
            .ToLowerInvariant()[..12];
        return $"elevator-rides-{bridgeId.ToLowerInvariant()}-{digest}";
    }
}

public interface IBonjourPublisher : IAsyncDisposable
{
    Task StartAsync(IReadOnlyList<BonjourServiceDescriptor> services, CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}

/// <summary>Managed mDNS/DNS-SD publisher. Network use is isolated behind IBonjourPublisher.</summary>
/// <remarks>
/// Haukcode.Mdns' explicit local-address argument selects the A record(s) in the
/// advertisement; it does not select the multicast transmit interface. A wildcard
/// configuration therefore intentionally creates address-specific service instances,
/// but each instance is sent on every interface that Haukcode considers multicast-capable.
/// Interface-scoped advertisements require a publisher with an interface-aware transport.
/// </remarks>
public sealed class HaukcodeBonjourPublisher : IBonjourPublisher
{
    private readonly object _sync = new();
    private List<MdnsAdvertiser> _advertisers = [];
    private bool _started;
    private bool _disposed;

    public Task StartAsync(IReadOnlyList<BonjourServiceDescriptor> services, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_started) return Task.CompletedTask;
            cancellationToken.ThrowIfCancellationRequested();
            _started = true;
            if (services.Count == 0) return Task.CompletedTask;

            try
            {
                foreach (var descriptor in services)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var profile = new ServiceProfile(
                        descriptor.InstanceName,
                        BonjourServiceDescriptor.ServiceTypeName,
                        (ushort)descriptor.Port,
                        descriptor.Txt);
                    // MdnsAdvertiser(address) constrains the A record, not the links used by
                    // MulticastTransport. See the class remarks; do not treat this as a bind.
                    var advertiser = new MdnsAdvertiser(profile, descriptor.Address);
                    _advertisers.Add(advertiser);
                    advertiser.Start();
                }
            }
            catch (Exception startFailure)
            {
                Exception? cleanupFailure = null;
                try { DisposeAdvertisers(_advertisers).GetAwaiter().GetResult(); }
                catch (Exception ex) { cleanupFailure = ex; }
                _advertisers = [];
                _started = false;
                if (cleanupFailure is not null)
                    throw new AggregateException("Bonjour advertisement startup and cleanup failed.", startFailure, cleanupFailure);
                throw;
            }
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        // Once shutdown begins, goodbye packets and socket disposal are not cancellable.
        lock (_sync)
        {
            var advertisers = TakeAdvertisers();
            return DisposeAdvertisers(advertisers);
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_sync)
        {
            _disposed = true;
            var advertisers = TakeAdvertisers();
            return new ValueTask(DisposeAdvertisers(advertisers));
        }
    }

    private List<MdnsAdvertiser> TakeAdvertisers()
    {
        var advertisers = _advertisers;
        _advertisers = [];
        _started = false;
        return advertisers;
    }

    private static async Task DisposeAdvertisers(IReadOnlyList<MdnsAdvertiser> advertisers)
    {
        // Haukcode.Mdns 1.0.18's async disposer can time out before releasing its sockets:
        // MdnsAdvertiser marks itself disposed before its timer can complete the second goodbye,
        // then WaitAsync throws and ReleaseResources is never reached. Its synchronous disposer
        // always releases the transport after the bounded goodbye wait, so use it off-thread and
        // await all advertisers without serially blocking the lifecycle thread.
        var cleanupTasks = advertisers.Select(advertiser => Task.Run(advertiser.Dispose)).ToArray();
        try
        {
            await Task.WhenAll(cleanupTasks).ConfigureAwait(false);
        }
        catch
        {
            var failures = cleanupTasks
                .Where(task => task.IsFaulted)
                .SelectMany(task => task.Exception!.InnerExceptions)
                .ToList();
            if (failures.Count == 1) throw failures[0];
            if (failures.Count > 1) throw new AggregateException("Bonjour publisher cleanup failed.", failures);
            throw;
        }
    }
}

internal sealed class NoopBonjourPublisher : IBonjourPublisher
{
    public Task StartAsync(IReadOnlyList<BonjourServiceDescriptor> services, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
