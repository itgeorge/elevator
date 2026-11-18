using System.Globalization;

namespace RidesBridge;

/// <summary>
/// Append-only operator trace of signal millivolts. Lines are timestamp, kind, millivolts, detail.
/// Token blocks, pairing material, and request headers are never written.
/// </summary>
public sealed class SignalTrace
{
    private readonly string _path;
    private readonly object _gate = new();

    public SignalTrace(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            throw new ArgumentException("Signal trace path must be absolute.", nameof(path));
        _path = path;
        WriteRaw($"# session {Timestamp()}");
    }

    public void Write(string kind, int? millivolts, string detail)
    {
        var volts = millivolts?.ToString(CultureInfo.InvariantCulture) ?? "";
        WriteRaw($"{Timestamp()}\t{kind}\t{volts}\t{detail}");
    }

    private void WriteRaw(string line)
    {
        lock (_gate)
        {
            try
            {
                var directory = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);
                File.AppendAllText(_path, line + "\n");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A trace failure must not fail the hardware request the operator is waiting on.
            }
        }
    }

    private static string Timestamp() =>
        DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
}
