using NUnit.Framework;
using Pm3UsbApi;
using Pm3UsbApi.Commands;
using Pm3UsbApi.Parsers;

namespace Pm3UsbApi.Tests;

[TestFixture]
public class BlockReadParserTests
{
    private static CommandResult ToResult(string output) =>
        new()
        {
            Commands = [new T55ReadBlockCommand(0)],
            OutputLines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(s => s.TrimEnd()).ToList(),
            ExitCode = 0,
            HasErrors = false
        };

    [Test]
    public void Parse_ReadBlock0_TableFormat_ReturnsHex()
    {
        var result = ToResult(TestFixtures.ReadBlock0);
        var parsed = BlockReadParser.Parse(result, 0);

        Assert.That(parsed.Success, Is.True);
        Assert.That(parsed.HexData, Is.EqualTo("00148040"));
    }

    [Test]
    public void Parse_ReadBlock2_ColonFormat_ReturnsHex()
    {
        var result = ToResult(TestFixtures.ReadBlock2);
        var parsed = BlockReadParser.Parse(result, 2);

        Assert.That(parsed.Success, Is.True);
        Assert.That(parsed.HexData, Is.EqualTo("01242422"));
    }

    [Test]
    public void Parse_ReadBlockFailed_ReturnsFailure()
    {
        var result = ToResult(TestFixtures.ReadBlockFailed);
        var parsed = BlockReadParser.Parse(result, 0);

        Assert.That(parsed.Success, Is.False);
        Assert.That(parsed.HexData, Is.Null);
    }

    [Test]
    public void Parse_ThrowsOnNull()
    {
        Assert.Throws<ArgumentNullException>(() => BlockReadParser.Parse(null!, 0));
    }

    [Test]
    public void Parse_ThrowsOnInvalidBlock()
    {
        var result = ToResult(TestFixtures.ReadBlock0);
        Assert.Throws<ArgumentOutOfRangeException>(() => BlockReadParser.Parse(result, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => BlockReadParser.Parse(result, 8));
    }
}
