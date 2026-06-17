using NUnit.Framework;
using Pm3UsbApi;
using Pm3UsbApi.Commands;
using Pm3UsbApi.Parsers;
using Tokens;

namespace Pm3UsbApi.Tests;

[TestFixture]
public class DumpParserTests
{
    private static CommandResult ToResult(string output) =>
        new()
        {
            Commands = [new T55DumpCommand()],
            OutputLines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(s => s.TrimEnd()).ToList(),
            ExitCode = 0,
            HasErrors = false
        };

    [Test]
    public void Parse_DumpSuccess_Returns8Blocks()
    {
        var result = ToResult(TestFixtures.DumpSuccess);
        var parsed = DumpParser.Parse(result);

        Assert.That(parsed.Success, Is.True);
        Assert.That(parsed.Blocks, Has.Count.EqualTo(8));
    }

    [Test]
    public void Parse_DumpSuccess_BlockValuesMatch()
    {
        var result = ToResult(TestFixtures.DumpSuccess);
        var parsed = DumpParser.Parse(result);

        Assert.That(parsed.Blocks[0].ToHex(), Is.EqualTo("00107060"));
        Assert.That(parsed.Blocks[1].ToHex(), Is.EqualTo("01242422"));
        Assert.That(parsed.Blocks[2].ToHex(), Is.EqualTo("BA3A3B1B"));
        Assert.That(parsed.Blocks[7].ToHex(), Is.EqualTo("44444444"));
    }

    [Test]
    public void Parse_DumpSuccess_RawOutputPreserved()
    {
        var result = ToResult(TestFixtures.DumpSuccess);
        var parsed = DumpParser.Parse(result);

        Assert.That(parsed.RawOutput, Does.Contain("blk | hex data"));
    }

    [Test]
    public void Parse_EmptyOutput_ReturnsFailure()
    {
        var result = ToResult("");
        var parsed = DumpParser.Parse(result);

        Assert.That(parsed.Success, Is.False);
        Assert.That(parsed.Blocks, Is.Empty);
    }

    [Test]
    public void Parse_ThrowsOnNull()
    {
        Assert.Throws<ArgumentNullException>(() => DumpParser.Parse(null!));
    }

    [Test]
    public void Parse_IcemanFormatWithPlusPrefix_Returns8Blocks()
    {
        var result = ToResult(TestFixtures.DumpSuccessIceman);
        var parsed = DumpParser.Parse(result);

        Assert.That(parsed.Success, Is.True);
        Assert.That(parsed.Blocks, Has.Count.EqualTo(8));
        Assert.That(parsed.Blocks[0].ToHex(), Is.EqualTo("00148040"));
        Assert.That(parsed.Blocks[5].ToHex(), Is.EqualTo("CCC61159"));
    }
}
