namespace Pm3UsbApi;

/// <summary>
/// Configuration options for the Proxmark3 USB API.
/// </summary>
public record Pm3Options
{
    /// <summary>
    /// Folder name for dev/test runs. Use with <see cref="WorkingDirectory"/> to isolate
    /// proxmark3 output files (e.g. lf-t55xx-*.bin) from the repo. Ignore via .gitignore.
    /// </summary>
    public const string DevRunsDirectoryName = "proxmark-runs";

    /// <summary>
    /// Working directory for the proxmark3 process. When set, the process runs here (output files
    /// like lf-t55xx-*.bin go here). When null, the process uses the current directory.
    /// Set to <see cref="DevRunsDirectoryName"/> (or full path) for test/dev to avoid clutter.
    /// Leave null for published executables.
    /// </summary>
    public string? WorkingDirectory { get; init; }
    /// <summary>
    /// Absolute path to proxmark3 executable (or pm3.bat).
    /// null = auto-detect from PATH / common locations.
    /// </summary>
    public string? Pm3ClientPath { get; init; }

    /// <summary>
    /// COM port or device path (e.g., "COM3", "/dev/ttyACM0").
    /// null = let pm3 client auto-detect.
    /// </summary>
    public string? DevicePort { get; init; }

    /// <summary>
    /// Timeout for a single command execution (including process startup for per-invocation).
    /// </summary>
    public TimeSpan DefaultCommandTimeout { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Timeout for connection verification.
    /// </summary>
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Prompt regex patterns for interactive mode (future).
    /// </summary>
    public string[] PromptPatterns { get; init; } =
    [
        @"^\[usb\].*pm3\s*-->\s*$",
        @"^proxmark3>\s*$",
        @"^pm3\s*-->\s*$"
    ];

    /// <summary>
    /// Enable transcript logging of all commands and responses.
    /// </summary>
    public bool EnableTranscriptLogging { get; init; }

    /// <summary>
    /// Path for transcript log file. null = auto-generate in temp.
    /// </summary>
    public string? TranscriptPath { get; init; }
}
