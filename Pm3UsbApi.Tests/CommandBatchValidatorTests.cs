using NUnit.Framework;
using Pm3UsbApi.Commands;
using Pm3UsbApi.Session;

namespace Pm3UsbApi.Tests;

[TestFixture]
public class CommandBatchValidatorTests
{
    [Test]
    public void Validate_EmptyBatch_Throws()
    {
        Assert.Throws<ArgumentException>(() => CommandBatchValidator.Validate([]));
    }

    [Test]
    public void Validate_LfTuneAlone_Succeeds()
    {
        Assert.DoesNotThrow(() => CommandBatchValidator.Validate([new LfTuneCommand()]));
    }

    [Test]
    public void Validate_LfTuneWithOtherCommands_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            CommandBatchValidator.Validate([new T55DetectCommand(), new LfTuneCommand()]));
    }

    [Test]
    public void Validate_LfTuneWithHwVersion_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            CommandBatchValidator.Validate([new LfTuneCommand(), new HwVersionCommand()]));
    }

    [Test]
    public void Validate_T55DetectAndRead_Succeeds()
    {
        Assert.DoesNotThrow(() =>
            CommandBatchValidator.Validate([new T55DetectCommand(), new T55ReadBlockCommand(0)]));
    }
}
