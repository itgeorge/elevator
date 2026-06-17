using NUnit.Framework;
using Pm3UsbApi;
using Pm3UsbApi.Commands;
using Pm3UsbApi.Parsers;
using Pm3UsbApi.Session;
using Tokens;

namespace Pm3UsbApi.Tests.Session;

[TestFixture]
public class Pm3T55DetectCacheTests
{
    private static readonly DateTime T0 = new(2026, 6, 18, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DetectResult SampleDetect = new(true, "T55x7", "ASK", "00148040");

    [Test]
    public void ShouldSkipDetect_WhenNoPriorDetect_ReturnsFalse()
    {
        var cache = new Pm3T55DetectCache();
        Assert.That(cache.ShouldSkipDetect(
            Pm3ExecutorKind.Native,
            "/dev/cu.usbmodem1",
            new T55ReadBlockCommand(5),
            T0), Is.False);
    }

    [Test]
    public void ShouldSkipDetect_AfterDetectAndReadFollowOn_ReturnsTrue_ForNative()
    {
        var cache = new Pm3T55DetectCache();
        cache.RecordDetect(Pm3ExecutorKind.Native, "/dev/cu.usbmodem1", SampleDetect, T0);

        Assert.That(cache.ShouldSkipDetect(
            Pm3ExecutorKind.Native,
            "/dev/cu.usbmodem1",
            new T55ReadBlockCommand(5),
            T0.AddSeconds(10)), Is.True);
    }

    [Test]
    public void ShouldSkipDetect_ForProcessExecutor_AlwaysReturnsFalse()
    {
        var cache = new Pm3T55DetectCache();
        cache.RecordDetect(Pm3ExecutorKind.Native, "/dev/cu.usbmodem1", SampleDetect, T0);

        Assert.That(cache.ShouldSkipDetect(
            Pm3ExecutorKind.Process,
            "/dev/cu.usbmodem1",
            new T55ReadBlockCommand(5),
            T0.AddSeconds(1)), Is.False);
    }

    [Test]
    public void ShouldSkipDetect_WhenFollowOnIsDetect_ReturnsFalse()
    {
        var cache = new Pm3T55DetectCache();
        cache.RecordDetect(Pm3ExecutorKind.Native, "/dev/cu.usbmodem1", SampleDetect, T0);

        Assert.That(cache.ShouldSkipDetect(
            Pm3ExecutorKind.Native,
            "/dev/cu.usbmodem1",
            new T55DetectCommand(),
            T0.AddSeconds(1)), Is.False);
    }

    [Test]
    public void ShouldSkipDetect_AfterLfTuneInvalidation_ReturnsFalse()
    {
        var cache = new Pm3T55DetectCache();
        cache.RecordDetect(Pm3ExecutorKind.Native, "/dev/cu.usbmodem1", SampleDetect, T0);
        cache.InvalidateForLfTune();

        Assert.That(cache.ShouldSkipDetect(
            Pm3ExecutorKind.Native,
            "/dev/cu.usbmodem1",
            new T55ReadBlockCommand(5),
            T0.AddSeconds(1)), Is.False);
    }

    [Test]
    public void ShouldSkipDetect_AfterWriteInvalidation_ReturnsFalse()
    {
        var cache = new Pm3T55DetectCache();
        cache.RecordDetect(Pm3ExecutorKind.Native, "/dev/cu.usbmodem1", SampleDetect, T0);
        cache.InvalidateForWrite();

        Assert.That(cache.ShouldSkipDetect(
            Pm3ExecutorKind.Native,
            "/dev/cu.usbmodem1",
            new T55ReadBlockCommand(5),
            T0.AddSeconds(1)), Is.False);
    }

    [Test]
    public void ShouldSkipDetect_AfterTtlElapsed_ReturnsFalse()
    {
        var cache = new Pm3T55DetectCache(TimeSpan.FromSeconds(30));
        cache.RecordDetect(Pm3ExecutorKind.Native, "/dev/cu.usbmodem1", SampleDetect, T0);

        Assert.That(cache.ShouldSkipDetect(
            Pm3ExecutorKind.Native,
            "/dev/cu.usbmodem1",
            new T55ReadBlockCommand(5),
            T0.AddSeconds(30)), Is.False);

        Assert.That(cache.ShouldSkipDetect(
            Pm3ExecutorKind.Native,
            "/dev/cu.usbmodem1",
            new T55ReadBlockCommand(5),
            T0.AddSeconds(29)), Is.True);
    }

    [Test]
    public void ShouldSkipDetect_WhenPortChanges_ReturnsFalse()
    {
        var cache = new Pm3T55DetectCache();
        cache.RecordDetect(Pm3ExecutorKind.Native, "/dev/cu.usbmodem1", SampleDetect, T0);

        Assert.That(cache.ShouldSkipDetect(
            Pm3ExecutorKind.Native,
            "/dev/cu.usbmodem2",
            new T55ReadBlockCommand(5),
            T0.AddSeconds(1)), Is.False);
    }

    [Test]
    public void InvalidateForReadFailure_ClearsCache()
    {
        var cache = new Pm3T55DetectCache();
        cache.RecordDetect(Pm3ExecutorKind.Native, "/dev/cu.usbmodem1", SampleDetect, T0);
        cache.InvalidateForReadFailure();

        Assert.That(cache.ShouldSkipDetect(
            Pm3ExecutorKind.Native,
            "/dev/cu.usbmodem1",
            new T55ReadBlockCommand(5),
            T0.AddSeconds(1)), Is.False);
    }

    [Test]
    public void InvalidateForBlock0Mismatch_ClearsCache()
    {
        var cache = new Pm3T55DetectCache();
        cache.RecordDetect(Pm3ExecutorKind.Native, "/dev/cu.usbmodem1", SampleDetect, T0);
        cache.InvalidateForBlock0Mismatch("DEADBEEF");

        Assert.That(cache.ShouldSkipDetect(
            Pm3ExecutorKind.Native,
            "/dev/cu.usbmodem1",
            new T55ReadBlockCommand(0),
            T0.AddSeconds(1)), Is.False);
    }

    [Test]
    public void TryRecordFromBatchResult_RecordsWhenDetectSucceeded()
    {
        var cache = new Pm3T55DetectCache();
        var result = new CommandResult
        {
            Commands = [new T55DetectCommand(), new T55ReadBlockCommand(5)],
            OutputLines =
            [
                "[=] Chip Type: T55x7",
                "[=] Modulation: ASK",
                "[=] Block0: 0x00148040",
                "[+] Block 5: DEADBEEF",
            ],
            ExitCode = 0,
            HasErrors = false,
        };

        cache.TryRecordFromBatchResult(
            Pm3ExecutorKind.Native,
            "/dev/cu.usbmodem1",
            result.Commands,
            result,
            T0);

        Assert.That(cache.ShouldSkipDetect(
            Pm3ExecutorKind.Native,
            "/dev/cu.usbmodem1",
            new T55DumpCommand(),
            T0.AddSeconds(5)), Is.True);
    }

    [Test]
    public void TryRecordFromBatchResult_DoesNotRecordFailedDetect()
    {
        var cache = new Pm3T55DetectCache();
        var result = new CommandResult
        {
            Commands = [new T55DetectCommand()],
            OutputLines = ["[!] Could not detect modulation automatically."],
            ExitCode = 1,
            HasErrors = true,
        };

        cache.TryRecordFromBatchResult(
            Pm3ExecutorKind.Native,
            "/dev/cu.usbmodem1",
            result.Commands,
            result,
            T0);

        Assert.That(cache.ShouldSkipDetect(
            Pm3ExecutorKind.Native,
            "/dev/cu.usbmodem1",
            new T55ReadBlockCommand(5),
            T0), Is.False);
    }

    [Test]
    public void BuildT55CommandBatch_UsesCacheWhenValid()
    {
        var cache = new Pm3T55DetectCache();
        cache.RecordDetect(Pm3ExecutorKind.Native, "COM4", SampleDetect, T0);

        var batch = Pm3T55DetectCache.BuildT55CommandBatch(
            cache,
            Pm3ExecutorKind.Native,
            "COM4",
            new T55ReadBlockCommand(5),
            T0.AddSeconds(5));

        Assert.That(batch, Has.Count.EqualTo(1));
        Assert.That(batch[0], Is.InstanceOf<T55ReadBlockCommand>());
    }

    [Test]
    public void BuildT55CommandBatch_PrependsDetectWhenCacheMiss()
    {
        var cache = new Pm3T55DetectCache();

        var batch = Pm3T55DetectCache.BuildT55CommandBatch(
            cache,
            Pm3ExecutorKind.Native,
            "COM4",
            new T55ReadBlockCommand(5),
            T0);

        Assert.That(batch, Has.Count.EqualTo(2));
        Assert.That(batch[0], Is.InstanceOf<T55DetectCommand>());
        Assert.That(batch[1], Is.InstanceOf<T55ReadBlockCommand>());
    }
}
