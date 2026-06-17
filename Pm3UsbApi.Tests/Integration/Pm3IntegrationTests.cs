using NUnit.Framework;
using Pm3UsbApi;
using Pm3UsbApi.Commands;
using Pm3UsbApi.Parsers;
using Tokens;

namespace Pm3UsbApi.Tests.Integration;

/// <summary>
/// Integration tests that require a connected Proxmark3 with T5577 tag.
/// Parameterized by executor kind so both process and native paths are exercised.
/// Skip in CI: dotnet test --filter "Category!=Integration"
/// Run manually:
///   dotnet test --filter "Category=Integration" -- NUnit.RunExplicitTests=true
///   dotnet test --filter "FullyQualifiedName~Pm3IntegrationTests(Process)" -- NUnit.RunExplicitTests=true
///   dotnet test --filter "FullyQualifiedName~Pm3IntegrationTests(Native)" -- NUnit.RunExplicitTests=true
/// </summary>
[TestFixture(Pm3ExecutorKind.Process)]
[TestFixture(Pm3ExecutorKind.Native)]
[Category("Integration")]
[Explicit("Requires Proxmark3 connected with T5577 tag. Run: dotnet test --filter 'Category=Integration'")]
[NonParallelizable]
public class Pm3IntegrationTests
{
    private const uint TestWriteBlock5Value = 0xDEADBEEF;
    private const uint TestWriteBlock6Value = 0xCAFEBABE;

    private readonly Pm3ExecutorKind _executorKind;
    private string? _snapshotBlock5Hex;
    private string? _snapshotBlock6Hex;

    public Pm3IntegrationTests(Pm3ExecutorKind executorKind) => _executorKind = executorKind;

    private bool SupportsWrite => true;

    private bool SupportsDump => true;

    private bool SupportsCliPassthrough => _executorKind == Pm3ExecutorKind.Process;

    private TimeSpan DefaultCommandTimeout => SupportsWrite
        ? TimeSpan.FromSeconds(15)
        : TimeSpan.FromSeconds(15);

    private Pm3Options CreateOptions(TimeSpan? commandTimeout = null) =>
        IntegrationTestOptions.Create(_executorKind, commandTimeout ?? DefaultCommandTimeout);

    [OneTimeSetUp]
    public async Task SnapshotRideBlocksAsync()
    {
        if (!SupportsWrite)
            return;

        await using var pm3 = await ConnectPm3Async();
        await pm3.EnsureT55SessionActiveAsync();
        _snapshotBlock5Hex = await pm3.ReadPage0BlockAsync(5);
        _snapshotBlock6Hex = await pm3.ReadPage0BlockAsync(6);
    }

    [OneTimeTearDown]
    public async Task RestoreRideBlocksAsync()
    {
        if (!SupportsWrite || _snapshotBlock5Hex is null || _snapshotBlock6Hex is null)
            return;

        try
        {
            await using var pm3 = await ConnectPm3Async();
            await pm3.EnsureT55SessionActiveAsync();
            await pm3.WritePage0BlockAsync(5, T55Block.FromHex(_snapshotBlock5Hex));
            await pm3.WritePage0BlockAsync(6, T55Block.FromHex(_snapshotBlock6Hex));

            Assert.That(await pm3.ReadPage0BlockAsync(5), Is.EqualTo(_snapshotBlock5Hex),
                "Fixture teardown failed to restore block 5.");
            Assert.That(await pm3.ReadPage0BlockAsync(6), Is.EqualTo(_snapshotBlock6Hex),
                "Fixture teardown failed to restore block 6.");
        }
        catch (Exception ex)
        {
            Assert.Fail($"Fixture teardown failed to restore blocks 5 and 6: {ex.Message}");
        }
    }

    private void RequireWrite()
    {
        if (!SupportsWrite)
            Assert.Ignore("T55 write/dump is not supported by the native executor yet.");
    }

