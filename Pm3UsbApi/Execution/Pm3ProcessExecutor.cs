using System.Diagnostics;
using System.Runtime.InteropServices;
using Pm3UsbApi.Parsers;

namespace Pm3UsbApi.Execution;

/// <summary>
/// Stage A: Executes Proxmark3 commands by launching the pm3 client process with -c flag.
/// Requires the proxmark3 client (e.g., from ProxSpace) to be installed on the host.
/// </summary>
/// <remarks>
/// <c>lf tune</c> has special handling: it runs continuously, so the executor interrupts it after
/// <see cref="LfTuneCaptureInterval"/> and returns whatever output was captured (e.g. peak mV).
/// <c>lf tune</c> cannot be combined with other commands in the same invocation.
/// </remarks>
/// <remarks>
/// Windows (ProxSpace): The proxmark3.exe in client/ requires MinGW64 DLLs (libjansson, Qt5, etc.)
/// on PATH. When the exe is under a ProxSpace layout, we derive msys2/mingw64/bin and prepend it to
/// PATH before launching. Point Pm3ClientPath directly to proxmark3.exe.
/// </remarks>
public sealed class Pm3ProcessExecutor : IPm3CommandExecutor
{
    /// <summary>
    /// Duration to run lf tune before interrupting. Enough time to capture peak mV output.
    /// </summary>
    public static readonly TimeSpan LfTuneCaptureInterval = TimeSpan.FromSeconds(3);

    private readonly Pm3Options _options;
    private string? _resolvedPm3Path;
    private Process? _currentProcess;
    private bool _disposed;

    public Pm3ProcessExecutor(Pm3Options options)
    {
        _options = options ?? new Pm3Options();
    }

