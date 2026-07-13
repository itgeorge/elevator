using System.Buffers.Binary;
using NUnit.Framework;
using RidesCli;
using Tokens;

namespace RidesCli.Tests;

public class RidesCommandHandlerTests
{
    [Test]
    public void Config_pricePer100_sets_value()
    {
        var output = new StringBuilderRidesOutput();
        var config = new RidesConfig();
        var pm3 = new FakeRidesPm3Api();
        var handler = new RidesCommandHandler(pm3, output, config);

        handler.Execute(["config", "pricePer100", "4.00"]);

        Assert.That(config.PricePer100, Is.EqualTo(4.00m));
    }

    [Test]
    public void Config_pricePer100_accepts_decimal_value()
    {
        var output = new StringBuilderRidesOutput();
        var config = new RidesConfig();
        var pm3 = new FakeRidesPm3Api();
        var handler = new RidesCommandHandler(pm3, output, config);

        handler.Execute(["config", "pricePer100", "24.50"]);

        Assert.That(config.PricePer100, Is.EqualTo(24.50m));
    }

    [Test]
    public void Config_pricePer100_invalid_format_prints_error()
    {
        var output = new StringBuilderRidesOutput();
        var config = new RidesConfig();
        var pm3 = new FakeRidesPm3Api();
        var handler = new RidesCommandHandler(pm3, output, config);

        handler.Execute(["config", "pricePer100", "not-a-number"]);

        Assert.That(output.Lines, Has.Some.Contains("error").Or.Some.Contains("invalid"));
    }

    [Test]
    public void TuneProbe_writes_probe_paths()
    {
        var output = new StringBuilderRidesOutput();
        var handler = new RidesCommandHandler(new FakeRidesPm3Api(), output, new RidesConfig());

        handler.Execute(["tune-probe", "fake-token-center"]);

        Assert.That(output.Lines, Has.Some.Contains("LF tune probe written:"));
        Assert.That(output.Lines, Has.Some.Contains("fake-token-center"));
    }

    [Test]
    public void Tune_shows_signal_strength_only()
    {
        var output = new StringBuilderRidesOutput();
        var pm3 = FakeRidesPm3Api.WithRides(73);
        pm3.SignalStrengthMv = 420;
        var handler = new RidesCommandHandler(pm3, output, new RidesConfig());

        handler.Execute(["tune"]);

        Assert.That(output.Lines, Has.Some.Matches(@"signal strength: 420 mV"));
        Assert.That(output.Lines, Has.None.Matches(@"rides remaining:"));
    }

    [Test]
    public void Tune_with_args_prints_usage()
    {
        var output = new StringBuilderRidesOutput();
        var handler = new RidesCommandHandler(new FakeRidesPm3Api(), output, new RidesConfig());

        handler.Execute(["tune", "extra"]);

        Assert.That(output.Lines, Has.Some.EqualTo("Usage: tune"));
    }

    [Test]
    public void Reset_without_sequence_prints_usage()
    {
        var output = new StringBuilderRidesOutput();
        var pm3 = FakeRidesPm3Api.WithRides(73);
        var handler = new RidesCommandHandler(pm3, output, new RidesConfig(), new ScriptedRidesInput("y"));

        handler.Execute(["reset"]);

        Assert.That(output.Lines, Has.Some.Contains("Usage: reset --sequence"));
        Assert.That(output.Lines, Has.None.EqualTo("Success."));
        Assert.That(pm3.GetRides(), Is.EqualTo(73u));
    }

    [Test]
    public void Reset_with_unknown_sequence_prints_error()
    {
        var output = new StringBuilderRidesOutput();
        var pm3 = FakeRidesPm3Api.WithRides(73);
        var handler = new RidesCommandHandler(pm3, output, new RidesConfig(), new ScriptedRidesInput("y"));

        handler.Execute(["reset", "--sequence", "pluto"]);

        Assert.That(output.Lines, Has.Some.Contains("unknown encoding sequence 'pluto'"));
        Assert.That(output.Lines, Has.None.EqualTo("Success."));
    }

    [Test]
    public void Reset_without_read_prompts_and_writes_default_image_with_zero_rides()
    {
        var output = new StringBuilderRidesOutput();
        var pm3 = FakeRidesPm3Api.WithRides(73);
        var input = new ScriptedRidesInput("y");
        var handler = new RidesCommandHandler(pm3, output, new RidesConfig(), input);

        handler.Execute(["reset", "--sequence", "mercury"]);

        Assert.That(output.Lines, Has.None.Matches(@"signal strength:"));
        Assert.That(output.Lines, Has.Some.EqualTo("current token rides: 73"));
        Assert.That(output.Lines, Has.Some.EqualTo("Success."));
        Assert.That(output.Lines, Has.Some.EqualTo("rides remaining: 0"));

        Assert.That(pm3.GetBlockHex(1), Is.EqualTo("9BFE0062"));
        Assert.That(pm3.GetBlockHex(2), Is.EqualTo("5BA4A3DE"));
        Assert.That(pm3.GetBlockHex(3), Is.EqualTo("D5D1D713"));
        Assert.That(pm3.GetBlockHex(4), Is.EqualTo("D5D1D713"));
        Assert.That(pm3.GetBlockHex(5), Is.EqualTo(TokenBlockUtils.Encode(0, EncodingSequences.Mercury).ToHex()));
        Assert.That(pm3.GetBlockHex(6), Is.EqualTo(TokenBlockUtils.Encode(0, EncodingSequences.Mercury).ToHex()));
    }

