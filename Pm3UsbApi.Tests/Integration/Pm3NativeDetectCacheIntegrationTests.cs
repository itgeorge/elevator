using NUnit.Framework;
using Pm3UsbApi;
using Pm3UsbApi.Diagnostics;
using Tokens;

namespace Pm3UsbApi.Tests.Integration;

/// <summary>
/// End-to-end native T55 detect cache validation on hardware.
/// Run:
///   dotnet test --filter "FullyQualifiedName~Pm3NativeDetectCacheIntegrationTests" -- NUnit.RunExplicitTests=true
/// </summary>
[TestFixture]
[Category("Integration")]
[Explicit("Requires Proxmark3 connected with T5577 tag.")]
[NonParallelizable]
public class Pm3NativeDetectCacheIntegrationTests
{
    private string? _logDir;
    private string? _priorLogDir;
    private string? _snapshotBlock5Hex;

    [SetUp]
    public void SetUpDiagnosticLog()
    {
        _priorLogDir = Environment.GetEnvironmentVariable("PM3_LOG_DIR");
        _logDir = Path.Combine(Path.GetTempPath(), "elevator-cache-e2e", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_logDir);
        Environment.SetEnvironmentVariable("PM3_LOG_DIR", _logDir);
        Pm3DiagnosticLog.ResetForTesting();
        Pm3DiagnosticLog.EnsureInitialized();
        TestContext.WriteLine($"Session log: {Pm3DiagnosticLog.Current.SessionLogPath}");
    }

    [TearDown]
    public void TearDownDiagnosticLog()
    {
        Environment.SetEnvironmentVariable("PM3_LOG_DIR", _priorLogDir);
        Pm3DiagnosticLog.ResetForTesting();
    }

    [Test]
    public async Task Native_DetectCache_ChainedOperations_ShowsHitAndInvalidation()
    {
        await using var pm3 = new Pm3(IntegrationTestOptions.Create(
            Pm3ExecutorKind.Native,
            TimeSpan.FromSeconds(12)));

        await pm3.ConnectAsync();

        await EstablishRfCouplingAsync(pm3);

        // 1) Cold read — detect + read (retry if tag coupling is marginal)
        _snapshotBlock5Hex = await ReadBlock5WithDetectRetryAsync(pm3, "cold read");
        TestContext.WriteLine($"Snapshot block5={_snapshotBlock5Hex}");
        LogStep("after cold read");
        Assert.That(LastCommandBatch(), Does.Contain("lf t55 detect"));
        Assert.That(LastCommandBatch(), Does.Contain("lf t55 read -b 5"));
        Assert.That(CacheHitCount(), Is.EqualTo(0));

        // 2) Warm read — cache hit, read only
        var block5Again = await pm3.ReadPage0BlockAsync(5);
        Assert.That(block5Again, Is.EqualTo(_snapshotBlock5Hex));
        LogStep("after cached read");
        Assert.That(CacheHitCount(), Is.EqualTo(1));
        Assert.That(LastCommandBatch(), Is.EqualTo(">>> lf t55 read -b 5"));

        // 3) Dump — cache hit
        var dump1 = await pm3.DumpAsync();
        Assert.That(dump1, Does.Contain("blk").Or.Contain("Page 0"));
        LogStep("after cached dump");
        Assert.That(CacheHitCount(), Is.EqualTo(2));
        Assert.That(LastCommandBatch(), Is.EqualTo(">>> lf t55 dump"));

        // 4) LF tune — invalidates cache
        await pm3.StartLfTuneAsync();
        var mv = await pm3.GetLfTuneLastMilliVoltsAsync();
        Assert.That(mv, Is.GreaterThan(1000u));
        TestContext.WriteLine($"tune={mv} mV");

        // 5) Read after tune — detect runs again
        await pm3.ReadPage0BlockAsync(5);
        LogStep("after read post-tune");
        Assert.That(CacheHitCount(), Is.EqualTo(2));
        Assert.That(LastCommandBatch(), Does.Contain("lf t55 detect"));
        Assert.That(LastCommandBatch(), Does.Contain("lf t55 read -b 5"));

        // 6) Warm read again — cache hit restored
        await pm3.ReadPage0BlockAsync(5);
        LogStep("after second cached read");
        Assert.That(CacheHitCount(), Is.EqualTo(3));
        Assert.That(LastCommandBatch(), Is.EqualTo(">>> lf t55 read -b 5"));

        // 7) Write — invalidates cache
        var ridesBlock = TokenBlockUtils.Encode(TokenBlockUtils.Decode(T55Block.FromHex(_snapshotBlock5Hex)));
        await pm3.WritePage0BlockAsync(5, ridesBlock);
        await pm3.WritePage0BlockAsync(6, ridesBlock);
        LogStep("after write");

        // 8) Read after write — detect runs again
        await pm3.ReadPage0BlockAsync(5);
        LogStep("after read post-write");
        Assert.That(LastCommandBatch(), Does.Contain("lf t55 detect"));

        // 9) Explicit invalidate
        await pm3.ReadPage0BlockAsync(5);
        var hitsBeforeInvalidate = CacheHitCount();
        LogStep("after read establishing cache before invalidate");
        Assert.That(LastCommandBatch(), Is.EqualTo(">>> lf t55 read -b 5"));

        pm3.InvalidateT55DetectCache();
        await pm3.ReadPage0BlockAsync(5);
        LogStep("after explicit invalidate + read");
        Assert.That(CacheHitCount(), Is.EqualTo(hitsBeforeInvalidate));
        Assert.That(LastCommandBatch(), Does.Contain("lf t55 detect"));

        // Restore snapshot
        await pm3.WritePage0BlockAsync(5, T55Block.FromHex(_snapshotBlock5Hex));
        await pm3.WritePage0BlockAsync(6, T55Block.FromHex(_snapshotBlock5Hex));
        TestContext.WriteLine($"Restored block5={_snapshotBlock5Hex}");
    }

