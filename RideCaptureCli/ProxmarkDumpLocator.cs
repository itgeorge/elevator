namespace RideCaptureCli;

public sealed class ProxmarkDumpLocator
{
    public string? LocateNewestMatchingBin(string searchDirectory, CaptureScanData scan, DateTimeOffset dumpStartedAt)
    {
        var fullSearchDirectory = Path.GetFullPath(searchDirectory);
        if (!Directory.Exists(fullSearchDirectory))
            return null;

        var requiredParts = scan.Blocks.Skip(1).Take(6).ToArray();
        return Directory.EnumerateFiles(fullSearchDirectory, "lf-t55xx-*.bin", SearchOption.AllDirectories)
            .Select(path => new FileInfo(path))
            .Where(info => info.Exists)
            .Where(info => info.LastWriteTimeUtc >= dumpStartedAt.UtcDateTime.AddSeconds(-5))
            .Where(info => requiredParts.All(part => info.Name.Contains(part, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(info => info.LastWriteTimeUtc)
            .Select(info => info.FullName)
            .FirstOrDefault();
    }

    public string CopyIntoDataset(string sourcePath, CapturePaths paths, DateTimeOffset now)
    {
        var sourceFullPath = Path.GetFullPath(sourcePath);
        var targetDirectory = paths.GetDatedDumpDirectory(now);
        Directory.CreateDirectory(targetDirectory);

        var targetFileName = $"{now:HHmmss}-{Path.GetFileName(sourceFullPath)}";
        var targetPath = Path.Combine(targetDirectory, targetFileName);
        File.Copy(sourceFullPath, targetPath, overwrite: true);
        return Path.GetRelativePath(paths.OutputRootDirectory, targetPath);
    }
}