    [Test]
    public void Reset_same_sequence_only_writes_ride_blocks_to_zero()
    {
        var output = new StringBuilderRidesOutput();
        var pm3 = FakeRidesPm3Api.WithSequenceRides(EncodingSequences.Mercury, 73);
        var handler = new RidesCommandHandler(pm3, output, new RidesConfig(), new ScriptedRidesInput("y"));

        handler.Execute(["reset", "--sequence", "mercury"]);

        Assert.That(output.Lines, Has.Some.Contains("resetting ride blocks only"));
        Assert.That(output.Lines, Has.Some.EqualTo("Success."));
        Assert.That(pm3.WrittenBlocks, Is.EqualTo(new uint[] { 5, 6 }));
        Assert.That(pm3.WriteAndVerifyPage0BlocksCallCount, Is.EqualTo(0));
        Assert.That(pm3.GetBlockHex(1), Is.EqualTo("9BFE0062"));
        Assert.That(pm3.GetBlockHex(2), Is.EqualTo("5BA4A3DE"));
        Assert.That(pm3.GetBlockHex(3), Is.EqualTo("D5D1D713"));
        Assert.That(pm3.GetBlockHex(4), Is.EqualTo("D5D1D713"));
        Assert.That(pm3.GetBlockHex(5), Is.EqualTo(EncodingSequences.Mercury.Encode(0).ToHex()));
        Assert.That(pm3.GetBlockHex(6), Is.EqualTo(EncodingSequences.Mercury.Encode(0).ToHex()));
    }

    [Test]
    public void Reset_cross_sequence_writes_blocks_individually_without_batch_writer()
    {
        var output = new StringBuilderRidesOutput();
        var pm3 = FakeRidesPm3Api.WithSequenceRides(EncodingSequences.Mercury, 73);
        var handler = new RidesCommandHandler(pm3, output, new RidesConfig(), new ScriptedRidesInput("y"));

        handler.Execute(["reset", "--sequence", "venus"]);

        Assert.That(output.Lines, Has.Some.EqualTo("Success."));
        Assert.That(pm3.WrittenBlocks, Is.EqualTo(new uint[] { 1, 2, 3, 4, 5, 6 }));
        Assert.That(pm3.WriteAndVerifyPage0BlocksCallCount, Is.EqualTo(0));
        Assert.That(pm3.GetBlockHex(1), Is.EqualTo("43FE0062"));
        Assert.That(pm3.GetBlockHex(2), Is.EqualTo("5BA494A3"));
        Assert.That(pm3.GetBlockHex(3), Is.EqualTo("D6D1C733"));
        Assert.That(pm3.GetBlockHex(4), Is.EqualTo("D6D1C733"));
        Assert.That(pm3.GetBlockHex(5), Is.EqualTo(EncodingSequences.Venus.Encode(0).ToHex()));
        Assert.That(pm3.GetBlockHex(6), Is.EqualTo(EncodingSequences.Venus.Encode(0).ToHex()));
    }

    [Test]
    public void Reset_failure_retries_once_then_rolls_back_previous_values()
    {
        var output = new StringBuilderRidesOutput();
        var pm3 = FakeRidesPm3Api.WithSequenceRides(EncodingSequences.Mercury, 73);
        pm3.RemainingWriteFailuresByBlock[2] = 2;
        var handler = new RidesCommandHandler(pm3, output, new RidesConfig(), new ScriptedRidesInput("y"));

        handler.Execute(["reset", "--sequence", "venus"]);

        Assert.That(output.Lines, Has.Some.Contains("block 2 write/verify failed"));
        Assert.That(output.Lines, Has.Some.Contains("Rollback to previous block values succeeded"));
        Assert.That(output.Lines, Has.None.EqualTo("Success."));
        Assert.That(pm3.GetBlockHex(1), Is.EqualTo("9BFE0062"));
        Assert.That(pm3.GetBlockHex(2), Is.EqualTo("5BA4A3DE"));
        Assert.That(pm3.GetBlockHex(3), Is.EqualTo("D5D1D713"));
        Assert.That(pm3.GetBlockHex(4), Is.EqualTo("D5D1D713"));
        Assert.That(pm3.GetBlockHex(5), Is.EqualTo(EncodingSequences.Mercury.Encode(73).ToHex()));
        Assert.That(pm3.GetBlockHex(6), Is.EqualTo(EncodingSequences.Mercury.Encode(73).ToHex()));
        Assert.That(pm3.WrittenBlocks, Is.EqualTo(new uint[] { 1, 2, 2, 1 }));
    }

    [Test]
    public void Reset_when_no_token_detected_prints_error_and_signal_strength()
    {
        var output = new StringBuilderRidesOutput();
        var pm3 = new FakeRidesPm3Api();
        pm3.RemoveToken();
        var handler = new RidesCommandHandler(pm3, output, new RidesConfig(), new ScriptedRidesInput("y"));

        handler.Execute(["reset", "--sequence", "mercury"]);

        Assert.That(output.Lines, Has.None.Matches(@"signal strength:"));
        Assert.That(output.Lines, Has.Some.Contains("no token detected"));
        Assert.That(output.Lines, Has.None.EqualTo("Success."));
    }

    [Test]
    public void Reset_with_unknown_family_prompts_and_overwrites_token()
    {
        var output = new StringBuilderRidesOutput();
        var pm3 = FakeRidesPm3Api.WithUnknownFamilyBlock5();
        var handler = new RidesCommandHandler(pm3, output, new RidesConfig(), new ScriptedRidesInput("y"));

        handler.Execute(["reset", "--sequence", "mercury"]);

        Assert.That(output.Lines, Has.Some.Contains("unknown encoding family"));
        Assert.That(output.Lines, Has.Some.EqualTo("Success."));
        Assert.That(pm3.GetBlockHex(5), Is.EqualTo(TokenBlockUtils.Encode(0, EncodingSequences.Mercury).ToHex()));
        Assert.That(pm3.GetBlockHex(6), Is.EqualTo(TokenBlockUtils.Encode(0, EncodingSequences.Mercury).ToHex()));
    }