    private void RequireDump()
    {
        if (!SupportsDump)
            Assert.Ignore("T55 dump is not supported by the native executor yet.");
    }

    private void RequireCliPassthrough()
    {
        if (!SupportsCliPassthrough)
            Assert.Ignore("Raw CLI passthrough is not supported by the native executor.");
    }

    private async Task<Pm3> ConnectPm3Async(Pm3Options? options = null)
    {
        var pm3 = new Pm3(options ?? CreateOptions());
        await pm3.ConnectAsync();
        return pm3;
    }

    private static async Task WriteReadRestoreBlockAsync(Pm3 pm3, uint block, T55Block testValue)
    {
        var originalHex = await pm3.ReadPage0BlockAsync(block);
        try
        {
            await pm3.WritePage0BlockAsync(block, testValue);
            var readBack = await pm3.ReadPage0BlockAsync(block);
            Assert.That(readBack, Is.EqualTo(testValue.ToHex()));
        }
        finally
        {
            await pm3.WritePage0BlockAsync(block, T55Block.FromHex(originalHex));
        }
    }

    [Test]
    public async Task ConnectDisconnect_Lifecycle_Succeeds()
    {
        await using var pm3 = new Pm3(CreateOptions());

        await pm3.ConnectAsync();
        Assert.That(await pm3.IsConnectedAsync(), Is.True);

        await pm3.DisconnectAsync();
    }

    [Test]
    public async Task Connect_IsConnected_ReturnsTrue()
    {
        await using var pm3 = await ConnectPm3Async();
        Assert.That(await pm3.IsConnectedAsync(), Is.True);
    }

