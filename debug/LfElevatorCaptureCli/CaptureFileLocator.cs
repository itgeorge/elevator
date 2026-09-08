namespace LfElevatorCaptureCli;

public static class CaptureFileLocator
{
    /// <summary>Finds the exact PM3 save or its collision-suffixed equivalent.</summary>
    public static string? Find(string directory, string baseName)
    {
        var exact = Path.Combine(directory, baseName + ".pm3");
        if (File.Exists(exact)) return exact;

        return Directory.GetFiles(directory, baseName + "*.pm3")
            .Where(path => Path.GetFileName(path).StartsWith(baseName + "-", StringComparison.Ordinal))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }
}
