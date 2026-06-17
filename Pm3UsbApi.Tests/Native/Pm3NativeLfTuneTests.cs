using NUnit.Framework;
using Pm3UsbApi.Native;
using Pm3UsbApi.Native.Protocol;

namespace Pm3UsbApi.Tests.Native;

[TestFixture]
public class Pm3NativeLfTuneTests
{
  [Test]
  public void MeasurePeakMillivolts_TakesExactlySampleCountMeasurements()
  {
    var measureCalls = 0;
    var send = CreateSendDelegate(
      onMeasure: () => measureCalls++,
      measureVoltages: [100u, 5000u, 2000u]);

    var peak = Pm3NativeLfTune.MeasurePeakMillivolts(
      send,
      sampleCount: 3,
      timeout: TimeSpan.FromSeconds(3));

    Assert.That(measureCalls, Is.EqualTo(3));
    Assert.That(peak, Is.EqualTo(5000u));
  }

  [Test]
  public void MeasurePeakMillivolts_SingleSample_ReturnsThatReading()
  {
    var measureCalls = 0;
    var send = CreateSendDelegate(
      onMeasure: () => measureCalls++,
      measureVoltages: [12345u]);

    var peak = Pm3NativeLfTune.MeasurePeakMillivolts(
      send,
      sampleCount: 1,
      timeout: TimeSpan.FromSeconds(3));

    Assert.That(measureCalls, Is.EqualTo(1));
    Assert.That(peak, Is.EqualTo(12345u));
  }

  [Test]
  public void MeasurePeakMillivolts_StopsEarlyOnAbort()
  {
    var measureCalls = 0;
    var send = CreateSendDelegate(
      onMeasure: () => measureCalls++,
      measureVoltages: [100u, 200u]);

    var peak = Pm3NativeLfTune.MeasurePeakMillivolts(
      send,
      sampleCount: 20,
      timeout: TimeSpan.FromSeconds(3));

    Assert.That(measureCalls, Is.EqualTo(2));
    Assert.That(peak, Is.EqualTo(200u));
  }

  [Test]
  public void MeasurePeakMillivolts_StopsOnTimeoutBeforeReachingSampleCount()
  {
    var measureCalls = 0;
    var tick = 0L;
    var send = CreateSendDelegate(
      onMeasure: () =>
      {
        measureCalls++;
        tick += 30;
      },
      measureVoltages: [1000u, 2000u, 3000u, 4000u, 5000u]);

    var peak = Pm3NativeLfTune.MeasurePeakMillivolts(
      send,
      sampleCount: 20,
      timeout: TimeSpan.FromMilliseconds(50),
      tickNow: () => tick);

    Assert.That(measureCalls, Is.EqualTo(2));
    Assert.That(peak, Is.EqualTo(2000u));
  }

  [Test]
  public void MeasurePeakMillivolts_NoSuccessfulSamples_Throws()
  {
    var send = CreateSendDelegate(
      onMeasure: null,
      measureVoltages: []);

    Assert.Throws<InvalidOperationException>(() =>
      Pm3NativeLfTune.MeasurePeakMillivolts(
        send,
        sampleCount: 3,
        timeout: TimeSpan.FromSeconds(3)));
  }

  [Test]
  public void MeasurePeakMillivolts_SendsInitAndShutdown()
  {
    var phases = new List<byte>();
    var send = CreateSendDelegate(
      onMeasure: null,
      measureVoltages: [42u],
      onPayload: payload => phases.Add(payload[0]));

    _ = Pm3NativeLfTune.MeasurePeakMillivolts(send, sampleCount: 1, timeout: TimeSpan.FromSeconds(3));

    Assert.That(phases, Is.EqualTo(new byte[] { 1, 2, 3 }));
  }

  private static Func<byte[], Pm3ResponseFrame> CreateSendDelegate(
    Action? onMeasure,
    IReadOnlyList<uint> measureVoltages,
    Action<byte[]>? onPayload = null)
  {
    var measureIndex = 0;

    return payload =>
    {
      onPayload?.Invoke(payload);
      var phase = payload[0];
      if (phase == 1)
        return SuccessFrame();

      if (phase == 3)
        return SuccessFrame();

      if (phase != 2)
        throw new InvalidOperationException($"Unexpected payload phase {phase}.");

      if (measureIndex >= measureVoltages.Count)
        return AbortedFrame();

      onMeasure?.Invoke();
      var voltage = measureVoltages[measureIndex++];
      return VoltageFrame(voltage);
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
