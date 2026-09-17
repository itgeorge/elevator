using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Configuration;

namespace RidesBridge;

/// <summary>Strict process-level launch parsing for the optional everyday mode.</summary>
public sealed record BridgeLaunchOptions(
    bool Everyday,
    bool ShowHelp,
    IReadOnlyList<string> AspNetCoreArguments)
{
    public static BridgeLaunchOptions Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var everydayCount = args.Count(argument => string.Equals(argument, "--everyday", StringComparison.Ordinal));
        var malformedEveryday = args.FirstOrDefault(argument =>
            argument.StartsWith("--everyday", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(argument, "--everyday", StringComparison.Ordinal));
        if (malformedEveryday is not null)
            throw new BridgeLaunchConfigurationException(
                $"Unknown or malformed launch option '{malformedEveryday}'. Use the exact flag '--everyday'.");

        if (everydayCount > 1)
            throw new BridgeLaunchConfigurationException("The '--everyday' launch flag may be specified only once.");
        if (everydayCount == 1 && args.Count != 1)
            throw new BridgeLaunchConfigurationException(
                "The '--everyday' launch mode accepts only the single launch flag and no additional options.");

        var helpCount = args.Count(argument => string.Equals(argument, "--help", StringComparison.Ordinal));
        if (helpCount > 0)
        {
            if (helpCount != 1 || args.Count != 1)
                throw new BridgeLaunchConfigurationException("The '--help' launch option must be used by itself.");
            return new BridgeLaunchOptions(false, true, []);
        }

        return everydayCount == 1
            ? new BridgeLaunchOptions(true, false, [])
            : new BridgeLaunchOptions(false, false, args.ToArray());
    }
}

public sealed class BridgeLaunchConfigurationException : Exception
{
    public BridgeLaunchConfigurationException(string message) : base(message) { }
}

/// <summary>Bounded IPv4 TCP bindability check used before Kestrel starts.</summary>
public interface IPortAvailabilityProbe
{
    bool CanBind(int port);
}

public sealed class TcpPortAvailabilityProbe : IPortAvailabilityProbe
{
    public bool CanBind(int port)
    {
        if (port is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(port));

        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            socket.Bind(new IPEndPoint(IPAddress.Any, port));
            socket.Listen(1);
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }
}

/// <summary>
/// Selects the first bindable port in the documented everyday range. The socket used for
/// each check is closed before returning, so this is deliberately a check-before-Kestrel
/// startup rather than a reservation.
/// </summary>
public static class EverydayPortSelector
{
    public const int DefaultFirstPort = 5080;
    public const int DefaultLastPort = 5179;

    public static int Select(
        IPortAvailabilityProbe probe,
        int firstPort = DefaultFirstPort,
        int lastPort = DefaultLastPort)
    {
        ArgumentNullException.ThrowIfNull(probe);
        if (firstPort is < 1 or > 65535 || lastPort is < 1 or > 65535 || firstPort > lastPort)
            throw new PortSelectionException(
                $"Everyday port range must be within 1..65535 and ordered; received {firstPort}..{lastPort}.");

        // The explicit last-port break avoids overflowing an int when the bounded range ends
        // at 65535.
        for (var port = firstPort; ; port++)
        {
            if (probe.CanBind(port))
                return port;
            if (port == lastPort)
                break;
        }

        throw new PortSelectionException(
            $"No bindable IPv4 TCP port was found in the everyday range {firstPort}..{lastPort}.");
    }
}

public sealed class PortSelectionException : Exception
{
    public PortSelectionException(string message) : base(message) { }
}

public static class EverydayLaunchMode
{
    public static BridgeOptions CreateOptions(IConfiguration configuration, int selectedPort)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (selectedPort is < 1 or > 65535)
            throw new PortSelectionException($"Selected everyday port {selectedPort} is outside 1..65535.");

        // Everyday mode owns exposure and PM3 discovery. FromConfiguration's everyday path
        // intentionally does not parse conflicting bind/port/auto-discovery values, while all
        // durable path settings retain their normal precedence.
        var configured = BridgeOptions.FromConfiguration(configuration, everydayMode: true);
        return configured with
        {
            BindUrl = $"http://0.0.0.0:{selectedPort}",
            Pm3AutoDiscover = true,
            Pm3Port = null,
        };
    }
}
