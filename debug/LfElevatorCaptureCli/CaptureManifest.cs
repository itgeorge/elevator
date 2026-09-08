using System.Text.Json;
using System.Text.Json.Serialization;

namespace LfElevatorCaptureCli;

public sealed class CaptureManifest
{
    public string SchemaVersion { get; init; } = "1";
    public string Label { get; init; } = "";
    public string SanitizedLabel { get; init; } = "";
    public string StartedUtc { get; init; } = "";
    public string? EndedUtc { get; set; }
    public int SamplesPerWindow { get; init; }
    public int? RequestedWindows { get; init; }
    public int? DurationSeconds { get; init; }
    public int KeepWindows { get; init; }
    public string CaptureMode { get; init; } = "bounded-lf-sniff-with-local-data-save";
    public string[] Commands { get; init; } = [PassiveCapturePlan.ConnectCommand, "lf sniff -s <samples>", "data save -f <run-directory>/<capture>.pm3"];
    public string[] ExplicitlyForbiddenOperations { get; init; } = [
        "tag writes", "password commands", "config commands", "tune commands",
        "block reads/dumps", "clone/sim/reset operations"];
    public List<CaptureEntry> Captures { get; } = [];
    public List<string> Events { get; } = [];
}

public sealed class CaptureEntry
{
    public int Window { get; init; }
    public string StartedUtc { get; init; } = "";
    public string EndedUtc { get; set; } = "";
    public string FileName { get; set; } = "";
    public int SamplesRequested { get; init; }
    public string Status { get; set; } = "started";
    public bool Retained { get; set; }
    public string? Error { get; set; }
}

public static class CaptureManifestStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static void Save(string path, CaptureManifest manifest)
    {
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(manifest, JsonOptions));
        File.Move(temporary, path, overwrite: true);
    }
}