    [Test]
    public void Reset_cancelled_does_not_modify_token()
    {
        var output = new StringBuilderRidesOutput();
        var pm3 = FakeRidesPm3Api.WithRides(73);
        var handler = new RidesCommandHandler(pm3, output, new RidesConfig(), new ScriptedRidesInput("n"));

        handler.Execute(["reset", "--sequence", "mercury"]);

        Assert.That(output.Lines, Has.Some.EqualTo("Cancelled."));
        Assert.That(pm3.GetRides(), Is.EqualTo(73u));
        Assert.That(pm3.GetBlockHex(1), Is.EqualTo("00000000"));
        Assert.That(pm3.GetBlockHex(2), Is.EqualTo("00000000"));
        Assert.That(pm3.GetBlockHex(3), Is.EqualTo("00000000"));
        Assert.That(pm3.GetBlockHex(4), Is.EqualTo("00000000"));
    }

    [Test]
    public void Read_matching_blocks_decodes_rides_without_tune_or_dump()
    {
        var output = new StringBuilderRidesOutput();
        var pm3 = FakeRidesPm3Api.WithRides(73);
        var handler = new RidesCommandHandler(pm3, output, new RidesConfig());

        handler.Execute(["read"]);

        Assert.That(output.Lines, Has.Some.Matches(@"rides remaining: 73"));
        Assert.That(output.Lines, Has.None.Matches(@"signal strength:"));
        Assert.That(pm3.TuneCallCount, Is.EqualTo(0));
        Assert.That(pm3.DumpCallCount, Is.EqualTo(0));
    }

    [Test]
    public void Read_when_decode_fails_unloads_and_prints_error()
    {
        var output = new StringBuilderRidesOutput();
        var pm3 = FakeRidesPm3Api.WithInvalidBlock5();
        var handler = new RidesCommandHandler(pm3, output, new RidesConfig());

        handler.Execute(["read"]);

        Assert.That(output.Lines, Has.Some.Contains("invalid block format"));
        // Subsequent set should fail with "no rides in memory"
        handler.Execute(["set", "50"]);
        Assert.That(output.Lines, Has.Some.Contains("no rides in memory"));
    }

    [Test]
    public void Read_with_unknown_encoding_family_saves_dump_with_unknown_suffix_and_unloads()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var output = new StringBuilderRidesOutput();
            var pm3 = FakeRidesPm3Api.WithUnknownFamilyBlock5();
            var config = new RidesConfig { DumpDirectory = tempDir };
            var input = new ScriptedRidesInput(string.Empty);
            var handler = new RidesCommandHandler(pm3, output, config, input);

            handler.Execute(["read"]);

            var files = Directory.GetFiles(tempDir, "*.bin");
            Assert.That(files, Has.Length.EqualTo(1));
            Assert.That(Path.GetFileName(files[0]), Does.EndWith("--rides-UNKNOWN.bin"));
            Assert.That(new FileInfo(files[0]).Length, Is.EqualTo(32));
            Assert.That(output.Lines, Has.Some.Contains("Unknown encoding family"));
            Assert.That(output.Lines, Has.Some.Contains("Saved token dump to"));

