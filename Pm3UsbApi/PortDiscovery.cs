using System.Diagnostics;
using System.IO.Ports;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace Pm3UsbApi;

/// <summary>
/// Discovers Proxmark3 ports. On Windows uses WMI/PowerShell VID/PID matching.
/// On Unix prefers the pm3 --list script when installed, otherwise uses native USB serial
/// enumeration (ioreg on macOS, sysfs on Linux) with a SerialPort name fallback.
/// </summary>
public static class PortDiscovery
{
    private static readonly Regex ListLineRegex = new(@"^\s*\d+\s*:\s*(.+)$", RegexOptions.Compiled);

    private static readonly string[] ExcludedUnixPortNames =
    [
        "debug-console",
        "Bluetooth-Incoming-Port",
    ];

    /// <summary>
    /// Returns discovered Proxmark3 ports. Returns empty if none found.
    /// </summary>
    /// <param name="pm3ClientPath">Path to proxmark3.exe; used to locate pm3 script on Unix.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of port strings (e.g. COM3, /dev/cu.usbmodem1201).</returns>
    public static async Task<IReadOnlyList<string>> ListPortsAsync(string? pm3ClientPath, CancellationToken ct = default)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return await ListPortsWindowsAsync(ct).ConfigureAwait(false);

        var scriptPorts = await ListPortsViaPm3ScriptAsync(pm3ClientPath, ct).ConfigureAwait(false);
        if (scriptPorts.Count > 0)
            return NormalizeUnixPorts(scriptPorts);

