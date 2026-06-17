using NUnit.Framework;
using Pm3UsbApi;
using Pm3UsbApi.Commands;
using Pm3UsbApi.Parsers;

namespace Pm3UsbApi.Tests;

[TestFixture]
public class DetectParserTests
{
    private static CommandResult ToResult(string output, bool hasErrors = false, string? errorSummary = null) =>
        new()
        {
            Commands = [new T55DetectCommand()],
            OutputLines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(s => s.TrimEnd()).ToList(),
            ExitCode = 0,
            HasErrors = hasErrors,
            ErrorSummary = errorSummary
        };

    [Test]
    public void Parse_DetectSuccess_ReturnsChipFound()
    {
        var result = ToResult(TestFixtures.DetectSuccess);
        var parsed = DetectParser.Parse(result);

        Assert.That(parsed.ChipFound, Is.True);
        Assert.That(parsed.ChipType, Is.EqualTo("T55x7"));
        Assert.That(parsed.Modulation, Is.EqualTo("ASK"));
        Assert.That(parsed.Block0Hex, Is.EqualTo("00323240"));
    }

    [Test]
    public void Parse_DetectNoTag_ReturnsChipNotFound()
    {
        var result = ToResult(TestFixtures.DetectNoTag, hasErrors: true);
        var parsed = DetectParser.Parse(result);

        Assert.That(parsed.ChipFound, Is.False);
        Assert.That(parsed.ChipType, Is.Null);
        Assert.That(parsed.Modulation, Is.Null);
        Assert.That(parsed.Block0Hex, Is.Null);
    }

    [Test]
    public void Parse_DetectSuccessIcemanFormat_ReturnsChipFound()
    {
        var result = ToResult(TestFixtures.DetectSuccessIcemanFormat);
        var parsed = DetectParser.Parse(result);

        Assert.That(parsed.ChipFound, Is.True);
        Assert.That(parsed.ChipType, Is.EqualTo("T55x7"));
        Assert.That(parsed.Modulation, Is.EqualTo("ASK"));
        Assert.That(parsed.Block0Hex, Is.EqualTo("00148040"));
    }

    [Test]
    public void Parse_DetectChipNone_ReturnsChipNotFound()
    {
        var result = ToResult(TestFixtures.DetectChipNone);
        var parsed = DetectParser.Parse(result);

        Assert.That(parsed.ChipFound, Is.False);
        Assert.That(parsed.ChipType, Is.EqualTo("none"));
        Assert.That(parsed.Modulation, Is.EqualTo("unknown"));
    }

    [Test]
    public void Parse_ThrowsOnNull()
    {
        Assert.Throws<ArgumentNullException>(() => DetectParser.Parse(null!));
    }
}