            handler.Execute(["set", "50"]);
            Assert.That(output.Lines, Has.Some.Contains("no rides in memory"));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Test]
    public void Read_with_unknown_encoding_family_saves_dump_with_known_ride_suffix()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var output = new StringBuilderRidesOutput();
            var pm3 = FakeRidesPm3Api.WithUnknownFamilyBlock5();
            var config = new RidesConfig { DumpDirectory = tempDir };
            var input = new ScriptedRidesInput("137");
            var handler = new RidesCommandHandler(pm3, output, config, input);

            handler.Execute(["read"]);

            var files = Directory.GetFiles(tempDir, "*.bin");
            Assert.That(files, Has.Length.EqualTo(1));
            Assert.That(Path.GetFileName(files[0]), Does.EndWith("--rides-137.bin"));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Test]
    public void Read_with_unknown_encoding_family_preserves_dump_when_token_changes_during_prompt()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var output = new StringBuilderRidesOutput();
            var pm3 = FakeRidesPm3Api.WithUnknownFamilyBlock5();
            var config = new RidesConfig { DumpDirectory = tempDir };
            var input = new ScriptedRidesInput("137")
            {
                BeforeReadLine = () => pm3.SimulateNewToken(42)
            };
            var handler = new RidesCommandHandler(pm3, output, config, input);

            handler.Execute(["read"]);

            var files = Directory.GetFiles(tempDir, "*.bin");
            Assert.That(files, Has.Length.EqualTo(1));
            Assert.That(Path.GetFileName(files[0]), Does.EndWith("--rides-137.bin"));

            var bytes = File.ReadAllBytes(files[0]);
            Assert.That(BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(20, 4)), Is.EqualTo(0xDEAD1234u));
            Assert.That(BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(24, 4)), Is.EqualTo(0xDEAD1234u));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Test]
    public void Read_with_unknown_encoding_family_reprompts_until_valid_ride_count()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var output = new StringBuilderRidesOutput();
            var pm3 = FakeRidesPm3Api.WithUnknownFamilyBlock5();
            var config = new RidesConfig { DumpDirectory = tempDir };
            var input = new ScriptedRidesInput("oops", "137");
            var handler = new RidesCommandHandler(pm3, output, config, input);

            handler.Execute(["read"]);

            var files = Directory.GetFiles(tempDir, "*.bin");
            Assert.That(files, Has.Length.EqualTo(1));
            Assert.That(Path.GetFileName(files[0]), Does.EndWith("--rides-137.bin"));
            Assert.That(output.Lines, Has.Some.Contains("invalid ride count"));
            Assert.That(output.Lines.Count(line => line.Contains("Enter known ride count")), Is.EqualTo(2));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Test]
    public void Read_with_unknown_encoding_family_dump_contains_current_page0_blocks()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var output = new StringBuilderRidesOutput();
            var pm3 = FakeRidesPm3Api.WithUnknownFamilyBlock5();
            var config = new RidesConfig { DumpDirectory = tempDir };
            var input = new ScriptedRidesInput(string.Empty);
            var handler = new RidesCommandHandler(pm3, output, config, input);

            handler.Execute(["read"]);

            var files = Directory.GetFiles(tempDir, "*.bin");
            Assert.That(files, Has.Length.EqualTo(1));

            var bytes = File.ReadAllBytes(files[0]);
            Assert.That(bytes.Length, Is.EqualTo(32));
            Assert.That(BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(0, 4)), Is.EqualTo(0u));
            Assert.That(BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(20, 4)), Is.EqualTo(0xDEAD1234u));
            Assert.That(BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(24, 4)), Is.EqualTo(0xDEAD1234u));
            Assert.That(BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(28, 4)), Is.EqualTo(0u));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Test]
    public void Read_with_d_flag_and_unknown_encoding_family_shows_dump_before_prompting()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var output = new StringBuilderRidesOutput();
            var pm3 = FakeRidesPm3Api.WithUnknownFamilyBlock5();
            pm3.DumpResult = "raw dump output";
            var config = new RidesConfig { DumpDirectory = tempDir };
            var input = new ScriptedRidesInput(string.Empty);
            var handler = new RidesCommandHandler(pm3, output, config, input);

            handler.Execute(["read", "-d"]);

            Assert.That(output.Lines, Has.Some.EqualTo("raw dump output"));
            Assert.That(output.Lines, Has.Some.Contains("Unknown encoding family"));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Test]
    public void Read_mismatch_only_block6_valid_loads_rides_from_block6()
    {
        var output = new StringBuilderRidesOutput();
        var pm3 = FakeRidesPm3Api.WithBlocks5And6(new T55Block(0xCCC70000), TokenBlockUtils.Encode(42, EncodingSequences.Mercury));
        var handler = new RidesCommandHandler(pm3, output, new RidesConfig());

        handler.Execute(["read"]);

        Assert.That(output.Lines, Has.Some.EqualTo("Warning: blocks 5 and 6 differ; using block 6."));
        Assert.That(output.Lines, Has.Some.Matches(@"rides remaining: 42"));
    }

    [Test]
    public void Read_mismatch_both_valid_prefers_block5()
    {
        var output = new StringBuilderRidesOutput();
        var pm3 = FakeRidesPm3Api.WithMismatchedRides(73, 80);
        var handler = new RidesCommandHandler(pm3, output, new RidesConfig());

        handler.Execute(["read"]);

        Assert.That(output.Lines, Has.Some.EqualTo("Warning: blocks 5 and 6 differ; using block 5 (73 rides)."));
        Assert.That(output.Lines, Has.Some.Matches(@"rides remaining: 73"));
        Assert.That(pm3.DumpCallCount, Is.EqualTo(0));
    }

    [Test]
    public void Read_with_d_flag_shows_dump()
    {
        var output = new StringBuilderRidesOutput();
        var pm3 = FakeRidesPm3Api.WithRides(73);
        pm3.DumpResult = "raw dump output";
        var handler = new RidesCommandHandler(pm3, output, new RidesConfig());

        handler.Execute(["read", "-d"]);

        Assert.That(output.Lines, Has.Some.EqualTo("raw dump output"));
        Assert.That(pm3.DumpCallCount, Is.EqualTo(1));
        Assert.That(pm3.TuneCallCount, Is.EqualTo(0));
    }

    [Test]
    public void Set_without_read_prints_error()
    {
        var output = new StringBuilderRidesOutput();
        var handler = new RidesCommandHandler(new FakeRidesPm3Api(), output, new RidesConfig());

        handler.Execute(["set", "100"]);

        Assert.That(output.Lines, Has.Some.Contains("no rides in memory"));
    }

    [Test]
    public void Set_number_out_of_range_prints_error()
    {
        var output = new StringBuilderRidesOutput();
        var pm3 = FakeRidesPm3Api.WithRides(73);
        var handler = new RidesCommandHandler(pm3, output, new RidesConfig());
        handler.Execute(["read"]);

        handler.Execute(["set", "666"]);

        Assert.That(output.Lines, Has.Some.Contains("[0, 500]"));
    }

    [Test]
    public void Set_501_prints_error()
    {
        var output = new StringBuilderRidesOutput();
        var pm3 = FakeRidesPm3Api.WithRides(73);
        var handler = new RidesCommandHandler(pm3, output, new RidesConfig());
        handler.Execute(["read"]);

        handler.Execute(["set", "501"]);

        Assert.That(output.Lines, Has.Some.Contains("[0, 500]"));
        Assert.That(pm3.GetRides(), Is.EqualTo(73u)); // image unchanged
    }

    [Test]
    public void Set_negative_prints_error()
    {
        var output = new StringBuilderRidesOutput();
        var pm3 = FakeRidesPm3Api.WithRides(73);
        var handler = new RidesCommandHandler(pm3, output, new RidesConfig());
        handler.Execute(["read"]);

        handler.Execute(["set", "-1"]);

        Assert.That(output.Lines, Has.Some.Contains("[0, 500]"));
        Assert.That(pm3.GetRides(), Is.EqualTo(73u)); // image unchanged
    }

    [Test]
    public void Set_500_succeeds()
    {
        var output = new StringBuilderRidesOutput();
        var pm3 = FakeRidesPm3Api.WithRides(73);
        var handler = new RidesCommandHandler(pm3, output, new RidesConfig());
        handler.Execute(["read"]);

        handler.Execute(["set", "500"]);

        Assert.That(output.Lines, Has.Some.Contains("Success"));
    }

    [Test]
    public void Add_negative_reduces_rides()
    {
        var output = new StringBuilderRidesOutput();
        var pm3 = FakeRidesPm3Api.WithRides(73);
        var handler = new RidesCommandHandler(pm3, output, new RidesConfig());
        handler.Execute(["read"]);

        handler.Execute(["add", "-13"]); // 73 - 13 = 60

        Assert.That(pm3.GetRides(), Is.EqualTo(60u));
        Assert.That(output.Lines, Has.Some.Contains("Success"));
    }

    [Test]
    public void Add_negative_to_exactly_zero_succeeds()
    {
        var output = new StringBuilderRidesOutput();
        var pm3 = FakeRidesPm3Api.WithRides(42);
        var handler = new RidesCommandHandler(pm3, output, new RidesConfig());
        handler.Execute(["read"]);

        handler.Execute(["add", "-42"]); // 42 - 42 = 0

        Assert.That(pm3.GetRides(), Is.EqualTo(0u));
        Assert.That(output.Lines, Has.Some.Contains("Success"));
    }

    [Test]
    public void Add_negative_below_zero_prints_error()
    {
        var output = new StringBuilderRidesOutput();
        var pm3 = FakeRidesPm3Api.WithRides(42);
        var handler = new RidesCommandHandler(pm3, output, new RidesConfig());
        handler.Execute(["read"]);

        handler.Execute(["add", "-43"]); // 42 - 43 = -1

        Assert.That(output.Lines, Has.Some.Contains("[0, 500]"));
    }

    [Test]
    public void Add_exceeding_500_prints_error()
    {
        var output = new StringBuilderRidesOutput();
        var pm3 = FakeRidesPm3Api.WithRides(400);
        var handler = new RidesCommandHandler(pm3, output, new RidesConfig());
        handler.Execute(["read"]);

        handler.Execute(["add", "101"]); // 400 + 101 = 501

        Assert.That(output.Lines, Has.Some.Contains("[0, 500]"));
    }

    [Test]
    public void Add_reaching_exactly_500_succeeds()
    {
        var output = new StringBuilderRidesOutput();
        var pm3 = FakeRidesPm3Api.WithRides(400);
        var handler = new RidesCommandHandler(pm3, output, new RidesConfig());
        handler.Execute(["read"]);

        handler.Execute(["add", "100"]); // 400 + 100 = 500

        Assert.That(output.Lines, Has.Some.Contains("Success"));
    }

    [Test]
    public void Add_without_read_prints_error()
    {
        var output = new StringBuilderRidesOutput();
        var handler = new RidesCommandHandler(new FakeRidesPm3Api(), output, new RidesConfig());

        handler.Execute(["add", "10"]);

        Assert.That(output.Lines, Has.Some.Contains("no rides in memory"));
    }

    [Test]
    public void Price_set_with_loaded_rides_prints_exact_cost()
    {
        // 73 rides → set 100, rideDiff = 27, price = 27/100 * 4.00 = 1.08
        var output = new StringBuilderRidesOutput();
        var config = new RidesConfig { PricePer100 = 4.00m };
        var pm3 = FakeRidesPm3Api.WithRides(73);
        var handler = new RidesCommandHandler(pm3, output, config);
        handler.Execute(["read"]);

        handler.Execute(["price", "set", "100"]);

        Assert.That(output.Lines, Has.Some.EqualTo("will cost: 1.08 EUR"));
    }

    [Test]
    public void Money_with_amount_prints_rides()
    {
        var output = new StringBuilderRidesOutput();
        var config = new RidesConfig { PricePer100 = 4.00m };
        var pm3 = FakeRidesPm3Api.WithRides(73);
        var handler = new RidesCommandHandler(pm3, output, config);
        handler.Execute(["read"]);

        handler.Execute(["money", "8.00"]);

        Assert.That(output.Lines, Has.Some.EqualTo("200 rides"));
    }

    [Test]
    public void Money_without_pricePer100_prints_error()
    {
        var output = new StringBuilderRidesOutput();
        var pm3 = FakeRidesPm3Api.WithRides(73);
        var handler = new RidesCommandHandler(pm3, output, new RidesConfig());
        handler.Execute(["read"]);

        handler.Execute(["money", "4.00"]);

        Assert.That(output.Lines, Has.Some.Contains("pricePer100"));
    }

    [Test]
    public void Set_preserves_43FE_profile_instead_of_switching_to_legacy_family()
    {
        var output = new StringBuilderRidesOutput();
        var pm3 = FakeRidesPm3Api.WithRidesEncodedByFamily(73, TokenBlockUtils.Families.Family48C7_0To127);
        var handler = new RidesCommandHandler(pm3, output, new RidesConfig());
        handler.Execute(["read"]);

        handler.Execute(["set", "100"]);

        var expected = TokenBlockUtils.EncodeByFamily(100, TokenBlockUtils.Families.Family48C7_0To127);
        Assert.That(pm3.GetBlockHex(5), Is.EqualTo(expected.ToHex()));
        Assert.That(pm3.GetBlockHex(6), Is.EqualTo(expected.ToHex()));
        Assert.That(pm3.GetBlockHex(5), Is.Not.EqualTo(TokenBlockUtils.Encode(100, EncodingSequences.Mercury).ToHex()));
    }

    [Test]
    public void Add_preserves_43FE_profile_when_crossing_to_high_range()
    {
        var output = new StringBuilderRidesOutput();
        var pm3 = FakeRidesPm3Api.WithRidesEncodedByFamily(120, TokenBlockUtils.Families.Family48C7_0To127);
        var handler = new RidesCommandHandler(pm3, output, new RidesConfig());
        handler.Execute(["read"]);

        handler.Execute(["add", "10"]); // 120 + 10 = 130

        var expected = TokenBlockUtils.EncodeByFamily(130, TokenBlockUtils.Families.FamilyBBC7_128To255);
        Assert.That(pm3.GetBlockHex(5), Is.EqualTo(expected.ToHex()));
        Assert.That(pm3.GetBlockHex(6), Is.EqualTo(expected.ToHex()));
        Assert.That(pm3.GetBlockHex(5), Is.Not.EqualTo(TokenBlockUtils.Encode(130, EncodingSequences.Mercury).ToHex()));
    }

    [Test]
    public void Set_preserves_venus_profile_at_256()
    {
        var output = new StringBuilderRidesOutput();
        var pm3 = FakeRidesPm3Api.WithRidesEncodedByFamily(255, TokenBlockUtils.Families.FamilyBBC7_128To255);
        var handler = new RidesCommandHandler(pm3, output, new RidesConfig());
        handler.Execute(["read"]);

        handler.Execute(["set", "256"]);

        var expected = EncodingSequences.Venus.Encode(256);
        Assert.That(pm3.GetBlockHex(5), Is.EqualTo(expected.ToHex()));
        Assert.That(pm3.GetBlockHex(6), Is.EqualTo(expected.ToHex()));
        Assert.That(pm3.GetBlockHex(5), Is.Not.EqualTo(EncodingSequences.Mercury.Encode(256).ToHex()));
    }

    [Test]
    public void Set_preserves_venus_profile_at_500()
    {
        var output = new StringBuilderRidesOutput();
        var pm3 = FakeRidesPm3Api.WithRidesEncodedByFamily(499, TokenBlockUtils.Families.FamilyBBC6_384To500);
        var handler = new RidesCommandHandler(pm3, output, new RidesConfig());
        handler.Execute(["read"]);

        handler.Execute(["set", "500"]);

        var expected = EncodingSequences.Venus.Encode(500);
        Assert.That(pm3.GetBlockHex(5), Is.EqualTo(expected.ToHex()));
        Assert.That(pm3.GetBlockHex(6), Is.EqualTo(expected.ToHex()));
        Assert.That(pm3.GetBlockHex(5), Is.Not.EqualTo(EncodingSequences.Mercury.Encode(500).ToHex()));
    }

    [Test]
    public void Add_preserves_venus_profile_when_crossing_to_48C6_range()
    {
        var output = new StringBuilderRidesOutput();
        var pm3 = FakeRidesPm3Api.WithRidesEncodedByFamily(250, TokenBlockUtils.Families.FamilyBBC7_128To255);
        var handler = new RidesCommandHandler(pm3, output, new RidesConfig());
        handler.Execute(["read"]);

        handler.Execute(["add", "10"]); // 260

        var expected = EncodingSequences.Venus.Encode(260);
        Assert.That(pm3.GetBlockHex(5), Is.EqualTo(expected.ToHex()));
        Assert.That(pm3.GetBlockHex(6), Is.EqualTo(expected.ToHex()));
        Assert.That(pm3.GetBlockHex(5), Is.Not.EqualTo(EncodingSequences.Mercury.Encode(260).ToHex()));
    }

    [Test]
    public void Reset_venus_writes_venus_identity_blocks_and_zero_ride_encoding()
    {
        var output = new StringBuilderRidesOutput();
        var pm3 = FakeRidesPm3Api.WithRidesEncodedByFamily(180, TokenBlockUtils.Families.FamilyBBC7_128To255);
        var handler = new RidesCommandHandler(pm3, output, new RidesConfig(), new ScriptedRidesInput("y"));

        handler.Execute(["reset", "--sequence", "venus"]);

        Assert.That(pm3.GetBlockHex(1), Is.EqualTo("43FE0062"));
        Assert.That(pm3.GetBlockHex(2), Is.EqualTo("5BA494A3"));
        Assert.That(pm3.GetBlockHex(3), Is.EqualTo("D6D1C733"));
        Assert.That(pm3.GetBlockHex(4), Is.EqualTo("D6D1C733"));
        Assert.That(pm3.GetBlockHex(1), Is.Not.EqualTo("9BFE0062"));
        Assert.That(pm3.GetBlockHex(3), Is.Not.EqualTo("D5D1D713"));

        var expected = EncodingSequences.Venus.Encode(0);
        Assert.That(pm3.GetBlockHex(5), Is.EqualTo(expected.ToHex()));
        Assert.That(pm3.GetBlockHex(6), Is.EqualTo(expected.ToHex()));
        Assert.That(pm3.GetBlockHex(5), Is.Not.EqualTo(EncodingSequences.Mercury.Encode(0).ToHex()));
    }

    [Test]
    public void Reset_earth_writes_d3_identity_blocks_and_zero_ride_encoding()
    {
        var output = new StringBuilderRidesOutput();
        var pm3 = FakeRidesPm3Api.WithRides(73);
        var handler = new RidesCommandHandler(pm3, output, new RidesConfig(), new ScriptedRidesInput("y"));

        handler.Execute(["reset", "--sequence", "earth"]);

        Assert.That(pm3.GetBlockHex(1), Is.EqualTo("D3FE005D"));
        Assert.That(pm3.GetBlockHex(2), Is.EqualTo("522BC69D"));
        Assert.That(pm3.GetBlockHex(3), Is.EqualTo("650432F5"));
        Assert.That(pm3.GetBlockHex(4), Is.EqualTo("650432F5"));
        Assert.That(pm3.GetBlockHex(5), Is.EqualTo("18121218"));
        Assert.That(pm3.GetBlockHex(6), Is.EqualTo("18121218"));
    }

    [Test]
    public void Set_preserves_earth_profile_within_confirmed_second_family()
    {
        var output = new StringBuilderRidesOutput();
        var pm3 = FakeRidesPm3Api.WithSequenceRides(EncodingSequences.Earth, 128);
        var handler = new RidesCommandHandler(pm3, output, new RidesConfig());
        handler.Execute(["read"]);

        handler.Execute(["set", "255"]);

        Assert.That(pm3.GetBlockHex(5), Is.EqualTo("EB12EDE7"));
        Assert.That(pm3.GetBlockHex(6), Is.EqualTo("EB12EDE7"));
        Assert.That(pm3.GetBlockHex(5), Is.Not.EqualTo(EncodingSequences.Venus.Encode(255).ToHex()));
        Assert.That(pm3.GetBlockHex(5), Is.Not.EqualTo(EncodingSequences.Mercury.Encode(255).ToHex()));
    }

    [Test]
    public void Set_above_earth_confirmed_range_prints_sequence_range_and_does_not_write()
    {
        var output = new StringBuilderRidesOutput();
        var pm3 = FakeRidesPm3Api.WithSequenceRides(EncodingSequences.Earth, 255);
        var handler = new RidesCommandHandler(pm3, output, new RidesConfig());
        handler.Execute(["read"]);
        output.Clear();

        handler.Execute(["set", "256"]);

        Assert.That(output.Lines, Has.Some.Contains("range [0, 255] for sequence 'earth'"));
        Assert.That(output.Lines, Has.None.EqualTo("Success."));
        Assert.That(pm3.GetBlockHex(5), Is.EqualTo("EB12EDE7"));
        Assert.That(pm3.GetBlockHex(6), Is.EqualTo("EB12EDE7"));
        Assert.That(pm3.WrittenBlocks, Is.Empty);
    }

    [Test]
    public void Add_above_earth_confirmed_range_prints_sequence_range_and_does_not_write()
    {
        var output = new StringBuilderRidesOutput();
        var pm3 = FakeRidesPm3Api.WithSequenceRides(EncodingSequences.Earth, 255);
        var handler = new RidesCommandHandler(pm3, output, new RidesConfig());
        handler.Execute(["read"]);
        output.Clear();

        handler.Execute(["add", "1"]);

        Assert.That(output.Lines, Has.Some.Contains("range [0, 255] for sequence 'earth'"));
        Assert.That(output.Lines, Has.None.EqualTo("Success."));
        Assert.That(pm3.GetBlockHex(5), Is.EqualTo("EB12EDE7"));
        Assert.That(pm3.GetBlockHex(6), Is.EqualTo("EB12EDE7"));
        Assert.That(pm3.WrittenBlocks, Is.Empty);
    }

    [Test]
    public void Set_after_read_writes_blocks_5_and_6()
    {
        var output = new StringBuilderRidesOutput();
        var pm3 = FakeRidesPm3Api.WithRides(73);
        var handler = new RidesCommandHandler(pm3, output, new RidesConfig());
        handler.Execute(["read"]);

        handler.Execute(["set", "100"]);

        Assert.That(pm3.GetRides(), Is.EqualTo(100u));
        Assert.That(output.Lines, Has.Some.Contains("Success"));
    }

    [Test]
    public void Set_prints_rides_remaining_after_success()
    {
        var output = new StringBuilderRidesOutput();
        var pm3 = FakeRidesPm3Api.WithRides(73);
        var handler = new RidesCommandHandler(pm3, output, new RidesConfig());
        handler.Execute(["read"]);
        output.Clear();

        handler.Execute(["set", "100"]);

        Assert.That(output.Lines, Has.Some.EqualTo("rides remaining: 100"));
    }

    [Test]
    public void Add_prints_rides_remaining_after_success()
    {
        var output = new StringBuilderRidesOutput();
        var pm3 = FakeRidesPm3Api.WithRides(73);
        var handler = new RidesCommandHandler(pm3, output, new RidesConfig());
        handler.Execute(["read"]);
        output.Clear();

        handler.Execute(["add", "27"]); // 73 + 27 = 100

        Assert.That(output.Lines, Has.Some.EqualTo("rides remaining: 100"));
    }

    [Test]
    public void Price_add_preview_prints_exact_cost()
    {
        // 100 rides, add 42 → rideDiff = 42, price = 42/100 * 4.00 = 1.68
        var output = new StringBuilderRidesOutput();
        var config = new RidesConfig { PricePer100 = 4.00m };
        var pm3 = FakeRidesPm3Api.WithRides(100);
        var handler = new RidesCommandHandler(pm3, output, config);
        handler.Execute(["read"]);

        handler.Execute(["price", "add", "42"]);

        Assert.That(output.Lines, Has.Some.EqualTo("will cost: 1.68 EUR"));
    }

    [Test]
    public void Price_set_rounds_up_to_nearest_cent()
    {
        // 73 rides → set 80, rideDiff = 7, price = 7/100 * 4.50 = 0.315 → rounds up to 0.32
        var output = new StringBuilderRidesOutput();
        var config = new RidesConfig { PricePer100 = 4.50m };
        var pm3 = FakeRidesPm3Api.WithRides(73);
        var handler = new RidesCommandHandler(pm3, output, config);
        handler.Execute(["read"]);

        handler.Execute(["price", "set", "80"]);

        Assert.That(output.Lines, Has.Some.EqualTo("will cost: 0.32 EUR"));
    }

    [Test]
    public void Price_add_rounds_up_single_ride()
    {
        // 73 rides, add 1 → rideDiff = 1, price = 1/100 * 3.33 = 0.0333 → rounds up to 0.04
        var output = new StringBuilderRidesOutput();
        var config = new RidesConfig { PricePer100 = 3.33m };
        var pm3 = FakeRidesPm3Api.WithRides(73);
        var handler = new RidesCommandHandler(pm3, output, config);
        handler.Execute(["read"]);

        handler.Execute(["price", "add", "1"]);

        Assert.That(output.Lines, Has.Some.EqualTo("will cost: 0.04 EUR"));
    }

    [Test]
    public void Money_8eur_at_4_per_100_returns_200_rides()
    {
        var output = new StringBuilderRidesOutput();
        var config = new RidesConfig { PricePer100 = 4.00m };
        var pm3 = FakeRidesPm3Api.WithRides(73);
        var handler = new RidesCommandHandler(pm3, output, config);
        handler.Execute(["read"]);

        handler.Execute(["money", "8.00"]);

        Assert.That(output.Lines, Has.Some.EqualTo("200 rides"));
    }

    [Test]
    public void Add_after_read_calls_set_with_sum()
    {
        var output = new StringBuilderRidesOutput();
        var pm3 = FakeRidesPm3Api.WithRides(73);
        var handler = new RidesCommandHandler(pm3, output, new RidesConfig());
        handler.Execute(["read"]);

        handler.Execute(["add", "27"]); // 73 + 27 = 100

        Assert.That(pm3.GetRides(), Is.EqualTo(100u));
        Assert.That(output.Lines, Has.Some.Contains("Success"));
    }

    [Test]
    public void Sequence_read_add_read_different_token_add()
    {
        // Token 1: 73 rides, add 27 → 100
        // Token 2: 42 rides, add 13 → 55
        var output = new StringBuilderRidesOutput();
        var pm3 = FakeRidesPm3Api.WithRides(73);
        var handler = new RidesCommandHandler(pm3, output, new RidesConfig());

        handler.Execute(["read"]);
        Assert.That(output.Lines, Has.Some.EqualTo("rides remaining: 73"));

        output.Clear();
        handler.Execute(["add", "27"]); // 73 + 27 = 100
        Assert.That(pm3.GetRides(), Is.EqualTo(100u));
        Assert.That(output.Lines, Has.Some.Contains("Success"));

        // Simulate placing a different token on the reader
        pm3.SimulateNewToken(42);
        output.Clear();
        handler.Execute(["read"]);
        Assert.That(output.Lines, Has.Some.EqualTo("rides remaining: 42"));

        output.Clear();
        handler.Execute(["add", "13"]); // 42 + 13 = 55
        Assert.That(pm3.GetRides(), Is.EqualTo(55u));
        Assert.That(output.Lines, Has.Some.Contains("Success"));
    }

    [Test]
    public void Sequence_consecutive_adds_accumulate()
    {
        // Start at 42, add 13 → 55, add 7 → 62, add 38 → 100
        var output = new StringBuilderRidesOutput();
        var pm3 = FakeRidesPm3Api.WithRides(42);
        var handler = new RidesCommandHandler(pm3, output, new RidesConfig());

        handler.Execute(["read"]);
        Assert.That(output.Lines, Has.Some.EqualTo("rides remaining: 42"));

        output.Clear();
        handler.Execute(["add", "13"]); // → 55
        Assert.That(pm3.GetRides(), Is.EqualTo(55u));

        output.Clear();
        handler.Execute(["add", "7"]); // → 62
        Assert.That(pm3.GetRides(), Is.EqualTo(62u));

        output.Clear();
        handler.Execute(["add", "38"]); // → 100
        Assert.That(pm3.GetRides(), Is.EqualTo(100u));
        Assert.That(output.Lines, Has.Some.Contains("Success"));
    }

    [Test]
    public void Sequence_set_after_add_overrides_to_set_value()
    {
        // Start at 73, add 27 → 100, then set 42 → 42, add 8 → 50
        var output = new StringBuilderRidesOutput();
        var pm3 = FakeRidesPm3Api.WithRides(73);
        var handler = new RidesCommandHandler(pm3, output, new RidesConfig());

        handler.Execute(["read"]);
        handler.Execute(["add", "27"]); // → 100

        output.Clear();
        handler.Execute(["set", "42"]); // override to 42
        Assert.That(pm3.GetRides(), Is.EqualTo(42u));

        output.Clear();
        handler.Execute(["add", "8"]); // 42 + 8 = 50
        Assert.That(pm3.GetRides(), Is.EqualTo(50u));
        Assert.That(output.Lines, Has.Some.Contains("Success"));
    }

    [Test]
    public void Sequence_add_with_price_change_mid_session()
    {
        // pricePer100 = 4.50, read 73, add 7 → 80 (cost 0.32), change to 24.50, add 20 → 100 (cost 4.90)
        var output = new StringBuilderRidesOutput();
        var config = new RidesConfig { PricePer100 = 4.50m };
        var pm3 = FakeRidesPm3Api.WithRides(73);
        var handler = new RidesCommandHandler(pm3, output, config);

        handler.Execute(["read"]);
        Assert.That(output.Lines, Has.Some.EqualTo("rides remaining: 73"));

        // add 7: 73 → 80, rideDiff = 7, price = ceil(7/100 * 4.50 * 100) / 100 = ceil(31.5)/100 = 0.32
        output.Clear();
        handler.Execute(["add", "7"]);
        Assert.That(output.Lines, Has.Some.Contains("Success"));
        Assert.That(output.Lines, Has.Some.EqualTo("cost: 0.32 EUR"));

        // Change price mid-session
        handler.Execute(["config", "pricePer100", "24.50"]);
        Assert.That(config.PricePer100, Is.EqualTo(24.50m));

        // add 20: 80 → 100, rideDiff = 20, price = ceil(20/100 * 24.50 * 100) / 100 = ceil(490)/100 = 4.90
        output.Clear();
        handler.Execute(["add", "20"]);
        Assert.That(output.Lines, Has.Some.Contains("Success"));
        Assert.That(output.Lines, Has.Some.EqualTo("cost: 4.90 EUR"));
    }
}
