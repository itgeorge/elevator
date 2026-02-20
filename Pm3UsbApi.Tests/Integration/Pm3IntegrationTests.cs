using NUnit.Framework;
using Tokens;

namespace Pm3UsbApi.Tests.Integration;

/// <summary>
/// Integration tests that require a connected Proxmark3 with T5577 tag.
/// Skip in CI: dotnet test --filter "Category!=Integration"
/// Run manually: dotnet test --filter "Category=Integration"
/// </summary>
[TestFixture]
[Category("Integration")]
[Explicit("Requires Proxmark3 connected with T5577 tag. Run: dotnet test --filter 'Category=Integration'")]
public class Pm3IntegrationTests
{
    private static Pm3Options CreateOptions()
    {
        var port = Environment.GetEnvironmentVariable("PM3_DEVICE_PORT");
        return new Pm3Options
        {
            DevicePort = string.IsNullOrWhiteSpace(port) ? null : port.Trim(),
            AutoConnect = string.IsNullOrWhiteSpace(port) || port.Trim().ToLowerInvariant() == "auto",
            DefaultCommandTimeout = TimeSpan.FromSeconds(15),
            ConnectTimeout = TimeSpan.FromSeconds(20),
            WorkingDirectory = Pm3Options.DevRunsDirectoryName,
        };
    }

    [Test]
    public async Task ConnectDisconnect_Lifecycle_Succeeds()
    {
        await using var pm3 = new Pm3(CreateOptions());

        await pm3.ConnectAsync();
        Assert.That(await pm3.IsConnectedAsync(), Is.True);

        await pm3.DisconnectAsync();
        // After disconnect the session is disposed; no further API calls.
    }

    [Test]
    public async Task Detect_ThenReadAllBlocks_ReturnsNonNullHex()
    {
        await using var pm3 = new Pm3(CreateOptions());
        await pm3.ConnectAsync();

        await pm3.EnsureT55SessionActiveAsync();

        for (uint b = 0; b <= 7; b++)
        {
            var hex = await pm3.ReadPage0BlockAsync(b);
            Assert.That(hex, Is.Not.Null.And.Not.Empty);
            Assert.That(hex.Length, Is.EqualTo(8));
            Assert.That(hex, Does.Match("^[0-9A-Fa-f]+$"));
        }
    }

    [Test]
    public async Task WriteBlock5_ThenRead_MatchesWrittenValue()
    {
        await using var pm3 = new Pm3(CreateOptions());
        await pm3.ConnectAsync();

        await pm3.EnsureT55SessionActiveAsync();

        var originalHex = await pm3.ReadPage0BlockAsync(5);
        var testValue = new T55Block(0xDEADBEEF);

        try
        {
            await pm3.WritePage0BlockAsync(5, testValue);
            var readBack = await pm3.ReadPage0BlockAsync(5);
            Assert.That(readBack, Is.EqualTo("DEADBEEF"));
        }
        finally
        {
            var restore = T55Block.FromHex(originalHex);
            await pm3.WritePage0BlockAsync(5, restore);
        }
    }

    [Test]
    public async Task Dump_ReturnsExpectedBlockCount()
    {
        await using var pm3 = new Pm3(CreateOptions());
        await pm3.ConnectAsync();

        var output = await pm3.DumpAsync();

        Assert.That(output, Is.Not.Null.And.Not.Empty);
        Assert.That(output, Does.Contain("blk").Or.Contain("Block").Or.Contain("hex"));
        var lines = output.Split(new[] {'\r', '\n'}, StringSplitOptions.RemoveEmptyEntries);
        var dumpResult = Parsers.DumpParser.Parse(
            new CommandResult { Commands = ["lf t55 dump"], OutputLines = lines });
        Assert.That(dumpResult.Success, Is.True, () => $"Dump parse failed. Preview: {output[..Math.Min(400, output.Length)]}...");
        Assert.That(dumpResult.Blocks.Count, Is.GreaterThanOrEqualTo(8)); // Page 0 (8 blocks); may include Page 1
    }

    [Test]
    public async Task Execute_WithVeryShortTimeout_ThrowsPm3TimeoutException()
    {
        // Use a command that respects DefaultCommandTimeout (lf tune uses fixed LfTuneCaptureInterval)
        var options = CreateOptions() with { DefaultCommandTimeout = TimeSpan.FromMilliseconds(1) };
        await using var pm3 = new Pm3(options);
        await pm3.ConnectAsync();

        Assert.ThrowsAsync<Pm3TimeoutException>(async () =>
            await pm3.ExecuteRawCommandAsync("lf t55 dump"));
    }

    [Test]
    public async Task ReadAfterDisconnect_ThrowsMeaningfulError()
    {
        await using var pm3 = new Pm3(CreateOptions());
        await pm3.ConnectAsync();
        await pm3.DisconnectAsync();

        var ex = Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            await pm3.ReadPage0BlockAsync(0));

        Assert.That(ex!.Message, Does.Contain("disposed"));
    }
}