    [Test]
    public async Task Detect_ThenReadAllBlocks_ReturnsNonNullHex()
    {
        await using var pm3 = await ConnectPm3Async();

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
    public async Task DetectAndReadBlock5_ReturnsHex()
    {
        await using var pm3 = await ConnectPm3Async();
        await pm3.EnsureT55SessionActiveAsync();

        var hex = await pm3.ReadPage0BlockAsync(5);
        Assert.That(hex, Has.Length.EqualTo(8));
        Assert.That(hex, Does.Match("^[0-9A-F]+$"));
    }

    [Test]
    public async Task WriteBlock5_ThenRead_MatchesWrittenValue()
    {
        RequireWrite();
        await using var pm3 = await ConnectPm3Async();

        await pm3.EnsureT55SessionActiveAsync();
        await WriteReadRestoreBlockAsync(pm3, 5, new T55Block(TestWriteBlock5Value));
    }

    [Test]
    public async Task WriteBlock6_ThenRead_MatchesWrittenValue()
    {
        RequireWrite();
        await using var pm3 = await ConnectPm3Async();

        await pm3.EnsureT55SessionActiveAsync();
        await WriteReadRestoreBlockAsync(pm3, 6, new T55Block(TestWriteBlock6Value));
    }

    [Test]
    public async Task Tune_ReturnsReasonablePeakMilliVolts()
    {
        await using var pm3 = await ConnectPm3Async();

        await pm3.StartLfTuneAsync();
        var peakMv = await pm3.GetLfTuneLastMilliVoltsAsync();

        Assert.That(peakMv, Is.GreaterThan(1000u), "Expected a non-trivial LF antenna signal with tag on reader.");
        Assert.That(peakMv, Is.LessThan(100_000u), "Peak mV looks unreasonably high.");
    }

    [Test]
    public async Task ConnectThenTune_SequentialOperationsSucceed()
    {
        await using var pm3 = await ConnectPm3Async();

        Assert.That(await pm3.IsConnectedAsync(), Is.True);

        await pm3.StartLfTuneAsync();
        var first = await pm3.GetLfTuneLastMilliVoltsAsync();

        await pm3.StartLfTuneAsync();
        var second = await pm3.GetLfTuneLastMilliVoltsAsync();

        Assert.That(first, Is.GreaterThan(1000u));
        Assert.That(second, Is.GreaterThan(1000u));
        Assert.That(Math.Abs((int)first - (int)second), Is.LessThan(5000));
    }

    [Test]
    public async Task ReadBlock5Twice_ReturnsSameValue()
    {
        await using var pm3 = await ConnectPm3Async();

        await pm3.EnsureT55SessionActiveAsync();

        var first = await pm3.ReadPage0BlockAsync(5);
        var second = await pm3.ReadPage0BlockAsync(5);

        Assert.That(second, Is.EqualTo(first));
    }

    [Test]
    public async Task ReadBlocks5And6_DecodeRidesInValidRange()
    {
        await using var pm3 = await ConnectPm3Async();

        await pm3.EnsureT55SessionActiveAsync();

        var block5 = T55Block.FromHex(await pm3.ReadPage0BlockAsync(5));
        var block6 = T55Block.FromHex(await pm3.ReadPage0BlockAsync(6));

        Assert.That(TokenBlockUtils.Families.TryGetFamilyFromBlock(block5, out _), Is.True,
            "Block 5 should use a known elevator encoding family.");
        Assert.That(TokenBlockUtils.Families.TryGetFamilyFromBlock(block6, out _), Is.True,
            "Block 6 should use a known elevator encoding family.");

        var rides5 = TokenBlockUtils.Decode(block5);
        var rides6 = TokenBlockUtils.Decode(block6);

        Assert.That(rides5, Is.InRange(0u, 500u));
        Assert.That(rides6, Is.InRange(0u, 500u));
        Assert.That(rides6, Is.EqualTo(rides5), "Blocks 5 and 6 should encode the same ride count.");
    }

    [Test]
    public async Task TokenBaseline_ReadsFiftyRides()
    {
        await using var pm3 = await ConnectPm3Async();
        await pm3.EnsureT55SessionActiveAsync();

        var block5 = T55Block.FromHex(await pm3.ReadPage0BlockAsync(5));
        var rides = TokenBlockUtils.Decode(block5);

        Assert.That(rides, Is.EqualTo(50u), $"Expected 50 rides via {_executorKind} executor.");
    }

    [Test]
    public async Task Dump_ReturnsExpectedBlockCount()
    {
        RequireDump();
        await using var pm3 = await ConnectPm3Async();

        var output = await pm3.DumpAsync();

        Assert.That(output, Is.Not.Null.And.Not.Empty);
        Assert.That(output, Does.Contain("blk").Or.Contain("Block").Or.Contain("hex"));
        var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var dumpResult = DumpParser.Parse(
            new CommandResult { Commands = [new T55DumpCommand()], OutputLines = lines });
        Assert.That(dumpResult.Success, Is.True, () => $"Dump parse failed. Preview: {output[..Math.Min(400, output.Length)]}...");
        Assert.That(dumpResult.Blocks.Count, Is.GreaterThanOrEqualTo(8));
    }

    [Test]
    public async Task Dump_Block5MatchesIndividualRead()
    {
        RequireDump();
        await using var pm3 = await ConnectPm3Async();

        await pm3.EnsureT55SessionActiveAsync();

        var block5Hex = await pm3.ReadPage0BlockAsync(5);
        var dumpOutput = await pm3.DumpAsync();
        var lines = dumpOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var dumpResult = DumpParser.Parse(
            new CommandResult { Commands = [new T55DumpCommand()], OutputLines = lines });

        Assert.That(dumpResult.Success, Is.True);
        Assert.That(dumpResult.Blocks.Count, Is.GreaterThanOrEqualTo(6));
        Assert.That(dumpResult.Blocks[5].ToHex(), Is.EqualTo(block5Hex));
    }

    [Test]
    public async Task ExecuteRawCommand_HwVersion_ReturnsDeviceInfo()
    {
        RequireCliPassthrough();

        await using var pm3 = await ConnectPm3Async();

        var output = await pm3.ExecuteRawCommandAsync("hw version");

        Assert.That(output, Does.Contain("Proxmark3").IgnoreCase);
        Assert.That(output, Does.Not.Contain("OFFLINE mode"));
    }

    [Test]
    public async Task Execute_WithVeryShortTimeout_ThrowsPm3TimeoutException()
    {
        RequireCliPassthrough();

        var options = CreateOptions(TimeSpan.FromMilliseconds(1));
        await using var pm3 = new Pm3(options);
        await pm3.ConnectAsync();

        Assert.ThrowsAsync<Pm3TimeoutException>(async () =>
            await pm3.ExecuteRawCommandAsync("lf t55 dump"));
    }

    [Test]
    public async Task ReadAfterDisconnect_ThrowsMeaningfulError()
    {
        await using var pm3 = await ConnectPm3Async();
        await pm3.DisconnectAsync();

        var ex = Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            await pm3.ReadPage0BlockAsync(0));

        Assert.That(ex!.Message, Does.Contain("disposed"));
    }

