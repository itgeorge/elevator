using System.Globalization;
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

    private uint? _rides;
    private string? _lastDumpRaw;

    public RidesCommandHandler(IRidesPm3Api pm3, IRidesOutput output, RidesConfig config)
    {
        _pm3 = pm3 ?? throw new ArgumentNullException(nameof(pm3));
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _config = config ?? throw new ArgumentNullException(nameof(config));
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
                "read" => ExecuteRead(args[1..]),
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
            _output.WriteLine($"pricePer100 = {price:F2}");
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

    private bool ExecuteRead(string[] args)
    {
        var showDump = args.Length > 0 && args[0] == "-d";
        return ExecuteReadCore(showDump).GetAwaiter().GetResult();
    }

    private async Task<bool> ExecuteReadCore(bool showDump)
    {
        try
        {
            var mv = await _pm3.GetSignalStrengthMvAsync();
            _output.WriteLine($"signal strength: {mv} mV");

            var dump = await _pm3.DumpAsync();
            _lastDumpRaw = dump;
            if (showDump)
                _output.WriteLine(dump);

            var block5Hex = await _pm3.ReadPage0BlockAsync(5);
            var block5 = T55Block.FromHex(block5Hex);
            var rides = TokenBlockUtils.Decode(block5);
            _rides = rides;
            _output.WriteLine($"rides remaining: {rides}");
            return true;
        }
        catch (Exception ex)
        {
            _rides = null;
            _lastDumpRaw = null;
            _output.WriteLine($"Error: {ex.Message}");
            return true;
        }
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
                _output.WriteLine($"will cost: {price:F2} EUR");
            }
            return true;
        }

        _rides = (uint)number;
        var block = TokenBlockUtils.Encode(_rides.Value);

        bool block5Confirmed = false, block6Confirmed = false;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (!block5Confirmed)
                await _pm3.WritePage0BlockAsync(5, block);
            if (!block6Confirmed)
                await _pm3.WritePage0BlockAsync(6, block);

            var read5 = await _pm3.ReadPage0BlockAsync(5);
            var read6 = await _pm3.ReadPage0BlockAsync(6);
            block5Confirmed = read5 == block.ToHex();
            block6Confirmed = read6 == block.ToHex();

            if (block5Confirmed && block6Confirmed)
                break;
        }

        var success = block5Confirmed && block6Confirmed;

        var finalDump = await _pm3.DumpAsync();
        _lastDumpRaw = finalDump;
        _output.WriteLine(finalDump);

        _output.WriteLine(success ? "Success." : "Error: block write/verify failed.");
        if (success)
            _output.WriteLine($"rides remaining: {_rides.Value}");
        var rideDiff2 = (int)_rides.Value - (int)previousRides;
        if (_config.PricePer100.HasValue && rideDiff2 > 0)
        {
            var price = Math.Ceiling((rideDiff2 / 100m) * _config.PricePer100.Value * 100) / 100;
            _output.WriteLine($"cost: {price:F2} EUR");
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
        _output.WriteLine("  read [-d]     Detect and read token, show signal and rides (use -d for dump)");
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
