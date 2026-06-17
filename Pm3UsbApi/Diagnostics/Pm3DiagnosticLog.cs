namespace Pm3UsbApi.Diagnostics;

/// <summary>
/// Always-on PM3 diagnostic logs under the system temp directory.
/// Distinct from opt-in <see cref="Pm3Options.EnableTranscriptLogging"/>.
/// </summary>
public sealed class Pm3DiagnosticLog : IDisposable
{
    private static readonly object StaticLock = new();
    private static Pm3DiagnosticLog? _current;
    private static int _handlersInstalled;

    private readonly object _writeLock = new();
    private StreamWriter? _sessionWriter;
    private StreamWriter? _errorsWriter;
    private StreamWriter? _nativeWriter;
    private bool _disposed;

    private Pm3DiagnosticLog(string baseDirectory, string sessionLogPath, string errorsLogPath, string? nativeTraceLogPath, bool nativeTraceEnabled)
    {
        BaseDirectory = baseDirectory;
        SessionLogPath = sessionLogPath;
        ErrorsLogPath = errorsLogPath;
        NativeTraceLogPath = nativeTraceLogPath;
        NativeTraceEnabled = nativeTraceEnabled;
    }

    public string BaseDirectory { get; }
    public string SessionLogPath { get; }
    public string ErrorsLogPath { get; }
    public string? NativeTraceLogPath { get; }
    public bool NativeTraceEnabled { get; }

    public static Pm3DiagnosticLog Current
    {
        get
        {
            lock (StaticLock)
            {
                return _current ??= CreateNew();
            }
        }
    }

    /// <summary>
    /// Ensures diagnostic logs exist and process-level exception hooks are installed.
    /// </summary>
    public static Pm3DiagnosticLog EnsureInitialized() => Current;

    public static Pm3DiagnosticLog CreateNew(string? baseDirectoryOverride = null)
    {
        var baseDir = baseDirectoryOverride ?? ResolveBaseDirectory();
        Directory.CreateDirectory(baseDir);

        var stamp = $"{Environment.ProcessId}-{DateTime.UtcNow:yyyyMMddHHmmss}";
        var sessionPath = Path.Combine(baseDir, $"pm3-{stamp}-session.log");
        var errorsPath = Path.Combine(baseDir, $"pm3-{stamp}-errors.log");
        var nativeTraceEnabled = IsNativeTraceEnabled();
        string? nativePath = nativeTraceEnabled
            ? Path.Combine(baseDir, $"pm3-{stamp}-native.log")
            : null;

        var log = new Pm3DiagnosticLog(baseDir, sessionPath, errorsPath, nativePath, nativeTraceEnabled);
        log.WriteSession($"PM3 log directory: {baseDir}");
        log.WriteSession($"session log: {sessionPath}");
        log.WriteSession($"errors log: {errorsPath}");
        if (nativePath is not null)
            log.WriteSession($"native trace log: {nativePath}");

        InstallProcessHandlersOnce();
        return log;
    }

    internal static void ReplaceCurrentForTesting(Pm3DiagnosticLog log)
    {
        lock (StaticLock)
        {
            _current?.Dispose();
            _current = log;
        }
    }

    internal static void ResetForTesting()
    {
        lock (StaticLock)
        {
            _current?.Dispose();
            _current = null;
            _handlersInstalled = 0;
        }
    }

    public void WriteSession(string message)
    {
        if (_disposed || string.IsNullOrWhiteSpace(message))
            return;

        AppendLine(SessionLogPath, ref _sessionWriter, "SESSION", message);
    }

    public void WriteError(string message, Exception? exception = null)
    {
        if (_disposed)
            return;

        var text = exception is null ? message : $"{message}{Environment.NewLine}{FormatException(exception)}";
        AppendLine(ErrorsLogPath, ref _errorsWriter, "ERROR", text);
        WriteSession($"ERROR: {message}");
    }

    public void WriteNativeTrace(string message)
    {
        if (_disposed || !NativeTraceEnabled || NativeTraceLogPath is null || string.IsNullOrWhiteSpace(message))
            return;

        AppendLine(NativeTraceLogPath, ref _nativeWriter, "NATIVE", message);
    }

    public static void LogFatal(Exception exception, string? source = null)
    {
        try
        {
            var prefix = string.IsNullOrWhiteSpace(source) ? "fatal unhandled exception" : $"fatal unhandled exception in {source}";
            Current.WriteError(prefix, exception);
        }
        catch
        {
            // Never throw from logging.
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        lock (_writeLock)
        {
            _sessionWriter?.Dispose();
            _errorsWriter?.Dispose();
            _nativeWriter?.Dispose();
            _sessionWriter = null;
            _errorsWriter = null;
            _nativeWriter = null;
        }
    }

    private static string ResolveBaseDirectory()
    {
        var overrideDir = Environment.GetEnvironmentVariable("PM3_LOG_DIR");
        if (!string.IsNullOrWhiteSpace(overrideDir))
            return Path.GetFullPath(overrideDir.Trim());

        return Path.Combine(Path.GetTempPath(), "elevator");
    }

    private static bool IsNativeTraceEnabled()
    {
        var value = Environment.GetEnvironmentVariable("PM3_NATIVE_TRACE");
        return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "on", StringComparison.OrdinalIgnoreCase);
    }

    private static void InstallProcessHandlersOnce()
    {
        if (Interlocked.Exchange(ref _handlersInstalled, 1) == 1)
            return;

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                LogFatal(ex, "AppDomain");
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            LogFatal(args.Exception, "TaskScheduler");
            args.SetObserved();
        };
    }

    private void AppendLine(string path, ref StreamWriter? writer, string channel, string message)
    {
        lock (_writeLock)
        {
            try
            {
                writer ??= OpenWriter(path);
                var ts = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff");
                foreach (var line in message.Split(["\r\n", "\n"], StringSplitOptions.None))
                    writer.WriteLine($"[{ts}] [{channel}] {line}");
                writer.Flush();
            }
            catch
            {
                // Best effort; never fail PM3 operations for logging errors.
            }
        }
    }

    private static StreamWriter OpenWriter(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
        return new StreamWriter(fs) { AutoFlush = true };
    }

    private static string FormatException(Exception exception)
    {
        return exception.ToString();
    }
}
