namespace RideCaptureCli;

public sealed class CapturePaths
{
    public CapturePaths(string outputRootDirectory)
    {
        OutputRootDirectory = Path.GetFullPath(outputRootDirectory);
    }

    public string OutputRootDirectory { get; }
    public string CsvPath => Path.Combine(OutputRootDirectory, "captures.csv");
    public string OtherCsvPath => Path.Combine(OutputRootDirectory, "other-captures.csv");
    public string DumpsRootDirectory => Path.Combine(OutputRootDirectory, "dumps");

    public void EnsureExists()
    {
        Directory.CreateDirectory(OutputRootDirectory);
        Directory.CreateDirectory(DumpsRootDirectory);
    }

    public string GetDatedDumpDirectory(DateTimeOffset now) =>
        Path.Combine(DumpsRootDirectory, now.ToString("yyyy-MM-dd"));
}
