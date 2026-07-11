using System.Buffers.Binary;
using System.Globalization;

namespace RideCaptureCli;

public sealed class ProxmarkDumpLocator
{
    public string? LocateNewestMatchingBin(string searchDirectory, CaptureScanData scan, DateTimeOffset dumpStartedAt)
    {
        var fullSearchDirectory = Path.GetFullPath(searchDirectory);
        if (!Directory.Exists(fullSearchDirectory))
            return null;

        var requiredParts = scan.Blocks.Skip(1).Take(6).ToArray();
        return Directory.EnumerateFiles(fullSearchDirectory, "lf-t55xx-*.bin", SearchOption.TopDirectoryOnly)
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

    public string WritePage0BinIntoDataset(CapturePaths paths, CaptureScanData scan)
    {
        if (scan.Blocks.Count < 8)
            throw new InvalidOperationException("Cannot write page 0 dump without at least 8 blocks.");

        var targetDirectory = paths.GetDatedDumpDirectory(scan.Timestamp);
        Directory.CreateDirectory(targetDirectory);

        var blockParts = string.Join('-', scan.Blocks.Skip(1).Take(6));
        var targetFileName = $"{scan.Timestamp:HHmmss}-lf-t55xx-{blockParts}-native-page0-dump.bin";
        var targetPath = Path.Combine(targetDirectory, targetFileName);

        var bytes = new byte[8 * sizeof(uint)];
        for (var i = 0; i < 8; i++)
        {
            if (!uint.TryParse(scan.Blocks[i], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
                throw new InvalidOperationException($"Cannot write page 0 dump; block {i} is not valid hex: {scan.Blocks[i]}");
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(i * sizeof(uint), sizeof(uint)), value);
        }

        File.WriteAllBytes(targetPath, bytes);
        return Path.GetRelativePath(paths.OutputRootDirectory, targetPath);
    }
}
