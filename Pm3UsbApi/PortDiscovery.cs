using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace Pm3UsbApi;

/// <summary>
/// Discovers Proxmark3 ports. On Windows uses the same WMI/PowerShell logic as the pm3 script
/// (VID_9AC4&PID_4B8F, VID_2D2D&PID_504D). On Linux/macOS runs the pm3 --list script.
/// </summary>
public static class PortDiscovery
{
    private static readonly Regex ListLineRegex = new(@"^\s*\d+\s*:\s*(.+)$", RegexOptions.Compiled);

    /// <summary>
    /// Returns discovered Proxmark3 ports. Returns empty if none found.
    /// </summary>
    /// <param name="pm3ClientPath">Path to proxmark3.exe; used to locate pm3 script on Unix.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of port strings (e.g. COM3, /dev/ttyACM0).</returns>
    public static async Task<IReadOnlyList<string>> ListPortsAsync(string? pm3ClientPath, CancellationToken ct = default)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return await ListPortsWindowsAsync(ct).ConfigureAwait(false);

        return await ListPortsViaPm3ScriptAsync(pm3ClientPath, ct).ConfigureAwait(false);
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
            var clientDir = Path.GetDirectoryName(Path.GetFullPath(pm3ClientPath.Trim()));
            if (!string.IsNullOrEmpty(clientDir))
            {
                var parentDir = Path.GetDirectoryName(clientDir);
                if (!string.IsNullOrEmpty(parentDir))
                {
                    var candidate = Path.Combine(parentDir, "pm3");
                    if (File.Exists(candidate))
                        scriptPath = Path.GetFullPath(candidate);
                }
                if (scriptPath is null)
                {
                    var candidate = Path.Combine(clientDir, "pm3");
                    if (File.Exists(candidate))
                        scriptPath = Path.GetFullPath(candidate);
                }
            }
        }

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
        var startInfo = new ProcessStartInfo
        {
            FileName = shell,
            ArgumentList = { scriptPath, "--list" },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var workDir = Path.GetDirectoryName(scriptPath);
        if (!string.IsNullOrEmpty(workDir) && Directory.Exists(workDir))
            startInfo.WorkingDirectory = workDir;

        using var process = Process.Start(startInfo);
        if (process is null)
            throw new InvalidOperationException("Failed to start pm3 process.");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(10));

        var output = await process.StandardOutput.ReadToEndAsync(cts.Token).ConfigureAwait(false);
        await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);

        return output;
    }

    private static IReadOnlyList<string> ParseListOutput(string output)
    {
        var ports = new List<string>();
        foreach (var line in output.Split(new[] {'\r', '\n'}, StringSplitOptions.RemoveEmptyEntries))
        {
            var match = ListLineRegex.Match(line);
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
