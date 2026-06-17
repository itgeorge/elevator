using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using Tokens;

namespace RidesCli;

/// <summary>
/// Handles ride-related commands. Keeps CLI as thin I/O layer.
/// </summary>
public sealed class RidesCommandHandler
{
    private readonly IRidesPm3Api _pm3;
    private readonly IRidesOutput _output;
    private readonly RidesConfig _config;
    private readonly IRidesInput _input;

    private uint? _rides;
    private string? _lastDumpRaw;

    public RidesCommandHandler(IRidesPm3Api pm3, IRidesOutput output, RidesConfig config, IRidesInput? input = null)
    {
        _pm3 = pm3 ?? throw new ArgumentNullException(nameof(pm3));
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _input = input ?? new ConsoleRidesInput();
    }

    /// <summary>Execute a command. Returns false to signal exit.</summary>
    public bool Execute(string[] args)
    {
        if (args.Length == 0) return true;

        var cmd = args[0].ToLowerInvariant();
        try
        {
            return cmd switch
            {
                "config" => ExecuteConfig(args[1..]),
                "tune" => ExecuteTune(args[1..]),
                "read" => ExecuteRead(args[1..]),
                "reset" => ExecuteReset(args[1..]),
                "set" => ExecuteSet(args[1..]),
                "add" => ExecuteAdd(args[1..]),
                "price" => ExecutePrice(args[1..]),
                "money" => ExecuteMoney(args[1..]),
                "exit" => false,
                "help" => ExecuteHelp(),
                _ => ExecuteUnknown(cmd)
            };
        }
        catch (Exception ex)
        {
            _output.WriteLine($"Error: {ex.Message}");
            return true;
        }
    }

    private bool ExecuteConfig(string[] args)
    {
        if (args.Length < 2)
        {
            _output.WriteLine("Usage: config <key> <value>");
            return true;
        }

        var key = args[0].ToLowerInvariant();
        var value = args[1];

        if (key == "priceper100")
        {
            if (!ParsePricePer100(value, out var price))
            {
                _output.WriteLine($"Error: invalid pricePer100 '{value}', expected form like 4.00 or 24.50");
                return true;
            }
            _config.PricePer100 = price;
            _output.WriteLine($"pricePer100 = {FormatInvariant2(price)}");
        }
        else
        {
            _output.WriteLine($"Error: unknown config key '{key}'");
        }
        return true;
    }

    private static bool ParsePricePer100(string s, out decimal result)
    {
        result = 0;
        if (string.IsNullOrWhiteSpace(s)) return false;
        s = s.Trim();
        if (!decimal.TryParse(s, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var v))
            return false;
        if (v < 0) return false;
        result = v;
        return true;
    }

    private static string FormatInvariant2(decimal value) =>
        value.ToString("F2", CultureInfo.InvariantCulture);

    private bool ExecuteTune(string[] args)
    {
        if (args.Length > 0)
        {
            _output.WriteLine("Usage: tune");
            return true;
        }

        return ExecuteTuneCore().GetAwaiter().GetResult();
    }

    private bool ExecuteRead(string[] args)
    {
        var showDump = args.Length > 0 && args[0] == "-d";
        return ExecuteReadCore(showDump).GetAwaiter().GetResult();
    }

    private bool ExecuteReset(string[] args)
    {
        if (args.Length > 0)
        {
            _output.WriteLine("Usage: reset");
            return true;
        }

        return ExecuteResetCore().GetAwaiter().GetResult();
    }

    private async Task<bool> ExecuteTuneCore()
    {
        var mv = await _pm3.GetSignalStrengthMvAsync();
        _output.WriteLine($"signal strength: {mv} mV");
        return true;
    }

