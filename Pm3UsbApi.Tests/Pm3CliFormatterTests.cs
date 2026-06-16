using NUnit.Framework;
using Pm3UsbApi.Commands;
using Pm3UsbApi.Execution;
using Tokens;

namespace Pm3UsbApi.Tests;

[TestFixture]
public class Pm3CliFormatterTests
{
    [Test]
    public void Format_HwVersion_ReturnsCliString()
    {
        Assert.That(Pm3CliFormatter.Format(new HwVersionCommand()), Is.EqualTo("hw version"));
    }

    [Test]
    public void Format_LfTune_ReturnsCliString()
    {
        Assert.That(Pm3CliFormatter.Format(new LfTuneCommand()), Is.EqualTo("lf tune"));
    }

    [Test]
    public void Format_T55Detect_ReturnsCliString()
    {
        Assert.That(Pm3CliFormatter.Format(new T55DetectCommand()), Is.EqualTo("lf t55 detect"));
    }

    [Test]
    public void Format_T55ReadBlock_ReturnsCliString()
    {
        Assert.That(Pm3CliFormatter.Format(new T55ReadBlockCommand(5)), Is.EqualTo("lf t55 read -b 5"));
    }

    [Test]
    public void Format_T55WriteBlock_ReturnsCliString()
    {
        var command = new T55WriteBlockCommand(5, T55Block.FromHex("DEADBEEF"));
        Assert.That(Pm3CliFormatter.Format(command), Is.EqualTo("lf t55 write -b 5 -d DEADBEEF"));
    }

    [Test]
    public void Format_T55Dump_ReturnsCliString()
    {
        Assert.That(Pm3CliFormatter.Format(new T55DumpCommand()), Is.EqualTo("lf t55 dump"));
    }

    [Test]
    public void Format_CliPassthrough_ReturnsRawText()
    {
        Assert.That(Pm3CliFormatter.Format(new CliPassthroughCommand("lf search")), Is.EqualTo("lf search"));
    }

    [Test]
    public void FormatBatch_JoinsWithSemicolon()
    {
        var batch = new IPm3DeviceCommand[] { new T55DetectCommand(), new T55ReadBlockCommand(0) };
        Assert.That(Pm3CliFormatter.FormatBatch(batch), Is.EqualTo("lf t55 detect; lf t55 read -b 0"));
    }
}