        var nativePorts = await ListPortsUnixNativeAsync(ct).ConfigureAwait(false);
        return NormalizeUnixPorts(nativePorts);
    }

    /// <summary>
    /// Returns the first discovered port, or null if none found.
    /// </summary>
    /// <param name="pm3ClientPath">Path to proxmark3.exe; used to locate pm3 script on Unix.</param>
    /// <param name="ct">Cancellation token.</param>
    public static async Task<string?> DiscoverFirstPortAsync(string? pm3ClientPath, CancellationToken ct = default)
    {
        var ports = await ListPortsAsync(pm3ClientPath, ct).ConfigureAwait(false);
        return ports.Count > 0 ? ports[0] : null;
    }

    /// <summary>
    /// Windows: Uses PowerShell/WMI to query Win32_serialport for Proxmark3 VID/PID
    /// (VID_9AC4&PID_4B8F, VID_2D2D&PID_504D) - same as pm3 script get_pm3_list_Windows.
    /// </summary>
    private static async Task<IReadOnlyList<string>> ListPortsWindowsAsync(CancellationToken ct)
    {
        var ps = FindPowerShell();
        if (ps is null) return [];

        var script = """
            Get-CimInstance -ClassName Win32_serialport | Where-Object {
                $_.PNPDeviceID -like '*VID_9AC4&PID_4B8F*' -or $_.PNPDeviceID -like '*VID_2D2D&PID_504D*'
            } | Select-Object -ExpandProperty DeviceID
            """;

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = ps,
                ArgumentList = { "-NoProfile", "-NonInteractive", "-Command", script },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process is null) return [];

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(10));

            var output = await process.StandardOutput.ReadToEndAsync(cts.Token).ConfigureAwait(false);
            await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);

            var ports = output
                .Split(new[] {'\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .ToList();
            return ports;
        }
        catch
        {
            return [];
        }
    }

    private static async Task<IReadOnlyList<string>> ListPortsUnixNativeAsync(CancellationToken ct)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var macPorts = await ListPortsMacOsNativeAsync(ct).ConfigureAwait(false);
            if (macPorts.Count > 0)
                return macPorts;
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            var linuxPorts = ListPortsLinuxNative();
            if (linuxPorts.Count > 0)
                return linuxPorts;
        }

        return ListPortsSerialPortFallback();
    }

    private static async Task<IReadOnlyList<string>> ListPortsMacOsNativeAsync(CancellationToken ct)
    {
        const string script = """
            ioreg -r -c "IOUSBHostDevice" -l | awk -F '"' '
            $2=="USB Vendor Name"{b=($4=="proxmark.org")}
            b==1 && $2=="IODialinDevice"{print $4}'
            """;

        try
        {
            var output = await RunShellCommandAsync("/bin/bash", ["-c", script], TimeSpan.FromSeconds(10), ct)
                .ConfigureAwait(false);
            return ParseLineOutput(output);
        }
        catch
        {
            return [];
        }
    }

    private static IReadOnlyList<string> ListPortsLinuxNative()
    {
        var ports = new List<string>();
        if (!Directory.Exists("/dev"))
            return ports;

        foreach (var entry in Directory.EnumerateFileSystemEntries("/dev"))
        {
            var name = Path.GetFileName(entry);
            if (name is null || (!name.StartsWith("ttyACM", StringComparison.Ordinal) && !name.StartsWith("ttyUSB", StringComparison.Ordinal)))
                continue;

            var devicePath = entry.StartsWith("/dev/", StringComparison.Ordinal) ? entry : Path.Combine("/dev", name);
            if (IsProxmarkLinuxDevice(devicePath))
                ports.Add(devicePath);
        }

        return ports;
    }

    private static bool IsProxmarkLinuxDevice(string devicePath)
    {
        var ttyName = Path.GetFileName(devicePath);
        if (string.IsNullOrEmpty(ttyName))
            return false;

        var manufacturerPath = Path.Combine("/sys/class/tty", ttyName, "device", "..", "..", "..", "manufacturer");
        try
        {
            manufacturerPath = Path.GetFullPath(manufacturerPath);
            if (!File.Exists(manufacturerPath))
                return false;

            var manufacturer = File.ReadAllText(manufacturerPath).Trim();
            return manufacturer.Contains("proxmark.org", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static IReadOnlyList<string> ListPortsSerialPortFallback()
    {
        return SerialPort.GetPortNames()
            .Select(NormalizePortPath)
            .Where(IsLikelyProxmarkSerialName)
            .Where(port => !IsExcludedUnixPort(port))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(port => port, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    internal static IReadOnlyList<string> NormalizeUnixPorts(IReadOnlyList<string> ports)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var port in ports)
        {
            if (string.IsNullOrWhiteSpace(port) || IsExcludedUnixPort(port))
                continue;

            var normalized = PreferCalloutDevice(NormalizePortPath(port.Trim()));
            var dedupeKey = ToUnixPortDedupeKey(normalized);
            if (seen.Add(dedupeKey))
                result.Add(normalized);
        }

        return result;
    }

    internal static string PreferCalloutDevice(string port)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return port;

        const string ttyPrefix = "/dev/tty.";
        if (!port.StartsWith(ttyPrefix, StringComparison.Ordinal))
            return port;

        var cuPort = "/dev/cu." + port[ttyPrefix.Length..];
        return File.Exists(cuPort) ? cuPort : port;
    }

    internal static bool IsLikelyProxmarkSerialName(string port)
    {
        var name = Path.GetFileName(port);
        if (string.IsNullOrEmpty(name))
            return false;

        return name.Contains("usbmodem", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("ttyACM", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("cu.usbmodem", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("ttyUSB", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsExcludedUnixPort(string port)
    {
        var name = Path.GetFileName(port);
        if (string.IsNullOrEmpty(name))
            return false;

        foreach (var excluded in ExcludedUnixPortNames)
        {
            if (name.Equals(excluded, StringComparison.OrdinalIgnoreCase)
                || name.EndsWith("." + excluded, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static string NormalizePortPath(string port)
    {
        if (!port.StartsWith('/'))
            return port;

        return port.StartsWith("/dev/", StringComparison.Ordinal) ? port : "/dev/" + port.TrimStart('/');
    }

    private static string ToUnixPortDedupeKey(string port)
    {
        var name = Path.GetFileName(port) ?? port;
        if (name.StartsWith("cu.", StringComparison.Ordinal))
            return "tty." + name["cu.".Length..];
        return name;
    }

    private static string? FindPowerShell()
    {
        var psPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe");
        if (File.Exists(psPath))
            return psPath;
        return FindInPath("powershell.exe") ?? FindInPath("pwsh.exe");
    }

    private static async Task<IReadOnlyList<string>> ListPortsViaPm3ScriptAsync(string? pm3ClientPath, CancellationToken ct)
    {
        var (scriptPath, shell) = ResolvePm3ScriptAndShell(pm3ClientPath);
        if (scriptPath is null || shell is null)
            return [];

        try
        {
            var output = await RunPm3ListAsync(scriptPath, shell, ct).ConfigureAwait(false);
            return ParseListOutput(output);
        }
        catch
        {
            return [];
        }
    }

    private static (string? scriptPath, string? shell) ResolvePm3ScriptAndShell(string? pm3ClientPath)
    {
        string? scriptPath = null;

        if (!string.IsNullOrWhiteSpace(pm3ClientPath))
        {
            var trimmed = pm3ClientPath.Trim();
            scriptPath = ResolvePm3ScriptFromClientPath(trimmed);

            if (scriptPath is null)
            {
                var resolvedClientPath = FindInPath(trimmed);
                if (resolvedClientPath is not null)
                    scriptPath = ResolvePm3ScriptFromClientPath(resolvedClientPath);
            }
        }

        if (scriptPath is null)
            scriptPath = FindInPath("pm3");

        if (scriptPath is null)
        {
            foreach (var baseDir in GetCommonPm3Locations())
            {
                var candidate = Path.Combine(baseDir, "pm3");
                if (File.Exists(candidate))
                {
                    scriptPath = Path.GetFullPath(candidate);
                    break;
                }
            }
        }

        return (scriptPath, scriptPath is not null ? "/bin/bash" : null);
    }

    private static string? ResolvePm3ScriptFromClientPath(string clientPath)
    {
        if (string.IsNullOrWhiteSpace(clientPath))
            return null;

        if (File.Exists(clientPath) && Path.GetFileName(clientPath).Equals("pm3", StringComparison.OrdinalIgnoreCase))
            return Path.GetFullPath(clientPath);

        var clientDir = Path.GetDirectoryName(Path.GetFullPath(clientPath.Trim()));
        if (string.IsNullOrEmpty(clientDir))
            return null;

        var parentDir = Path.GetDirectoryName(clientDir);
        if (!string.IsNullOrEmpty(parentDir))
        {
            var candidate = Path.Combine(parentDir, "pm3");
            if (File.Exists(candidate))
                return Path.GetFullPath(candidate);
        }

        var siblingCandidate = Path.Combine(clientDir, "pm3");
        if (File.Exists(siblingCandidate))
            return Path.GetFullPath(siblingCandidate);

        return null;
    }

    private static IEnumerable<string> GetCommonPm3Locations()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(home))
            yield return Path.Combine(home, "proxmark3");
        yield return "/usr/local/share/proxmark3";
    }

    private static string? FindInPath(string fileName)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathEnv)) return null;

        foreach (var dir in pathEnv.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            var candidate = Path.Combine(dir.Trim(), fileName);
            if (File.Exists(candidate))
                return Path.GetFullPath(candidate);
        }
        return null;
    }

    private static async Task<string> RunPm3ListAsync(string scriptPath, string shell, CancellationToken ct)
    {
        return await RunShellCommandAsync(shell, [scriptPath, "--list"], TimeSpan.FromSeconds(10), ct)
            .ConfigureAwait(false);
    }

    private static async Task<string> RunShellCommandAsync(
        string shell,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken ct)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = shell,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        var workDir = arguments.Count > 0 ? Path.GetDirectoryName(arguments[0]) : null;
        if (!string.IsNullOrEmpty(workDir) && Directory.Exists(workDir))
            startInfo.WorkingDirectory = workDir;

        using var process = Process.Start(startInfo);
        if (process is null)
            throw new InvalidOperationException($"Failed to start shell command: {shell}");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        var output = await process.StandardOutput.ReadToEndAsync(cts.Token).ConfigureAwait(false);
        await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);

        return output;
    }

    private static IReadOnlyList<string> ParseListOutput(string output) =>
        ParseLineOutput(output, ListLineRegex);

    internal static IReadOnlyList<string> ParseLineOutput(string output, Regex? lineRegex = null)
    {
        var ports = new List<string>();
        foreach (var line in output.Split(new[] {'\r', '\n'}, StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed))
                continue;

            if (lineRegex is null)
            {
                ports.Add(trimmed);
                continue;
            }

            var match = lineRegex.Match(trimmed);
            if (match.Success)
            {
                var port = match.Groups[1].Value.Trim();
                if (!string.IsNullOrEmpty(port))
                    ports.Add(port);
            }
        }

        return ports;
    }
}