    /// <inheritdoc />
    public async Task<CommandResult> ExecuteAsync(
        string[] commands,
        TimeSpan? timeout = null,
        CancellationToken ct = default,
        string? portOverride = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (commands is null || commands.Length == 0)
            throw new ArgumentException("At least one command is required.", nameof(commands));

        if (ContainsLfTuneWithOthers(commands))
            throw new InvalidOperationException("lf tune cannot be combined with other commands. Execute it alone.");

        var isLfTuneOnly = IsLfTuneOnly(commands);
        var effectiveTimeout = isLfTuneOnly ? LfTuneCaptureInterval : (timeout ?? _options.DefaultCommandTimeout);
        var commandString = string.Join("; ", commands);
        var path = ResolvePm3ClientPath();
        var args = BuildArguments(commandString, portOverride);

        string? workingDir = null;
        if (!string.IsNullOrWhiteSpace(_options.WorkingDirectory))
        {
            workingDir = Path.IsPathRooted(_options.WorkingDirectory)
                ? _options.WorkingDirectory.Trim()
                : Path.Combine(Directory.GetCurrentDirectory(), _options.WorkingDirectory.Trim());
            Directory.CreateDirectory(workingDir);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = path,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        if (workingDir is not null)
            startInfo.WorkingDirectory = workingDir;
        EnsureProxSpacePath(startInfo, path);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(effectiveTimeout);

        var outputLines = new List<string>();
        try
        {
            _currentProcess = new Process { StartInfo = startInfo };
            _currentProcess.Start();

            var stdoutTask = ReadStreamAsync(_currentProcess.StandardOutput, outputLines, cts.Token);
            var stderrTask = ReadStreamAsync(_currentProcess.StandardError, outputLines, cts.Token);
            await Task.WhenAll(stdoutTask, stderrTask);

            var exitTask = _currentProcess.WaitForExitAsync(cts.Token);
            try
            {
                await exitTask;
            }
            catch (OperationCanceledException) when (cts.Token.IsCancellationRequested)
            {
                try { _currentProcess.Kill(entireProcessTree: true); } catch { /* best effort */ }
                var interruptedLines = outputLines.Select(OutputParser.StripAnsi).ToList();
                var (interruptedHasErrors, interruptedErrorSummary) = OutputParser.DetectErrors(interruptedLines);
                var interruptedResult = new CommandResult
                {
                    Commands = commands,
                    OutputLines = interruptedLines,
                    ExitCode = -1,
                    HasErrors = interruptedHasErrors,
                    ErrorSummary = interruptedHasErrors ? interruptedErrorSummary : null
                };
                if (isLfTuneOnly)
                    return interruptedResult;
                throw new Pm3TimeoutException("Command execution timed out.", interruptedResult);
            }

            var exitCode = _currentProcess.ExitCode;
            var strippedLines = outputLines.Select(OutputParser.StripAnsi).ToList();
            var (hasErrors, errorSummary) = OutputParser.DetectErrors(strippedLines);

            return new CommandResult
            {
                Commands = commands,
                OutputLines = strippedLines,
                ExitCode = exitCode,
                HasErrors = hasErrors || exitCode != 0,
                ErrorSummary = hasErrors ? errorSummary : (exitCode != 0 ? $"Exit code {exitCode}" : null)
            };
        }
        finally
        {
            _currentProcess?.Dispose();
            _currentProcess = null;
        }
    }

    /// <inheritdoc />
    public Task CancelCurrentAsync(CancellationToken ct = default)
    {
        if (_currentProcess is { } p && !p.HasExited)
        {
            try { p.Kill(entireProcessTree: true); } catch { /* best effort */ }
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        CancelCurrentAsync().GetAwaiter().GetResult();
        _disposed = true;
        return ValueTask.CompletedTask;
    }

    private string ResolvePm3ClientPath()
    {
        if (_resolvedPm3Path is not null)
            return _resolvedPm3Path;

        if (!string.IsNullOrWhiteSpace(_options.Pm3ClientPath))
        {
            var p = _options.Pm3ClientPath.Trim();
            if (File.Exists(p))
            {
                _resolvedPm3Path = Path.GetFullPath(p);
                return _resolvedPm3Path;
            }
            if (File.Exists(p + ".exe"))
            {
                _resolvedPm3Path = Path.GetFullPath(p + ".exe");
                return _resolvedPm3Path;
            }
        }

        var searchNames = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? new[] { "proxmark3.exe", "pm3.exe" }
            : new[] { "proxmark3", "pm3" };

        foreach (var name in searchNames)
        {
            var fromPath = FindInPath(name);
            if (fromPath is not null)
            {
                _resolvedPm3Path = fromPath;
                return _resolvedPm3Path;
            }
        }

        foreach (var baseDir in GetCommonSearchDirectories())
        {
            foreach (var name in searchNames)
            {
                var candidate = Path.Combine(baseDir, name);
                if (File.Exists(candidate))
                {
                    _resolvedPm3Path = Path.GetFullPath(candidate);
                    return _resolvedPm3Path;
                }
            }
        }

        throw new Pm3ClientNotFoundException(
            "Proxmark3 client not found. Set Pm3Options.Pm3ClientPath to the exe path (e.g. .../ProxSpace/pm3/proxmark3/client/proxmark3.exe), " +
            "or ensure proxmark3 is on PATH.");
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

    private static IEnumerable<string> GetCommonSearchDirectories()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            yield return Path.Combine(@"C:\ProxSpace", "pm3", "proxmark3", "client");
            yield return Path.Combine(@"C:\ProxSpace", "pm3", "proxmark3");
            var user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(user))
            {
                yield return Path.Combine(user, "ProxSpace", "pm3", "proxmark3", "client");
                yield return Path.Combine(user, "ProxSpace", "pm3", "proxmark3");
            }
        }
        else
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(home))
            {
                yield return Path.Combine(home, ".local", "bin");
                yield return Path.Combine(home, "proxmark3");
            }
            yield return "/usr/bin";
            yield return "/usr/local/bin";
        }
    }

    private string BuildArguments(string commandString, string? portOverride = null)
    {
        var parts = new List<string>();
        var port = portOverride ?? _options.DevicePort;
        if (!string.IsNullOrWhiteSpace(port))
            parts.Add(port.Trim());
        parts.Add("-c");
        parts.Add($"\"{commandString.Replace("\"", "\\\"")}\"");
        return string.Join(" ", parts);
    }

    /// <summary>
    /// On Windows, when exe is under a ProxSpace layout, prepend msys2/mingw64/bin to PATH so
    /// proxmark3.exe can load MinGW64 DLLs (libjansson, Qt5, etc.). No-op on Linux/macOS.
    /// </summary>
    private static void EnsureProxSpacePath(ProcessStartInfo startInfo, string exePath)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        var proxSpaceRoot = FindProxSpaceRoot(exePath);
        if (proxSpaceRoot is null) return;

        var mingw64Bin = Path.Combine(proxSpaceRoot, "msys2", "mingw64", "bin");
        if (!Directory.Exists(mingw64Bin)) return;

        var env = startInfo.Environment;
        var existingPath = env.TryGetValue("PATH", out var v) ? v : Environment.GetEnvironmentVariable("PATH") ?? "";
        env["PATH"] = mingw64Bin + Path.PathSeparator + existingPath;
    }

    /// <summary>
    /// Walk up from exe directory to find a parent containing "msys2" (ProxSpace root).
    /// </summary>
    private static string? FindProxSpaceRoot(string exePath)
    {
        var dir = Path.GetDirectoryName(exePath);
        for (var i = 0; i < 10 && dir is not null; i++)
        {
            var msys2 = Path.Combine(dir, "msys2");
            if (Directory.Exists(msys2))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    private static bool IsLfTuneCommand(string command) =>
        command.AsSpan().TrimStart().StartsWith("lf tune", StringComparison.OrdinalIgnoreCase);

    private static bool IsLfTuneOnly(string[] commands) =>
        commands.Length == 1 && IsLfTuneCommand(commands[0]);

    private static bool ContainsLfTuneWithOthers(string[] commands) =>
        commands.Length > 1 && commands.Any(IsLfTuneCommand);

    private static async Task ReadStreamAsync(StreamReader reader, List<string> outputLines, CancellationToken ct)
    {
        try
        {
            while (true)
            {
                var line = await reader.ReadLineAsync(ct);
                if (line is null) break;
                lock (outputLines) outputLines.Add(line);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on timeout
        }
    }
}
