using NUnit.Framework;
using Pm3UsbApi.Diagnostics;

namespace Pm3UsbApi.Tests.Diagnostics;

[TestFixture]
public class Pm3DiagnosticLogTests
{
    private string? _priorLogDir;

    [SetUp]
    public void SetUp()
    {
        _priorLogDir = Environment.GetEnvironmentVariable("PM3_LOG_DIR");
        Pm3DiagnosticLog.ResetForTesting();
    }

    [TearDown]
    public void TearDown()
    {
        Pm3DiagnosticLog.ResetForTesting();
        Environment.SetEnvironmentVariable("PM3_LOG_DIR", _priorLogDir);
    }

    [Test]
    public void CreateNew_WritesUnderConfiguredBaseDirectory()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "elevator-tests", Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("PM3_LOG_DIR", baseDir);

        var log = Pm3DiagnosticLog.CreateNew();
        log.WriteSession("hello session");
        log.WriteError("hello error");
        log.Dispose();

        Assert.That(Directory.Exists(baseDir), Is.True);
        Assert.That(File.Exists(log.SessionLogPath), Is.True);
        Assert.That(File.Exists(log.ErrorsLogPath), Is.True);
        Assert.That(log.SessionLogPath, Does.StartWith(baseDir));
        Assert.That(File.ReadAllText(log.SessionLogPath), Does.Contain("hello session"));
        Assert.That(File.ReadAllText(log.ErrorsLogPath), Does.Contain("hello error"));

        try { Directory.Delete(baseDir, recursive: true); } catch { /* best effort */ }
    }

    [Test]
    public void WriteError_IncludesExceptionDetails()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "elevator-tests", Guid.NewGuid().ToString("N"));
        var log = Pm3DiagnosticLog.CreateNew(baseDir);
        var ex = new InvalidOperationException("boom");
        log.WriteError("failed", ex);
        log.Dispose();

        var text = File.ReadAllText(log.ErrorsLogPath);
        Assert.That(text, Does.Contain("failed"));
        Assert.That(text, Does.Contain("InvalidOperationException"));
        Assert.That(text, Does.Contain("boom"));

        try { Directory.Delete(baseDir, recursive: true); } catch { /* best effort */ }
    }

    [Test]
    public void WriteNativeTrace_OnlyWhenEnabled()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "elevator-tests", Guid.NewGuid().ToString("N"));
        var prior = Environment.GetEnvironmentVariable("PM3_NATIVE_TRACE");
        try
        {
            Environment.SetEnvironmentVariable("PM3_NATIVE_TRACE", null);
            var disabled = Pm3DiagnosticLog.CreateNew(baseDir);
            disabled.WriteNativeTrace("should not appear");
            disabled.Dispose();
            Assert.That(disabled.NativeTraceLogPath, Is.Null);

            Environment.SetEnvironmentVariable("PM3_NATIVE_TRACE", "1");
            var enabled = Pm3DiagnosticLog.CreateNew(baseDir);
            enabled.WriteNativeTrace("trace line");
            enabled.Dispose();
            Assert.That(enabled.NativeTraceLogPath, Is.Not.Null);
            Assert.That(File.ReadAllText(enabled.NativeTraceLogPath!), Does.Contain("trace line"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PM3_NATIVE_TRACE", prior);
            try { Directory.Delete(baseDir, recursive: true); } catch { /* best effort */ }
        }
    }

    [Test]
    public void LogFatal_DoesNotThrow_WhenCurrentDisposed()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "elevator-tests", Guid.NewGuid().ToString("N"));
        var log = Pm3DiagnosticLog.CreateNew(baseDir);
        Pm3DiagnosticLog.ReplaceCurrentForTesting(log);
        log.Dispose();

        Assert.DoesNotThrow(() => Pm3DiagnosticLog.LogFatal(new Exception("x"), "test"));

        try { Directory.Delete(baseDir, recursive: true); } catch { /* best effort */ }
    }
}