    private async Task<bool> ExecuteReadCore(bool showDump)
    {
        try
        {
            var block5Hex = await _pm3.ReadPage0BlockAsync(5);
            var block6Hex = await _pm3.ReadPage0BlockAsync(6);
            var block5 = T55Block.FromHex(block5Hex);
            var block6 = T55Block.FromHex(block6Hex);

            if (showDump)
            {
                var dump = await _pm3.DumpAsync();
                _lastDumpRaw = dump;
                _output.WriteLine(dump);
            }

            var result = RideBlockResolver.Resolve(block5, block6);

            if (!string.IsNullOrEmpty(result.WarningMessage))
                _output.WriteLine(result.WarningMessage);

            switch (result.Status)
            {
                case RideReadStatus.Success:
                    _rides = result.Rides!.Value;
                    _output.WriteLine($"rides remaining: {_rides.Value}");
                    return true;
                case RideReadStatus.UnknownEncodingFamily:
                    _rides = null;
                    await HandleUnknownEncodingFamilyAsync(block5).ConfigureAwait(false);
                    return true;
                default:
                    _rides = null;
                    _lastDumpRaw = null;
                    _output.WriteLine("Error: could not decode rides from token (invalid block format).");
                    return true;
            }
        }
        catch (Exception ex)
        {
            _rides = null;
            _lastDumpRaw = null;
            _output.WriteLine($"Error: {ex.Message}");
            return true;
        }
    }

    private async Task<bool> ExecuteResetCore()
    {
        _rides = null;

        try
        {
            var block5Hex = await _pm3.ReadPage0BlockAsync(5).ConfigureAwait(false);
            var block6Hex = await _pm3.ReadPage0BlockAsync(6).ConfigureAwait(false);
            var describe = RideBlockResolver.Resolve(
                T55Block.FromHex(block5Hex),
                T55Block.FromHex(block6Hex));

            switch (describe.Status)
            {
                case RideReadStatus.Success:
                    _output.WriteLine($"current token rides: {describe.Rides!.Value}");
                    break;
                case RideReadStatus.UnknownEncodingFamily:
                    _output.WriteLine($"Current token cannot be decoded: unknown encoding family in block 5 ({block5Hex}).");
                    break;
                default:
                    _output.WriteLine($"Current token cannot be decoded: invalid block format in block 5 ({block5Hex}).");
                    break;
            }
        }
        catch (Exception ex)
        {
            _output.WriteLine($"Error: no token detected. {ex.Message}");
            return true;
        }

        if (!PromptForYesNo("Overwrite token with reset image and set rides to 0? [y/N]"))
        {
            _output.WriteLine("Cancelled.");
            return true;
        }

        var resetBlocks = LoadDefaultResetPage0Blocks();
        var zeroBlock = TokenBlockUtils.Encode(0);
        resetBlocks[5] = zeroBlock;
        resetBlocks[6] = zeroBlock;

        var success = await _pm3.WriteAndVerifyPage0BlocksAsync(resetBlocks, 1, 6).ConfigureAwait(false);

        _output.WriteLine(success ? "Success." : "Error: block write/verify failed.");
        if (success)
        {
            _rides = 0;
            _output.WriteLine("rides remaining: 0");
        }

        return true;
    }

    private bool PromptForYesNo(string prompt)
    {
        while (true)
        {
            _output.WriteLine(prompt);
            var input = _input.ReadLine()?.Trim();
            if (string.IsNullOrEmpty(input) || input.Equals("n", StringComparison.OrdinalIgnoreCase) || input.Equals("no", StringComparison.OrdinalIgnoreCase))
                return false;
            if (input.Equals("y", StringComparison.OrdinalIgnoreCase) || input.Equals("yes", StringComparison.OrdinalIgnoreCase))
                return true;

            _output.WriteLine("Please answer 'y' or 'n'.");
        }
    }

    private static List<T55Block> LoadDefaultResetPage0Blocks()
    {
        var assembly = typeof(RidesCommandHandler).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith("default-500-rides.bin", StringComparison.OrdinalIgnoreCase));
        if (resourceName is null)
            throw new InvalidOperationException("Embedded resource 'default-500-rides.bin' not found.");

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
            throw new InvalidOperationException("Failed to load embedded resource stream.");

        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        var bytes = ms.ToArray();
        if (bytes.Length < 32 || bytes.Length % 4 != 0)
            throw new InvalidDataException("default-500-rides.bin must contain at least 8 blocks and be a multiple of 4 bytes.");

