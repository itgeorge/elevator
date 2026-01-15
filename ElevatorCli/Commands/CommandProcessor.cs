using System.Buffers.Binary;
using System.Text;
using ElevatorCli.IO;
using ElevatorCli.Models;
using ElevatorCli.Printing;

namespace ElevatorCli.Commands;

public class CommandProcessor
{
    private readonly Dictionary<string, IBlockReader> _readersByExt = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IBlockWriter> _writersByExt = new(StringComparer.OrdinalIgnoreCase);

    private T55xxImage? _current;

    public CommandProcessor(IEnumerable<IBlockReader> readers, IEnumerable<IBlockWriter> writers)
    {
        foreach (var r in readers) _readersByExt[r.FormatId] = r;
        foreach (var w in writers) _writersByExt[w.FormatId] = w;
    }

    public void PrintHelp()
    {
        Console.WriteLine("Commands:");
        Console.WriteLine("  default                  Load embedded default-500-rides.bin");
        Console.WriteLine("  open <path>              Open .bin file");
        Console.WriteLine("  save [path]              Save to original path, provided path, or auto-generated filename");
        Console.WriteLine("  unload                   Unload current image");
        Console.WriteLine("  info                     Show info about loaded image");
        Console.WriteLine("  print                    Print table of blocks");
        Console.WriteLine("  get <page> <block>       Get word at page/block");
        Console.WriteLine("  set <page> <block> <HEX> [-x] [-b BITS32]  Set word (hex default)");
        Console.WriteLine("  verify-mirrors           Check Page0 blocks 5 and 6 equality");
        Console.WriteLine("  sync-mirrors [src=5|6]   Copy Page0 block src to the other");
        Console.WriteLine("  set-rides <remaining>    Set rides remaining to specified value");
        Console.WriteLine("  add-rides <more>         Add rides to current remaining count");
        Console.WriteLine("  get-rides                Get rides remaining from current loaded image");
        Console.WriteLine("  help                     Show this help");
        Console.WriteLine("  exit                     Quit");
    }

    public bool Execute(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return true;
        var args = SplitArgs(line);
        if (args.Length == 0) return true;
        var cmd = args[0].ToLowerInvariant();
        try
        {
            switch (cmd)
            {
                case "default":
                    LoadDefault();
                    break;
                case "open":
                    RequireArgs(args, 2, "open <path>");
                    Open(args[1]);
                    break;
                case "save":
                    Save(args.Length >= 2 ? args[1] : null);
                    break;
                case "unload":
                    _current = null;
                    Console.WriteLine("Unloaded.");
                    break;
                case "info":
                    RequireLoaded();
                    PrintInfo();
                    break;
                case "print":
                    RequireLoaded();
                    TablePrinter.PrintTable(_current!, Console.Out);
                    break;
                case "get":
                    RequireArgs(args, 3, "get <page> <block>");
                    RequireLoaded();
                    var (pgG, blG) = (ParseInt(args[1], "page"), ParseInt(args[2], "block"));
                    var w = _current!.GetBlock(pgG, blG);
                    Console.WriteLine(w.ToString("X8"));
                    break;
                case "set":
                    RequireLoaded();
                    HandleSet(args);
                    break;
                case "verify-mirrors":
                    RequireLoaded();
                    VerifyMirrors();
                    break;
                case "sync-mirrors":
                    RequireLoaded();
                    int src = 5;
                    if (args.Length >= 2)
                    {
                        if (!int.TryParse(args[1], out src) || (src != 5 && src != 6))
                            throw new ArgumentException("sync-mirrors [src] where src is 5 or 6");
                    }
                    SyncMirrors(src);
                    break;
                case "set-rides":
                    RequireArgs(args, 2, "set-rides <remaining>");
                    RequireLoaded();
                    SetRides(ParseInt(args[1], "remaining"));
                    break;
                case "add-rides":
                    RequireArgs(args, 2, "add-rides <more>");
                    RequireLoaded();
                    AddRides(ParseInt(args[1], "more"));
                    break;
                case "get-rides":
                    RequireLoaded();
                    GetRides();
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

    private void Open(string path)
    {
        var ext = GetFormatFromPath(path);
        if (!_readersByExt.TryGetValue(ext, out var reader))
            throw new InvalidOperationException($"Unsupported format '.{ext}'");
        var blocks = reader.ReadBlocks(path);
        _current = new T55xxImage(blocks, path);
        Console.WriteLine($"Loaded {_current.BlockCount} blocks from '{path}'.");
    }

    private void LoadDefault()
    {
        var assembly = typeof(CommandProcessor).Assembly;
        var resourceName = assembly
            .GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith("default-500-rides.bin", StringComparison.OrdinalIgnoreCase));
        if (resourceName is null)
            throw new InvalidOperationException("Embedded resource 'default-500-rides.bin' not found.");

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
            throw new InvalidOperationException("Failed to load embedded resource stream.");

        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        var bytes = ms.ToArray();
        if (bytes.Length % 4 != 0)
            throw new InvalidDataException(".bin length must be a multiple of 4 bytes");

        var blocks = new List<uint>(bytes.Length / 4);
        for (var i = 0; i < bytes.Length; i += 4)
        {
            var word = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(i, 4));
            blocks.Add(word);
        }

        _current = new T55xxImage(blocks, "default-500-rides.bin");
        Console.WriteLine($"Loaded {_current.BlockCount} blocks from embedded 'default-500-rides.bin'.");
    }

    private void Save(string? path)
    {
        RequireLoaded();
        string target;
        if (!string.IsNullOrWhiteSpace(path))
        {
            target = path;
        }
        else if (!string.IsNullOrWhiteSpace(_current!.SourcePath))
        {
            target = _current.SourcePath;
        }
        else
        {
            // Generate filename from page 0 blocks
            var sb = new StringBuilder();
            sb.Append("elevator-t55xx-");
            for (int block = 0; block < _current.BlockCount; block++)
            {
                uint blockValue = _current.GetBlock(0, block);
                sb.Append(blockValue.ToString("X8"));
                if (block < _current.BlockCount - 1)
                {
                    sb.Append('-');
                }
            }
            sb.Append(".bin");
            target = sb.ToString();
        }

        var ext = GetFormatFromPath(target);
        if (!_writersByExt.TryGetValue(ext, out var writer))
            throw new InvalidOperationException($"Unsupported format '.{ext}'");
        writer.WriteBlocks(target, _current!.Blocks);
        _current!.SourcePath = target;
        _current.MarkSaved();
        Console.WriteLine($"Saved to '{Path.GetFullPath(target)}'.");
    }

    private void HandleSet(string[] args)
    {
        if (args.Length < 4 || args.Length > 5)
            throw new ArgumentException("Usage: set <page> <block> <HEX> or set <page> <block> (-x HEX | -b BITS32)");

        var page = ParseInt(args[1], "page");
        var block = ParseInt(args[2], "block");

        uint val;
        if (args.Length == 4)
        {
            // Default to hex mode: set <page> <block> <HEX>
            var hex = args[3];
            if (hex.Length != 8 || !uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out val))
                throw new ArgumentException("HEX must be exactly 8 hex chars");
        }
        else // args.Length == 5
        {
            var mode = args[3];
            var value = args[4];
            if (mode == "-x")
            {
                if (value.Length != 8 || !uint.TryParse(value, System.Globalization.NumberStyles.HexNumber, null, out val))
                    throw new ArgumentException("HEX must be exactly 8 hex chars");
            }
            else if (mode == "-b")
            {
                if (value.Length != 32 || value.Any(c => c != '0' && c != '1'))
                    throw new ArgumentException("BITS32 must be exactly 32 chars of 0/1");
                val = Convert.ToUInt32(value, 2);
            }
            else
            {
                throw new ArgumentException("Invalid mode. Use -x for hex or -b for binary, or omit for default hex");
            }
        }

        _current!.SetBlock(page, block, val);
        Console.WriteLine($"Set P{page}B{block} = {val:X8}");
    }

