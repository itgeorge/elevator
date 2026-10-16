using NUnit.Framework;
using Pm3UsbApi.Commands;
using Pm3UsbApi.Native;

namespace Pm3UsbApi.Tests.Native;

[TestFixture]
public class Pm3NativeExecutorRfSettlePolicyTests
{
    [TestCaseSource(nameof(T55Commands))]
    public void RequiresRfSettle_T55Operations_ReturnsTrue(IPm3DeviceCommand command)
    {
        Assert.That(Pm3NativeExecutor.RequiresRfSettle([command]), Is.True);
    }

    [Test]
    public void RequiresRfSettle_TuneAndVersionOnly_ReturnsFalse()
    {
        Assert.That(
            Pm3NativeExecutor.RequiresRfSettle([new LfTuneCommand(), new HwVersionCommand()]),
            Is.False);
    }

    [Test]
    public void RequiresRfSettle_MixedTuneAndT55Batch_ReturnsTrue()
    {
        Assert.That(
            Pm3NativeExecutor.RequiresRfSettle([new LfTuneCommand(), new T55DetectCommand()]),
            Is.True);
    }

    [Test]
    public void RequiresRfSettle_UnknownOperation_IsConservative()
    {
        Assert.That(
            Pm3NativeExecutor.RequiresRfSettle([new CliPassthroughCommand("version")]),
            Is.True);
    }

    private static IEnumerable<IPm3DeviceCommand> T55Commands()
    {
        yield return new T55DetectCommand();
        yield return new T55ReadBlockCommand(0);
        yield return new T55WriteBlockCommand(0, default);
        yield return new T55DumpCommand();
    }
}
