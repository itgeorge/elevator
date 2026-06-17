using NUnit.Framework;
using Pm3UsbApi.Diagnostics;
using Pm3UsbApi.Native;
using Pm3UsbApi.Native.Protocol;

namespace Pm3UsbApi.Tests.Diagnostics;

[TestFixture]
public class LfTuneProbeSessionTests
{
    [Test]
    public void MeasurePeakMillivolts_WithActiveProbe_RecordsSamplesAndWritesFiles()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            using var probe = LfTuneProbeSession.Begin("unit-test", sampleCountRequested: 3, TimeSpan.FromSeconds(3));

            var peak = Pm3NativeLfTune.MeasurePeakMillivolts(
                CreateSendDelegate([100u, 5000u, 2000u]),
                sampleCount: 3,
                timeout: TimeSpan.FromSeconds(3));

            Assert.That(peak, Is.EqualTo(5000u));
            Assert.That(probe.Samples, Has.Count.EqualTo(3));
            Assert.That(probe.Samples[2].RunningPeakMv, Is.EqualTo(5000u));

            var jsonPath = probe.WriteResults(tempDir);
            Assert.That(File.Exists(jsonPath), Is.True);
            Assert.That(File.Exists(Path.ChangeExtension(jsonPath, ".csv")), Is.True);
            Assert.That(File.ReadAllText(jsonPath), Does.Contain("\"label\": \"unit-test\""));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    private static Func<byte[], Pm3ResponseFrame> CreateSendDelegate(IReadOnlyList<uint> measureVoltages)
    {
        var measureIndex = 0;
        return payload =>
        {
            var phase = payload[0];
            if (phase is 1 or 3)
                return SuccessFrame();

            if (measureIndex >= measureVoltages.Count)
                return AbortedFrame();

            return VoltageFrame(measureVoltages[measureIndex++]);
        };
    }

    private static Pm3ResponseFrame SuccessFrame() => new()
    {
        Command = Pm3CommandCodes.CmdMeasureAntennaTuningLf,
        Status = Pm3CommandCodes.Pm3Success,
        Reason = 0,
        IsNg = true,
        Data = [],
    };

    private static Pm3ResponseFrame AbortedFrame() => new()
    {
        Command = Pm3CommandCodes.CmdMeasureAntennaTuningLf,
        Status = Pm3CommandCodes.Pm3EopAborted,
        Reason = 0,
        IsNg = true,
        Data = [],
    };

    private static Pm3ResponseFrame VoltageFrame(uint millivolts) => new()
    {
        Command = Pm3CommandCodes.CmdMeasureAntennaTuningLf,
        Status = Pm3CommandCodes.Pm3Success,
        Reason = 0,
        IsNg = true,
        Data = BitConverter.GetBytes(millivolts),
    };
}
