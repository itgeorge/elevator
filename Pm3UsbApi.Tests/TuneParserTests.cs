using NUnit.Framework;
using Pm3UsbApi;
using Pm3UsbApi.Parsers;

namespace Pm3UsbApi.Tests;

[TestFixture]
public class TuneParserTests
{
    private static CommandResult ToResult(string output) =>
        new()
        {
            Commands = ["lf tune"],
            OutputLines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(s => s.TrimEnd()).ToList(),
            ExitCode = 0,
            HasErrors = false
        };

    [Test]
    public void Parse_TuneSuccess_ReturnsPeakMv()
    {
        var result = ToResult(TestFixtures.TuneSuccess);
        var parsed = TuneParser.Parse(result);

        Assert.That(parsed.Success, Is.True);
        Assert.That(parsed.PeakMilliVolts, Is.EqualTo(60276));
    }

    [Test]
    public void Parse_TuneMultipleMv_UsesLast()
    {
        var result = ToResult(TestFixtures.TuneMultipleMv);
        var parsed = TuneParser.Parse(result);

        Assert.That(parsed.Success, Is.True);
        Assert.That(parsed.PeakMilliVolts, Is.EqualTo(60276));
    }

    [Test]
    public void Parse_TuneNoMv_ReturnsFailure()
    {
        var result = ToResult(TestFixtures.TuneNoMv);
        var parsed = TuneParser.Parse(result);

        Assert.That(parsed.Success, Is.False);
        Assert.That(parsed.PeakMilliVolts, Is.EqualTo(0));
    }

    [Test]
    public void Parse_ThrowsOnNull()
    {
        Assert.Throws<ArgumentNullException>(() => TuneParser.Parse(null!));
    }
}