    private void VerifyMirrors()
    {
        if (_current!.BlockCount < 7) { Console.WriteLine("Not enough blocks to verify mirrors."); return; }
        var b5 = _current!.GetBlock(0, 5);
        var b6 = _current!.GetBlock(0, 6);
        Console.WriteLine(b5 == b6 ? "Mirrors match" : $"Mismatch: B5={b5:X8}, B6={b6:X8}");
    }

    private void SyncMirrors(int src)
    {
        if (_current!.BlockCount < 7) { Console.WriteLine("Not enough blocks to sync mirrors."); return; }
        int dst = src == 5 ? 6 : 5;
        var val = _current!.GetBlock(0, src);
        _current!.SetBlock(0, dst, val);
        Console.WriteLine($"Copied P0B{src} -> P0B{dst} ({val:X8})");
    }

    private void SetRides(int remaining)
    {
        if (_current!.BlockCount < 7) { Console.WriteLine("Not enough blocks to set rides."); return; }
        uint block = TokenBlockUtils.Encode((uint)remaining);
        _current!.SetBlock(0, 5, block);
        SyncMirrors(5); // Copy from block 5 to block 6
        Console.WriteLine($"Set rides to {remaining} (block: {block:X8})");
        PrintInfo();
    }

    private void AddRides(int more)
    {
        if (_current!.BlockCount < 7) { Console.WriteLine("Not enough blocks to add rides."); return; }
        uint currentBlock = _current!.GetBlock(0, 5);
        uint currentRides = TokenBlockUtils.Decode(currentBlock);
        uint newRides = currentRides + (uint)more;
        SetRides((int)newRides);
    }

    private void GetRides()
    {
        if (_current!.BlockCount < 7) { Console.WriteLine("Not enough blocks to get rides."); return; }
        uint block = _current!.GetBlock(0, 5);
        uint rides = TokenBlockUtils.Decode(block);
        Console.WriteLine($"Rides remaining: {rides} (block: {block:X8})");
    }

    private void PrintInfo()
    {
        Console.WriteLine($"Blocks: {_current!.BlockCount}, Pages: {_current.PageCount}, Path: {_current.SourcePath ?? "<new>"}, Dirty: {_current.IsDirty}");
        TablePrinter.PrintTable(_current!, Console.Out);
    }

    private static string GetFormatFromPath(string path)
    {
        var ext = Path.GetExtension(path);
        if (string.IsNullOrEmpty(ext)) return "bin"; // default
        if (ext.StartsWith('.')) ext = ext[1..];
        return ext.ToLowerInvariant();
    }

    private void RequireLoaded()
    {
        if (_current is null) throw new InvalidOperationException("No image loaded. Use 'open <path>'.");
    }

    private static void RequireArgs(string[] args, int count, string usage)
    {
        if (args.Length < count) throw new ArgumentException($"Usage: {usage}");
    }

    private static int ParseInt(string s, string name)
    {
        if (!int.TryParse(s, out var v)) throw new ArgumentException($"Invalid {name}: '{s}'");
        return v;
    }

    private static string[] SplitArgs(string input)
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
            else sb.Append(c);
        }
        if (sb.Length > 0) result.Add(sb.ToString());
        return result.ToArray();
    }
}


