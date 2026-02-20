using NUnit.Framework;
using RidesCli;

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
    public void Read_shows_signal_strength_and_rides()
    {
        var output = new StringBuilderRidesOutput();
        var pm3 = FakeRidesPm3Api.WithRides(73);
        pm3.SignalStrengthMv = 420;
        var handler = new RidesCommandHandler(pm3, output, new RidesConfig());

        handler.Execute(["read"]);

        Assert.That(output.Lines, Has.Some.Matches(@"signal strength: 420 mV"));
        Assert.That(output.Lines, Has.Some.Matches(@"rides remaining: 73"));
    }

    [Test]
    public void Read_when_decode_fails_unloads_and_prints_error()
    {
        var output = new StringBuilderRidesOutput();
        var pm3 = FakeRidesPm3Api.WithInvalidBlock5();
        var handler = new RidesCommandHandler(pm3, output, new RidesConfig());

        handler.Execute(["read"]);

        Assert.That(output.Lines, Has.Some.Contains("Error"));
        // Subsequent set should fail with "no rides in memory"
        handler.Execute(["set", "50"]);
        Assert.That(output.Lines, Has.Some.Contains("no rides in memory"));
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