    [Test]
    public async Task SequentialSession_ExecutesTenOperationsWithoutFailure()
    {
        RequireWrite();

        await using var pm3 = await ConnectPm3Async(CreateOptions(TimeSpan.FromSeconds(20)));

        Assert.That(await pm3.IsConnectedAsync(), Is.True);

        await pm3.StartLfTuneAsync();
        var peakMv = await pm3.GetLfTuneLastMilliVoltsAsync();
        Assert.That(peakMv, Is.GreaterThan(1000u));

        await pm3.EnsureT55SessionActiveAsync();

        var block0Hex = await pm3.ReadPage0BlockAsync(0);
        Assert.That(block0Hex, Has.Length.EqualTo(8));

        var originalBlock5Hex = await pm3.ReadPage0BlockAsync(5);
        var originalBlock6Hex = await pm3.ReadPage0BlockAsync(6);

        var dumpOutput = await pm3.DumpAsync();
        var dumpLines = dumpOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var dumpResult = DumpParser.Parse(
            new CommandResult { Commands = [new T55DumpCommand()], OutputLines = dumpLines });
        Assert.That(dumpResult.Success, Is.True);
        Assert.That(dumpResult.Blocks[5].ToHex(), Is.EqualTo(originalBlock5Hex));

        try
        {
            await pm3.WritePage0BlockAsync(5, new T55Block(TestWriteBlock5Value));
            Assert.That(await pm3.ReadPage0BlockAsync(5), Is.EqualTo("DEADBEEF"));

            await pm3.WritePage0BlockAsync(6, new T55Block(TestWriteBlock6Value));
            Assert.That(await pm3.ReadPage0BlockAsync(6), Is.EqualTo("CAFEBABE"));
        }
        finally
        {
            await pm3.WritePage0BlockAsync(5, T55Block.FromHex(originalBlock5Hex));
            await pm3.WritePage0BlockAsync(6, T55Block.FromHex(originalBlock6Hex));
        }

        Assert.That(await pm3.ReadPage0BlockAsync(5), Is.EqualTo(originalBlock5Hex));
        Assert.That(await pm3.ReadPage0BlockAsync(6), Is.EqualTo(originalBlock6Hex));

        for (uint block = 1; block <= 4; block++)
        {
            var hex = await pm3.ReadPage0BlockAsync(block);
            Assert.That(hex, Has.Length.EqualTo(8));
            Assert.That(hex, Does.Match("^[0-9A-FA-F]+$"));
        }

        var ridesBlock = T55Block.FromHex(await pm3.ReadPage0BlockAsync(5));
        Assert.That(TokenBlockUtils.Families.TryGetFamilyFromBlock(ridesBlock, out _), Is.True);
        Assert.That(TokenBlockUtils.Decode(ridesBlock), Is.InRange(0u, 500u));

        Assert.That(await pm3.IsConnectedAsync(), Is.True);
    }
}
