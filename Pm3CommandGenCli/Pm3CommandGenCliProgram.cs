using System.Text;
using TokenDumpsCli.IO;
using Tokens;

namespace Pm3CommandGenCli;

class Pm3CommandGenCliProgram
{
    private static string? _dumpDir;
    private static List<FileInfo> _dumps = [];
    private static T55xxImage? _current;
    private static readonly BinBlockReader _binReader = new();

    static void Main(string[] args)
    {
        Console.WriteLine("PM3 command generator. Type 'help' for commands. Ctrl+C to exit.");
        PrintHelp();

        while (true)
        {
            Console.Write("pm3> ");
            var line = Console.ReadLine();
            if (line == null) break;
            var keepGoing = Execute(line);
            if (!keepGoing) break;
        }
    }

    static void PrintHelp()
    {
        Console.WriteLine("Commands:");
        Console.WriteLine("  setdumpdir <dir>   Set directory for .bin dumps (supports quoted paths)");
        Console.WriteLine("  listdumps          List .bin dumps in dump directory, sorted by date descending");
        Console.WriteLine("  load [index]       Load dump at index from listdumps; omit to load last");
        Console.WriteLine("  rides              Show rides remaining in loaded dump");
        Console.WriteLine("  addrides <count>   Add rides to loaded dump (updates block 5 and 6)");
        Console.WriteLine("  command            Print proxmark3 write command: lf t55 write -b 5 -d <hex>");
        Console.WriteLine("  help               Show this help");
        Console.WriteLine("  exit               Quit");
    }

    static bool Execute(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return true;
        var args = SplitArgs(line);
        if (args.Length == 0) return true;
        var cmd = args[0].ToLowerInvariant();

        try
        {
            switch (cmd)
            {
                case "setdumpdir":
                    RequireArgs(args, 2, "setdumpdir <dir>");
                    SetDumpDir(args[1]);
                    break;
                case "listdumps":
                    ListDumps();
                    break;
                case "load":
                    Load(args.Length >= 2 ? args[1] : null);
                    break;
                case "rides":
                    RequireLoaded();
                    ShowRides();
                    break;
                case "addrides":
                    RequireArgs(args, 2, "addrides <count>");
                    RequireLoaded();
                    AddRides(ParseInt(args[1], "count"));
                    break;
                case "command":
                    RequireLoaded();
                    PrintCommand();
                    break;
                case "help":
                    PrintHelp();
                    break;
                case "exit":
                    return false;
                default:
                    Console.Error.WriteLine($"Unknown command: {cmd}. Type 'help'.");
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
        }
        return true;
    }

    static void SetDumpDir(string dir)
    {
        dir = dir.Trim();
        if (string.IsNullOrEmpty(dir))
            throw new ArgumentException("Directory cannot be empty");

        var path = Path.GetFullPath(dir);
        if (!Directory.Exists(path))
            throw new DirectoryNotFoundException($"Directory not found: {path}");

        _dumpDir = path;
        _dumps.Clear();
        Console.WriteLine($"Dump directory set to: {path}");
    }

    static void ListDumps()
    {
        if (_dumpDir is null)
        {
            Console.Error.WriteLine("Dump directory not set. Use 'setdumpdir <dir>' first.");
            return;
        }

        _dumps = Directory.GetFiles(_dumpDir, "*.bin", SearchOption.TopDirectoryOnly)
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .ToList();

        if (_dumps.Count == 0)
        {
            Console.WriteLine("No .bin dumps found.");
            return;
        }

        for (int i = 0; i < _dumps.Count; i++)
        {
            var f = _dumps[i];
            Console.WriteLine($"{i,3}  {f.LastWriteTimeUtc:yyyy-MM-dd HH:mm:ss}  {f.Name}");
        }
        Console.WriteLine($"({_dumps.Count} dump(s) total)");
    }

    static void Load(string? indexStr)
    {
        ListDumps();
        if (_dumpDir is null)
            throw new InvalidOperationException("Dump directory not set. Use 'setdumpdir <dir>' first.");
        if (_dumps.Count == 0)
            throw new InvalidOperationException("No dumps found in dump directory.");

        int index;
        if (string.IsNullOrWhiteSpace(indexStr))
        {
            index = 0; // "last" = first in date-descending order
        }
        else
        {
            index = ParseInt(indexStr, "index");
            if (index < 0 || index >= _dumps.Count)
                throw new ArgumentOutOfRangeException("index", $"Index must be 0..{_dumps.Count - 1}");
        }

        var file = _dumps[index];
        var path = file.FullName;
        var blocks = _binReader.ReadBlocks(path);
        _current = new T55xxImage(blocks, path);
        Console.WriteLine($"Loaded: {path} ({_current.BlockCount} blocks) [{file.LastWriteTimeUtc:yyyy-MM-dd HH:mm:ss}]");
    }

    static void ShowRides()
    {
        if (_current!.BlockCount < 7)
        {
            Console.WriteLine("Loaded dump has fewer than 7 blocks; cannot read rides.");
            return;
        }

        var block = _current.GetBlock(0, 5);
        uint rides = TokenBlockUtils.Decode(block);
        Console.WriteLine($"Rides remaining: {rides} (block 5: {block.ToHex()})");
    }

    static void AddRides(int count)
    {
        if (_current!.BlockCount < 7)
            throw new InvalidOperationException("Loaded dump has fewer than 7 blocks; cannot add rides.");

        var currentBlock = _current.GetBlock(0, 5);
        uint currentRides = TokenBlockUtils.Decode(currentBlock);
        uint newRides = (uint)(currentRides + (int)count);

        var encoded = TokenBlockUtils.Encode(newRides);
        _current.SetBlock(0, 5, encoded);
        _current.SetBlock(0, 6, encoded); // sync mirror
        Console.WriteLine($"Added {count} rides. New total: {newRides} (block: {encoded.ToHex()})");
    }

    static void PrintCommand()
    {
        if (_current!.BlockCount < 7)
        {
            Console.WriteLine("Loaded dump has fewer than 7 blocks; block 5 unavailable.");
            return;
        }

        var block5 = _current.GetBlock(0, 5);
        var hex = block5.ToHex();
        Console.WriteLine($"lf t55 write -b 5 -d {hex}");
    }

    static void RequireLoaded()
    {
        if (_current is null)
            throw new InvalidOperationException("No dump loaded. Use 'listdumps' then 'load [index]'.");
    }

    static void RequireArgs(string[] args, int count, string usage)
    {
        if (args.Length < count)
            throw new ArgumentException($"Usage: {usage}");
    }

    static int ParseInt(string s, string name)
    {
        if (!int.TryParse(s, out var v))
            throw new ArgumentException($"Invalid {name}: '{s}'");
        return v;
    }

    static string[] SplitArgs(string input)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false;
        for (int i = 0; i < input.Length; i++)
        {
            char c = input[i];
            if (c == '"') { inQuotes = !inQuotes; continue; }
            if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (sb.Length > 0) { result.Add(sb.ToString()); sb.Clear(); }
            }
            else
            {
                sb.Append(c);
            }
        }
        if (sb.Length > 0) result.Add(sb.ToString());
        return result.ToArray();
    }
}
