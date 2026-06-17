using NUnit.Framework;
using Pm3UsbApi;
using Pm3UsbApi.Commands;
using Pm3UsbApi.Native;
using Pm3UsbApi.Parsers;

namespace Pm3UsbApi.Tests.Native;

[TestFixture]
public class Pm3NativeOutputBuilderTests
{
    [Test]
    public void BuildWriteBlockLines_HasNoErrorMarkers()
    {
        var lines = Pm3NativeOutputBuilder.BuildWriteBlockLines(5, 0xDEADBEEF);

        Assert.That(lines, Has.Count.GreaterThan(0));
        Assert.That(string.Join('\n', lines), Does.Not.Contain("[!]"));
        Assert.That(string.Join('\n', lines), Does.Not.Contain("[-]"));
        Assert.That(string.Join('\n', lines), Does.Contain("DEADBEEF"));
    }

    [Test]
    public void BuildDumpLines_ParseWithDumpParser_ReturnsEightBlocks()
    {
        var values = new uint[]
        {
            0x00148040,
            0x9BFE0062,
            0x5BA4A3DE,
            0xD5D1D713,
            0xD5D1D713,
            0xCCC61159,
            0xCCC61159,
            0x00000000,
        };

        var lines = Pm3NativeOutputBuilder.BuildDumpLines(values);
        var parsed = DumpParser.Parse(new CommandResult
        {
            Commands = [new T55DumpCommand()],
            OutputLines = lines,
            ExitCode = 0,
            HasErrors = false,
        });

        Assert.That(parsed.Success, Is.True);
        Assert.That(parsed.Blocks, Has.Count.EqualTo(8));
        Assert.That(parsed.Blocks[0].ToHex(), Is.EqualTo("00148040"));
        Assert.That(parsed.Blocks[5].ToHex(), Is.EqualTo("CCC61159"));
    }
}
