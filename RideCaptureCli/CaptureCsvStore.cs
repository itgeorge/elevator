using System.Globalization;
using System.Text;

namespace RideCaptureCli;

public sealed class CaptureCsvStore
{
    private static readonly string[] Headers =
    [
        "timestamp",
        "token_id",
        "sequence_id",
        "status",
        "warnings",
        "signal_mv",
        "weak_signal",
        "tracked_count",
        "real_ride_count",
        "zero_anchor",
        "block0",
        "block1",
        "block2",
        "block3",
        "block4",
        "block5",
        "block6",
        "block7",
        "copied_dump_relative_path"
    ];

    public IReadOnlyList<CaptureRecord> Load(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
            return [];

        var lines = File.ReadAllLines(fullPath);
        if (lines.Length == 0)
            return [];

        var records = new List<CaptureRecord>();
        foreach (var line in lines.Skip(1))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var fields = ParseCsvLine(line);
            if (fields.Count != Headers.Length)
                throw new InvalidDataException($"CSV row has {fields.Count} fields, expected {Headers.Length}: {line}");

            records.Add(new CaptureRecord
            {
                Timestamp = fields[0],
                TokenId = fields[1],
                SequenceId = fields[2],
                Status = Enum.Parse<CaptureStatus>(fields[3], ignoreCase: true),
                Warnings = fields[4],
                SignalMv = int.Parse(fields[5], CultureInfo.InvariantCulture),
                WeakSignal = bool.Parse(fields[6]),
                TrackedCount = int.Parse(fields[7], CultureInfo.InvariantCulture),
                RealRideCount = string.IsNullOrEmpty(fields[8]) ? null : int.Parse(fields[8], CultureInfo.InvariantCulture),
                ZeroAnchor = bool.Parse(fields[9]),
                Block0 = fields[10],
                Block1 = fields[11],
                Block2 = fields[12],
                Block3 = fields[13],
                Block4 = fields[14],
                Block5 = fields[15],
                Block6 = fields[16],
                Block7 = fields[17],
                CopiedDumpRelativePath = fields[18]
            });
        }

        return records;
    }

    public void Save(string path, IReadOnlyList<CaptureRecord> records)
    {
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath) ?? Directory.GetCurrentDirectory());

        using var writer = new StreamWriter(fullPath, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.WriteLine(string.Join(',', Headers));
        foreach (var record in records)
            writer.WriteLine(ToCsvLine(record));
    }

    public void Append(string path, CaptureRecord record)
    {
        var existing = Load(path).ToList();
        existing.Add(record);
        Save(path, existing);
    }

    private static string ToCsvLine(CaptureRecord record)
    {
        var values = new[]
        {
            record.Timestamp,
            record.TokenId,
            record.SequenceId,
            record.Status.ToString(),
            record.Warnings,
            record.SignalMv.ToString(CultureInfo.InvariantCulture),
            record.WeakSignal.ToString(),
            record.TrackedCount.ToString(CultureInfo.InvariantCulture),
            record.RealRideCount?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            record.ZeroAnchor.ToString(),
            record.Block0,
            record.Block1,
            record.Block2,
            record.Block3,
            record.Block4,
            record.Block5,
            record.Block6,
            record.Block7,
            record.CopiedDumpRelativePath
        };

        return string.Join(',', values.Select(Escape));
    }

    private static string Escape(string value)
    {
        value ??= string.Empty;
        var requiresQuotes = value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r') || value.Contains(' ');
        if (!requiresQuotes)
            return value;

        return $"\"{value.Replace("\"", "\"\"")}\"";
    }

    private static IReadOnlyList<string> ParseCsvLine(string line)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    sb.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
                continue;
            }

            if (c == ',' && !inQuotes)
            {
                result.Add(sb.ToString());
                sb.Clear();
                continue;
            }

            sb.Append(c);
        }

        result.Add(sb.ToString());
        return result;
    }
}
