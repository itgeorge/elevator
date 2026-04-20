namespace RideCaptureCli;

public static class ConsoleStatusWriter
{
    public static void WriteCaptureResult(CaptureRecord record, bool autoNormalized)
    {
        if (record.WeakSignal)
            WriteBanner("----- WEAK SIGNAL -----", ConsoleColor.Yellow);

        if (record.Warnings.Contains("UNKNOWN_TOKEN", StringComparison.Ordinal))
            WriteBanner("?????? UNKNOWN TOKEN ??????", ConsoleColor.Magenta);

        if (record.Warnings.Contains("MIRROR_MISMATCH", StringComparison.Ordinal))
            WriteBanner("!!!!!! MIRROR MISMATCH !!!!!!", ConsoleColor.Red);

        if (record.Status == CaptureStatus.NoChange)
            WriteBanner("===== NO CHANGE =====", ConsoleColor.Cyan);
        else
            WriteBanner("******** OK ********", ConsoleColor.Green);

        Console.WriteLine($"time:      {record.Timestamp}");
        Console.WriteLine($"token:     {record.TokenId}");
        Console.WriteLine($"sequence:  {record.SequenceId}");
        Console.WriteLine($"signal:    {record.SignalMv} mV");
        Console.WriteLine($"tracked:   {record.TrackedCount}");
        Console.WriteLine($"real:      {(record.RealRideCount.HasValue ? record.RealRideCount.Value : "<unknown>")}");
        Console.WriteLine($"state:     {record.Block5} / {record.Block6}");
        Console.WriteLine($"dump:      {(string.IsNullOrWhiteSpace(record.CopiedDumpRelativePath) ? "<not found>" : record.CopiedDumpRelativePath)}");
        if (!string.IsNullOrWhiteSpace(record.Warnings))
            Console.WriteLine($"warnings:  {record.Warnings}");
        if (record.ZeroAnchor)
            Console.WriteLine("anchor:    ZERO");
        if (autoNormalized)
            Console.WriteLine("anchor:    AUTO_NORMALIZED_FROM_KNOWN_STATE");
        Console.WriteLine();
    }

    public static void WriteError(string message)
    {
        WriteBanner("!!!!!! ERROR !!!!!!", ConsoleColor.Red);
        Console.WriteLine(message);
        Console.WriteLine();
    }

    public static void WriteInfo(string message) => Console.WriteLine(message);

    private static void WriteBanner(string text, ConsoleColor color)
    {
        var previous = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.WriteLine(text);
        Console.ForegroundColor = previous;
    }
}
