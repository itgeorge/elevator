using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Pm3UsbApi.Diagnostics;

/// <summary>
/// TEMPORARY instrumentation for LF tune stabilization experiments. Remove after choosing defaults.
/// </summary>
public sealed class LfTuneProbeSession : IDisposable
{
    private static readonly AsyncLocal<LfTuneProbeSession?> CurrentSession = new();

    private readonly DateTime _startedUtc;
    private readonly int _sampleCountRequested;
    private readonly double _timeoutMs;
    private bool _completed;
    private bool _disposed;

    private LfTuneProbeSession(string label, int sampleCountRequested, TimeSpan timeout)
    {
        Label = label;
        _sampleCountRequested = sampleCountRequested;
        _timeoutMs = timeout.TotalMilliseconds;
        _startedUtc = DateTime.UtcNow;
        StartedTickMs = Environment.TickCount64;
    }

    public string Label { get; }

    public long StartedTickMs { get; }

    public List<LfTuneProbeSample> Samples { get; } = [];

    public uint PeakMillivolts { get; private set; }

    public static LfTuneProbeSession? Current => CurrentSession.Value;

    public static LfTuneProbeSession Begin(string label, int sampleCountRequested, TimeSpan timeout)
    {
        if (string.IsNullOrWhiteSpace(label))
            throw new ArgumentException("Probe label is required.", nameof(label));

        if (CurrentSession.Value is not null)
            throw new InvalidOperationException("An LF tune probe session is already active.");

        var session = new LfTuneProbeSession(label.Trim(), sampleCountRequested, timeout);
        CurrentSession.Value = session;
        return session;
    }

    public void RecordSample(int index, long elapsedMs, uint millivolts, uint runningPeakMv)
    {
        Samples.Add(new LfTuneProbeSample(index, elapsedMs, millivolts, runningPeakMv));
        PeakMillivolts = runningPeakMv;
    }

    public void Complete(uint peakMillivolts)
    {
        PeakMillivolts = peakMillivolts;
        _completed = true;
    }

    public string WriteResults(string? outputDirectory = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var dir = ResolveOutputDirectory(outputDirectory);
        Directory.CreateDirectory(dir);

        var safeLabel = SanitizeLabel(Label);
        var stamp = _startedUtc.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var baseName = $"{stamp}-{safeLabel}";
        var jsonPath = Path.Combine(dir, $"{baseName}.json");
        var csvPath = Path.Combine(dir, $"{baseName}.csv");

        var endedUtc = DateTime.UtcNow;
        var document = new LfTuneProbeDocument(
            Label,
            _startedUtc,
            endedUtc,
            _sampleCountRequested,
            _timeoutMs,
            Samples.Count,
            PeakMillivolts,
            _completed,
            Samples.ToArray());

        var json = JsonSerializer.Serialize(
            document,
            new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            });
        var utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        File.WriteAllText(jsonPath, json, utf8NoBom);
        File.WriteAllText(csvPath, BuildCsv(document), utf8NoBom);

        return jsonPath;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        CurrentSession.Value = null;
        _disposed = true;
    }

    private static string ResolveOutputDirectory(string? outputDirectory)
    {
        if (!string.IsNullOrWhiteSpace(outputDirectory))
            return Path.GetFullPath(outputDirectory.Trim());

        var env = Environment.GetEnvironmentVariable("PM3_LF_TUNE_PROBE_DIR");
        if (!string.IsNullOrWhiteSpace(env))
            return Path.GetFullPath(env.Trim());

        return Path.GetFullPath(Path.Combine("debug", "lf-tune-probes"));
    }

    private static string SanitizeLabel(string label)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(label.Length);
        foreach (var ch in label)
            sb.Append(Array.IndexOf(invalid, ch) >= 0 ? '-' : ch);
        return sb.ToString();
    }

    private static string BuildCsv(LfTuneProbeDocument document)
    {
        var sb = new StringBuilder();
        sb.AppendLine("index,elapsed_ms,millivolts,running_peak_mv");
        foreach (var sample in document.Samples)
            sb.AppendLine(FormattableString.Invariant(
                $"{sample.Index},{sample.ElapsedMs},{sample.Millivolts},{sample.RunningPeakMv}"));
        return sb.ToString();
    }

    public sealed record LfTuneProbeSample(int Index, long ElapsedMs, uint Millivolts, uint RunningPeakMv);

    private sealed record LfTuneProbeDocument(
        string Label,
        DateTime StartedUtc,
        DateTime EndedUtc,
        int SampleCountRequested,
        double TimeoutMs,
        int SamplesTaken,
        uint PeakMillivolts,
        bool Completed,
        LfTuneProbeSample[] Samples);
}
