using System.Text.Json;
using System.Text.Json.Serialization;
using Pm3UsbApi;

namespace RideCaptureCli;

public sealed class RideCaptureConfig
{
    public int MaxAcceptableSignalMv { get; set; } = 29_000;
    public string OutputRootDirectory { get; set; } = "ride-capture-data";
    public string ProxmarkDumpSearchDirectory { get; set; } = Pm3Options.DevRunsDirectoryName;

    [JsonIgnore]
    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static RideCaptureConfig LoadOrCreate(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            var config = new RideCaptureConfig();
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath) ?? Directory.GetCurrentDirectory());
            File.WriteAllText(fullPath, JsonSerializer.Serialize(config, JsonOptions));
            return config;
        }

        var json = File.ReadAllText(fullPath);
        var loaded = JsonSerializer.Deserialize<RideCaptureConfig>(json, JsonOptions);
        return loaded ?? new RideCaptureConfig();
    }
}