        var blocks = new List<T55Block>(8);
        for (var i = 0; i < 8 * 4; i += 4)
        {
            var word = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(i, 4));
            blocks.Add(new T55Block(word));
        }

        return blocks;
    }

    private async Task HandleUnknownEncodingFamilyAsync(T55Block block5)
    {
        _output.WriteLine($"Unknown encoding family detected in page 0 block 5: {block5.ToHex()}");

        var blocks = await ReadPage0BlocksAsync();
        var dumpPath = SaveTokenDump(blocks, "UNKNOWN");

        var ridesLabel = PromptForKnownRideCountLabel();
        dumpPath = UpdateSavedTokenDumpPath(dumpPath, blocks, ridesLabel);

        _output.WriteLine($"Saved token dump to '{Path.GetFullPath(dumpPath)}'.");
        _output.WriteLine("Error: could not decode rides because the token uses an unknown encoding family.");
    }

    private string PromptForKnownRideCountLabel()
    {
        while (true)
        {
            _output.WriteLine("Enter known ride count for dump filename suffix, or press Enter to use UNKNOWN:");
            var input = _input.ReadLine()?.Trim();
            if (string.IsNullOrEmpty(input))
                return "UNKNOWN";

            if (uint.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rides))
                return rides.ToString(CultureInfo.InvariantCulture);

            _output.WriteLine("Error: invalid ride count. Enter a non-negative integer, or press Enter to use UNKNOWN.");
        }
    }

    private string SaveTokenDump(IReadOnlyList<T55Block> blocks, string ridesLabel)
    {
        var path = BuildDumpPath(blocks, ridesLabel);
        WriteBinDump(path, blocks);
        return path;
    }

    private string UpdateSavedTokenDumpPath(string currentPath, IReadOnlyList<T55Block> blocks, string ridesLabel)
    {
        var desiredPath = BuildDumpPath(blocks, ridesLabel);
        if (Path.GetFullPath(currentPath) == Path.GetFullPath(desiredPath))
            return currentPath;

        File.Move(currentPath, desiredPath, overwrite: true);
        return desiredPath;
    }

    private string BuildDumpPath(IReadOnlyList<T55Block> blocks, string ridesLabel)
    {
        if (string.IsNullOrWhiteSpace(_config.DumpDirectory))
            throw new InvalidOperationException("Dump directory is not configured.");

        Directory.CreateDirectory(_config.DumpDirectory);

        var baseFileName = BuildDumpFileName(blocks);
        var fileName = AddSuffixBeforeExtension(baseFileName, $"--rides-{ridesLabel}");
        return Path.Combine(_config.DumpDirectory, fileName);
    }

    private async Task<IReadOnlyList<T55Block>> ReadPage0BlocksAsync()
    {
        var blocks = new List<T55Block>(8);
        for (uint block = 0; block < 8; block++)
        {
            var hex = await _pm3.ReadPage0BlockAsync(block);
            blocks.Add(T55Block.FromHex(hex));
        }
        return blocks;
    }

    private static string BuildDumpFileName(IReadOnlyList<T55Block> blocks)
    {
        var sb = new StringBuilder();
        sb.Append("elevator-t55xx-");
        for (var i = 0; i < blocks.Count; i++)
        {
            if (i > 0)
                sb.Append('-');
            sb.Append(blocks[i].ToHex());
        }
        sb.Append(".bin");
        return sb.ToString();
    }

    private static string AddSuffixBeforeExtension(string fileName, string suffix)
    {
        var ext = Path.GetExtension(fileName);
        if (string.IsNullOrEmpty(ext))
            return fileName + suffix;

        return fileName[..^ext.Length] + suffix + ext;
    }

    private static void WriteBinDump(string path, IReadOnlyList<T55Block> blocks)
    {
        var bytes = new byte[checked(blocks.Count * 4)];
        for (var i = 0; i < blocks.Count; i++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(i * 4, 4), blocks[i].Value);
        }
        File.WriteAllBytes(path, bytes);
    }

    private bool ExecuteSet(string[] args)
    {
        if (args.Length < 1)
        {
            _output.WriteLine("Usage: set <number>");
            return true;
        }
        if (!int.TryParse(args[0], out var number))
        {
            _output.WriteLine("Error: invalid number");
            return true;
        }
        return ExecuteSetCore(number).GetAwaiter().GetResult();
    }

    private async Task<bool> ExecuteSetCore(int number, bool dryRun = false)
    {
        if (!_rides.HasValue)
        {
            _output.WriteLine("Error: no rides in memory. Run 'read' first.");
            return true;
        }

        var previousRides = _rides.Value;
        if (number < 0 || number > 500)
        {
            _output.WriteLine("Error: number must be in range [0, 500]");
            return true;
        }

        if (dryRun)
        {
            var rideDiff = number - (int)previousRides;
            if (_config.PricePer100.HasValue && rideDiff > 0)
            {
                var price = Math.Ceiling((rideDiff / 100m) * _config.PricePer100.Value * 100) / 100;
                _output.WriteLine($"will cost: {FormatInvariant2(price)} EUR");
            }
            return true;
        }

        _rides = (uint)number;
        var block = TokenBlockUtils.Encode(_rides.Value);

        var success = await _pm3.WriteRideMirrorBlocksAsync(block).ConfigureAwait(false);

        _output.WriteLine(success ? "Success." : "Error: block write/verify failed.");
        if (success)
            _output.WriteLine($"rides remaining: {_rides.Value}");
        var rideDiff2 = (int)_rides.Value - (int)previousRides;
        if (_config.PricePer100.HasValue && rideDiff2 > 0)
        {
            var price = Math.Ceiling((rideDiff2 / 100m) * _config.PricePer100.Value * 100) / 100;
            _output.WriteLine($"cost: {FormatInvariant2(price)} EUR");
        }
        return true;
    }

    private bool ExecuteAdd(string[] args)
    {
        if (args.Length < 1)
        {
            _output.WriteLine("Usage: add <addnum>");
            return true;
        }
        if (!int.TryParse(args[0], out var addNum))
        {
            _output.WriteLine("Error: invalid number");
            return true;
        }
        if (!_rides.HasValue)
        {
            _output.WriteLine("Error: no rides in memory. Run 'read' first.");
            return true;
        }
        var newNumber = (int)_rides.Value + addNum;
        return ExecuteSetCore(newNumber).GetAwaiter().GetResult();
    }

    private bool ExecutePrice(string[] args)
    {
        if (args.Length < 2)
        {
            _output.WriteLine("Usage: price set <number> | price add <addnum>");
            return true;
        }
        var sub = args[0].ToLowerInvariant();
        if (sub == "set" && args.Length >= 2 && int.TryParse(args[1], out var setNum))
            return ExecuteSetCore(setNum, dryRun: true).GetAwaiter().GetResult();
        if (sub == "add" && args.Length >= 2 && int.TryParse(args[1], out var addNum))
        {
            if (!_rides.HasValue)
            {
                _output.WriteLine("Error: no rides in memory. Run 'read' first.");
                return true;
            }
            return ExecuteSetCore((int)_rides.Value + addNum, dryRun: true).GetAwaiter().GetResult();
        }
        _output.WriteLine("Usage: price set <number> | price add <addnum>");
        return true;
    }

    private bool ExecuteMoney(string[] args)
    {
        if (args.Length < 1)
        {
            _output.WriteLine("Usage: money <amount>");
            return true;
        }
        if (!_rides.HasValue)
        {
            _output.WriteLine("Error: no rides in memory. Run 'read' first.");
            return true;
        }
        if (!_config.PricePer100.HasValue)
        {
            _output.WriteLine("Error: pricePer100 not configured. Use 'config pricePer100 <value>'.");
            return true;
        }
        if (!decimal.TryParse(args[0], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount) || amount <= 0)
        {
            _output.WriteLine("Error: invalid amount");
            return true;
        }
        var rides = (int)Math.Floor((amount / _config.PricePer100.Value) * 100);
        _output.WriteLine($"{rides} rides");
        return true;
    }

    private bool ExecuteHelp()
    {
        _output.WriteLine("Commands:");
        _output.WriteLine("  tune          Run signal check and show antenna strength");
        _output.WriteLine("  read [-d]     Read token blocks 5 and 6 and show rides (use -d for full dump)");
        _output.WriteLine("  reset         Reset token using default image and set rides to 0");
        _output.WriteLine("  set <number>  Set rides to token [0-500]");
        _output.WriteLine("  add <addnum>  Add rides to token");
        _output.WriteLine("  price set <number>   Preview cost for set");
        _output.WriteLine("  price add <addnum>   Preview cost for add");
        _output.WriteLine("  money <amount>       Rides purchasable for amount (e.g. 4.00)");
        _output.WriteLine("  config [key value]   Configure (e.g. config pricePer100 4.00)");
        _output.WriteLine("  help");
        _output.WriteLine("  exit");
        return true;
    }

    private bool ExecuteUnknown(string cmd)
    {
        _output.WriteLine($"Unknown command: {cmd}. Type 'help'.");
        return true;
    }
}
