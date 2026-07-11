using System.Globalization;
using System.Text;

namespace RideCaptureCli;

public sealed class OtherCaptureCsvStore
{
    private static readonly string[] Headers =
    [
        "timestamp",
        "token_id",
        "warnings",
        "signal_mv",
        "weak_signal",
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

    public IReadOnlyList<OtherCaptureRecord> Load(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
            return [];

        var lines = File.ReadAllLines(fullPath);
        if (lines.Length == 0)
            return [];

        var records = new List<OtherCaptureRecord>();
        foreach (var line in lines.Skip(1))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var fields = ParseCsvLine(line);
            if (fields.Count != Headers.Length)
                throw new InvalidDataException($"CSV row has {fields.Count} fields, expected {Headers.Length}: {line}");

            records.Add(new OtherCaptureRecord
            {
                Timestamp = fields[0],
                TokenId = fields[1],
                Warnings = fields[2],
                SignalMv = int.Parse(fields[3], CultureInfo.InvariantCulture),
                WeakSignal = bool.Parse(fields[4]),
                Block0 = fields[5],
                Block1 = fields[6],
                Block2 = fields[7],
                Block3 = fields[8],
                Block4 = fields[9],
                Block5 = fields[10],
                Block6 = fields[11],
                Block7 = fields[12],
                CopiedDumpRelativePath = fields[13]
            });
        }

        return records;
    }

    public void Save(string path, IReadOnlyList<OtherCaptureRecord> records)
    {
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath) ?? Directory.GetCurrentDirectory());

        using var writer = new StreamWriter(fullPath, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.WriteLine(string.Join(',', Headers));
        foreach (var record in records)
            writer.WriteLine(ToCsvLine(record));
    }

    public void Append(string path, OtherCaptureRecord record)
    {
        var existing = Load(path).ToList();
        existing.Add(record);
        Save(path, existing);
    }

    public OtherCaptureRecord CreateRecord(CaptureScanData scan)
    {
        var warnings = new List<string>();
        if (scan.WeakSignal)
            warnings.Add("WEAK_SIGNAL");
        if (!scan.MirrorMatches)
            warnings.Add("MIRROR_MISMATCH");
        if (string.IsNullOrWhiteSpace(scan.CopiedDumpRelativePath))
            warnings.Add("MISSING_DUMP");

        return new OtherCaptureRecord
        {
            Timestamp = scan.Timestamp.ToString("O", CultureInfo.InvariantCulture),
            TokenId = scan.TokenId,
            Warnings = string.Join('|', warnings),
            SignalMv = scan.SignalMv,
            WeakSignal = scan.WeakSignal,
            Block0 = scan.Blocks[0],
            Block1 = scan.Blocks[1],
            Block2 = scan.Blocks[2],
            Block3 = scan.Blocks[3],
            Block4 = scan.Blocks[4],
            Block5 = scan.Blocks[5],
            Block6 = scan.Blocks[6],
            Block7 = scan.Blocks[7],
            CopiedDumpRelativePath = scan.CopiedDumpRelativePath
        };
    }

    private static string ToCsvLine(OtherCaptureRecord record)
    {
        var values = new[]
        {
            record.Timestamp,
            record.TokenId,
            record.Warnings,
            record.SignalMv.ToString(CultureInfo.InvariantCulture),
            record.WeakSignal.ToString(),
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