    private static async Task<string> ReadBlock5WithDetectRetryAsync(Pm3 pm3, string label)
    {
        Pm3CommandException? last = null;
        for (var attempt = 1; attempt <= 4; attempt++)
        {
            try
            {
                var hex = await pm3.ReadPage0BlockAsync(5);
                TestContext.WriteLine($"{label} attempt {attempt}: block5={hex}");
                return hex;
            }
            catch (Pm3CommandException ex) when (attempt < 4)
            {
                last = ex;
                TestContext.WriteLine($"{label} attempt {attempt} failed: {ex.Message}");
                await EstablishRfCouplingAsync(pm3);
                await Task.Delay(750);
            }
        }

        throw last ?? new Pm3CommandException($"Failed {label} after retries.");
    }

    private static async Task EstablishRfCouplingAsync(Pm3 pm3)
    {
        Pm3CommandException? last = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                await pm3.StartLfTuneAsync();
                var mv = await pm3.GetLfTuneLastMilliVoltsAsync();
                TestContext.WriteLine($"RF tune attempt {attempt}: {mv} mV");
                if (mv > 1000)
                    return;
            }
            catch (Pm3CommandException ex)
            {
                last = ex;
                TestContext.WriteLine($"RF tune attempt {attempt} failed: {ex.Message}");
            }

            await Task.Delay(500);
        }

        throw last ?? new Pm3CommandException("Failed to establish LF coupling before cache test.");
    }

    private string SessionLogText() => File.ReadAllText(Pm3DiagnosticLog.Current.SessionLogPath);

    private string LastCommandBatch()
    {
        var batch = GetCommandBatches(SessionLogText()).LastOrDefault() ?? string.Empty;
        return batch.TrimEnd();
    }

    private int CacheHitCount() =>
        SessionLogText().Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Count(line => line.Contains("T55 detect cache hit", StringComparison.Ordinal));

    private static List<string> GetCommandBatches(string logText) =>
        logText.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.Contains("[SESSION] >>>", StringComparison.Ordinal))
            .Select(line => line[line.IndexOf(">>>", StringComparison.Ordinal)..])
            .ToList();

    private void LogStep(string label)
    {
        TestContext.WriteLine($"--- {label} ---");
        TestContext.WriteLine($"  cache hits: {CacheHitCount()}");
        TestContext.WriteLine($"  last batch: {LastCommandBatch()}");
    }
}
