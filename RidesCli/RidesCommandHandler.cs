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

    private const int ResetFirstWritableBlock = 1;
    private const int ResetLastWritableBlock = 6;
    private static readonly TimeSpan ResetRetryDelay = TimeSpan.FromMilliseconds(500);

    private uint? _rides;
    private string? _lastDumpRaw;
    private EncodingSequence? _encodingSequence;

    private sealed record ResetWriteAttemptResult(bool Success, uint Block, string? ErrorMessage = null);

    private sealed record ResetWriteResult(
        bool Success,
        uint? FailedBlock = null,
        string? ErrorMessage = null,
        bool RollbackAttempted = false,
        bool RollbackSucceeded = false,
        IReadOnlyList<string>? RollbackErrors = null);

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
                "tune-probe" => ExecuteTuneProbe(args[1..]),
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

    private bool ExecuteTuneProbe(string[] args)
    {
        if (!TryParseTuneProbeArgs(args, out var label, out var sampleCount, out var timeout, out var error))
        {
            _output.WriteLine(error);
            return true;
        }

        return ExecuteTuneProbeCore(label, sampleCount, timeout).GetAwaiter().GetResult();
    }

    private async Task<bool> ExecuteTuneProbeCore(string label, int sampleCount, TimeSpan timeout)
    {
        var jsonPath = await _pm3.RunLfTuneProbeAsync(label, sampleCount, timeout).ConfigureAwait(false);
        var csvPath = Path.ChangeExtension(jsonPath, ".csv");
        _output.WriteLine($"LF tune probe written:");
        _output.WriteLine($"  json: {jsonPath}");
        _output.WriteLine($"  csv:  {csvPath}");
        _output.WriteLine($"Plot with: python3 debug/plot-lf-tune-probe.py {Path.GetDirectoryName(jsonPath)}");
        return true;
    }

    private static bool TryParseTuneProbeArgs(
        string[] args,
        out string label,
        out int sampleCount,
        out TimeSpan timeout,
        out string error)
    {
        label = string.Empty;
        sampleCount = 60;
        timeout = TimeSpan.FromSeconds(3);
        error = string.Empty;

        if (args.Length < 1 || string.IsNullOrWhiteSpace(args[0]))
        {
            error = "Usage: tune-probe <label> [--samples N] [--timeout SEC]";
            return false;
        }

        label = args[0];
        for (var i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--samples" when i + 1 < args.Length:
                    if (!int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out sampleCount) || sampleCount < 1)
                    {
                        error = "Error: --samples must be a positive integer";
                        return false;
                    }
                    break;
                case "--timeout" when i + 1 < args.Length:
                    if (!double.TryParse(args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) || seconds <= 0)
                    {
                        error = "Error: --timeout must be a positive number of seconds";
                        return false;
                    }
                    timeout = TimeSpan.FromSeconds(seconds);
                    break;
                default:
                    error = "Usage: tune-probe <label> [--samples N] [--timeout SEC]";
                    return false;
            }
        }

        return true;
    }

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
        if (!TryParseResetArgs(args, out var profile, out var error))
        {
            _output.WriteLine(error);
            return true;
        }

        return ExecuteResetCore(profile!).GetAwaiter().GetResult();
    }

    private static bool TryParseResetArgs(string[] args, out TokenIdentityProfile? profile, out string error)
    {
        profile = null;
        error = string.Empty;
        string? profileName = null;

        for (var i = 0; i < args.Length; i++)
        {
            if ((args[i] == "--sequence" || args[i] == "--profile") && i + 1 < args.Length)
            {
                profileName = args[++i];
                continue;
            }

            error = FormatResetUsage();
            return false;
        }

        if (profileName is null)
        {
            error = FormatResetUsage();
            return false;
        }

        if (!TokenIdentityProfiles.TryGetByFriendlyName(profileName, out profile) || profile is null)
        {
            error = $"Error: unknown identity profile '{profileName}'. Known resettable profiles: {TokenIdentityProfiles.FormatResettableFriendlyNames()}";
            return false;
        }

        if (!profile.CanReset)
        {
            error = $"Error: identity profile '{profileName}' has no reset image and is not resettable.";
            return false;
        }

        return true;
    }

    private static string FormatResetUsage() =>
        $"Usage: reset --sequence|--profile <name>   Reset token using a resettable identity profile (known: {TokenIdentityProfiles.FormatResettableFriendlyNames()})";

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
            var (block5Hex, block6Hex) = await _pm3.ReadRideMirrorBlocksAsync();
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
                    _encodingSequence = result.SourceBlock is T55Block sourceBlock
                        && EncodingSequences.TryGetSequenceFromBlock(sourceBlock, out var sequence)
                        ? sequence
                        : null;
                    _output.WriteLine($"rides remaining: {_rides.Value}");
                    if (_encodingSequence is not null)
                        _output.WriteLine($"sequence: {_encodingSequence.FriendlyName}");
                    return true;
                case RideReadStatus.UnknownEncodingFamily:
                    _rides = null;
                    _encodingSequence = null;
                    await HandleUnknownEncodingFamilyAsync(block5).ConfigureAwait(false);
                    return true;
                default:
                    _rides = null;
                    _encodingSequence = null;
                    _lastDumpRaw = null;
                    await HandleInvalidBlockFormatAsync(block5).ConfigureAwait(false);
                    return true;
            }
        }
        catch (Exception ex)
        {
            _rides = null;
            _encodingSequence = null;
            _lastDumpRaw = null;
            _output.WriteLine($"Error: {ex.Message}");
            return true;
        }
    }

    private async Task<bool> ExecuteResetCore(TokenIdentityProfile profile)
    {
        _rides = null;
        _encodingSequence = null;

        IReadOnlyList<T55Block> currentBlocks;
        try
        {
            currentBlocks = await ReadResetWritableBlocksAsync().ConfigureAwait(false);
            var block5Hex = currentBlocks[5].ToHex();
            var block6Hex = currentBlocks[6].ToHex();
            var describe = RideBlockResolver.Resolve(currentBlocks[5], currentBlocks[6]);

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

        if (!PromptForYesNo($"Overwrite token with reset image and set rides to 0 using profile '{profile.FriendlyName}'? [y/N]"))
        {
            _output.WriteLine("Cancelled.");
            return true;
        }

        var resetBlocks = ResetPage0BlocksLoader.Load(profile);
        var zeroBlock = profile.RideSequence.Encode(0);
        resetBlocks[5] = zeroBlock;
        resetBlocks[6] = zeroBlock;

        var targetBlockNumbers = IsSameResetProfile(currentBlocks, resetBlocks, profile)
            ? new[] { 5u, 6u }
            : Enumerable.Range(ResetFirstWritableBlock, ResetLastWritableBlock - ResetFirstWritableBlock + 1)
                .Select(static block => (uint)block)
                .ToArray();

        if (targetBlockNumbers.Length == 2 && targetBlockNumbers[0] == 5 && targetBlockNumbers[1] == 6)
            _output.WriteLine("Token already matches requested reset identity; resetting ride blocks only.");

        var result = await WriteResetBlocksSafelyAsync(currentBlocks, resetBlocks, targetBlockNumbers).ConfigureAwait(false);

        if (result.Success)
        {
            _output.WriteLine("Success.");
            _rides = 0;
            _encodingSequence = profile.RideSequence;
            _output.WriteLine("rides remaining: 0");
            return true;
        }

        _output.WriteLine($"Error: block {result.FailedBlock} write/verify failed. {result.ErrorMessage}");
        if (result.RollbackAttempted)
        {
            if (result.RollbackSucceeded)
            {
                _output.WriteLine("Rollback to previous block values succeeded.");
            }
            else
            {
                _output.WriteLine("Warning: rollback to previous block values was incomplete.");
                foreach (var rollbackError in result.RollbackErrors ?? [])
                    _output.WriteLine($"  {rollbackError}");
            }
        }

        return true;
    }

    private async Task<IReadOnlyList<T55Block>> ReadResetWritableBlocksAsync(CancellationToken ct = default)
    {
        var blocks = Enumerable.Repeat(new T55Block(0), 8).ToList();
        for (uint block = ResetFirstWritableBlock; block <= ResetLastWritableBlock; block++)
            blocks[(int)block] = T55Block.FromHex(await _pm3.ReadPage0BlockAsync(block, ct).ConfigureAwait(false));
        return blocks;
    }

    private static bool IsSameResetProfile(
        IReadOnlyList<T55Block> currentBlocks,
        IReadOnlyList<T55Block> resetBlocks,
        TokenIdentityProfile profile)
    {
        for (var block = 1; block <= 4; block++)
        {
            if (currentBlocks[block].Value != resetBlocks[block].Value)
                return false;
        }

        return IsBlockFromSequence(currentBlocks[5], profile.RideSequence)
            && IsBlockFromSequence(currentBlocks[6], profile.RideSequence);
    }

    private static bool IsBlockFromSequence(T55Block block, EncodingSequence sequence) =>
        EncodingSequences.TryGetSequenceFromBlock(block, out var found) && ReferenceEquals(found, sequence);

    private async Task<ResetWriteResult> WriteResetBlocksSafelyAsync(
        IReadOnlyList<T55Block> originalBlocks,
        IReadOnlyList<T55Block> targetBlocks,
        IReadOnlyList<uint> targetBlockNumbers,
        CancellationToken ct = default)
    {
        foreach (var block in targetBlockNumbers)
        {
            ValidateResetWritableBlock(block);
            var target = targetBlocks[(int)block];
            if (originalBlocks[(int)block].Value == target.Value)
                continue;

            var write = await WriteAndVerifyBlockWithRetryAsync(block, target, ct).ConfigureAwait(false);
            if (write.Success)
                continue;

            var rollback = await RollBackResetBlocksAsync(originalBlocks, ct).ConfigureAwait(false);
            return new ResetWriteResult(
                Success: false,
                FailedBlock: block,
                ErrorMessage: write.ErrorMessage,
                RollbackAttempted: true,
                RollbackSucceeded: rollback.Count == 0,
                RollbackErrors: rollback);
        }

        return new ResetWriteResult(Success: true);
    }

    private async Task<IReadOnlyList<string>> RollBackResetBlocksAsync(
        IReadOnlyList<T55Block> originalBlocks,
        CancellationToken ct = default)
    {
        var errors = new List<string>();
        for (uint block = ResetFirstWritableBlock; block <= ResetLastWritableBlock; block++)
        {
            ValidateResetWritableBlock(block);
            try
            {
                var current = T55Block.FromHex(await _pm3.ReadPage0BlockAsync(block, ct).ConfigureAwait(false));
                var original = originalBlocks[(int)block];
                if (current.Value == original.Value)
                    continue;

                var rollback = await WriteAndVerifyBlockWithRetryAsync(block, original, ct).ConfigureAwait(false);
                if (!rollback.Success)
                    errors.Add($"block {block}: {rollback.ErrorMessage}");
            }
            catch (Exception ex)
            {
                errors.Add($"block {block}: {ex.Message}");
            }
        }

        return errors;
    }

    private async Task<ResetWriteAttemptResult> WriteAndVerifyBlockWithRetryAsync(
        uint block,
        T55Block target,
        CancellationToken ct = default)
    {
        ValidateResetWritableBlock(block);
        string? lastError = null;

        for (var attempt = 0; attempt < 2; attempt++)
        {
            if (attempt > 0)
                await Task.Delay(ResetRetryDelay, ct).ConfigureAwait(false);

            try
            {
                await _pm3.WritePage0BlockAsync(block, target, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                lastError = $"write attempt {attempt + 1} threw: {ex.Message}";
                continue;
            }

            try
            {
                var readBack = await _pm3.ReadPage0BlockAsync(block, ct).ConfigureAwait(false);
                if (string.Equals(readBack, target.ToHex(), StringComparison.OrdinalIgnoreCase))
                    return new ResetWriteAttemptResult(true, block);

                lastError = $"verify attempt {attempt + 1} read {readBack}, expected {target.ToHex()}";
            }
            catch (Exception ex)
            {
                lastError = $"verify attempt {attempt + 1} threw: {ex.Message}";
            }
        }

        return new ResetWriteAttemptResult(false, block, lastError ?? "unknown write/verify failure");
    }

    private static void ValidateResetWritableBlock(uint block)
    {
        if (block is < ResetFirstWritableBlock or > ResetLastWritableBlock)
            throw new ArgumentOutOfRangeException(nameof(block), "Reset writes are only allowed for page 0 blocks 1..6.");
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

    private async Task HandleUnknownEncodingFamilyAsync(T55Block block5)
    {
        _output.WriteLine($"Unknown encoding family detected in page 0 block 5: {block5.ToHex()}");
        await SaveUndecodableTokenDumpAsync(
            "Error: could not decode rides because the token uses an unknown encoding family.").ConfigureAwait(false);
    }

    private async Task HandleInvalidBlockFormatAsync(T55Block block5)
    {
        _output.WriteLine($"Invalid ride block format detected in page 0 block 5: {block5.ToHex()}");
        await SaveUndecodableTokenDumpAsync(
            "Error: could not decode rides because the token uses an invalid block format.").ConfigureAwait(false);
    }

    private async Task SaveUndecodableTokenDumpAsync(string finalErrorMessage)
    {
        var blocks = await ReadPage0BlocksAsync();
        var dumpPath = SaveTokenDump(blocks, "UNKNOWN");

        var ridesLabel = PromptForKnownRideCountLabel();
        dumpPath = UpdateSavedTokenDumpPath(dumpPath, blocks, ridesLabel);

        _output.WriteLine($"Saved token dump to '{Path.GetFullPath(dumpPath)}'.");
        _output.WriteLine(finalErrorMessage);
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

        if (_encodingSequence is null)
        {
            _output.WriteLine("Error: no encoding sequence in memory. Run 'read' first.");
            return true;
        }

        if (number < _encodingSequence.MinRides || number > _encodingSequence.MaxRides)
        {
            _output.WriteLine(
                $"Error: number must be in range [{_encodingSequence.MinRides}, {_encodingSequence.MaxRides}] for sequence '{_encodingSequence.FriendlyName}'");
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

        var targetRides = (uint)number;
        var block = TokenBlockUtils.Encode(targetRides, _encodingSequence);

        var success = await _pm3.WriteRideMirrorBlocksAsync(block).ConfigureAwait(false);

        _output.WriteLine(success ? "Success." : "Error: block write/verify failed.");
        if (success)
        {
            _rides = targetRides;
            _output.WriteLine($"rides remaining: {_rides.Value}");

            var rideDiff2 = (int)targetRides - (int)previousRides;
            if (_config.PricePer100.HasValue && rideDiff2 > 0)
            {
                var price = Math.Ceiling((rideDiff2 / 100m) * _config.PricePer100.Value * 100) / 100;
                _output.WriteLine($"cost: {FormatInvariant2(price)} EUR");
            }
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
        _output.WriteLine("  tune-probe <label> [--samples N] [--timeout SEC]");
        _output.WriteLine("                TEMPORARY: record LF tune samples to debug/lf-tune-probes/");
        _output.WriteLine("  read [-d]     Read token blocks 5 and 6 and show rides (use -d for full dump)");
        _output.WriteLine($"  reset --sequence|--profile <name>   Reset token using a resettable identity profile (known: {TokenIdentityProfiles.FormatResettableFriendlyNames()})");
        _output.WriteLine("  set <number>  Set rides to token [0-500, except Earth 0-255]");
        _output.WriteLine("  add <addnum>  Add rides to token [0-500, except Earth 0-255]");
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
