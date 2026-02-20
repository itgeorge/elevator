using NUnit.Framework;
using Pm3UsbApi.Parsers;

namespace Pm3UsbApi.Tests;

[TestFixture]
public class OutputParserTests
{
    [Test]
    public void StripAnsi_RemovesColorCodes()
    {
        var stripped = OutputParser.StripAnsi(TestFixtures.WithAnsiCodes);
        Assert.That(stripped, Is.EqualTo(TestFixtures.WithAnsiStripped));
    }

    [Test]
    public void StripAnsi_NullOrEmpty_ReturnsSame()
    {
        Assert.That(OutputParser.StripAnsi(""), Is.EqualTo(""));
        Assert.That(OutputParser.StripAnsi(null!), Is.Null);
    }

    [Test]
    public void DetectErrors_FindsErrorLines()
    {
        var lines = new[] { "[+] ok", "[!] error one", "[-] error two" };
        var (hasErrors, summary) = OutputParser.DetectErrors(lines);

        Assert.That(hasErrors, Is.True);
        Assert.That(summary, Does.Contain("[!]"));
        Assert.That(summary, Does.Contain("[-]"));
    }

    [Test]
    public void DetectErrors_FindsErrorAndFailedKeywords()
    {
        var lines = new[] { "[+] ok", "error: something", "failed to connect" };
        var (hasErrors, summary) = OutputParser.DetectErrors(lines);

        Assert.That(hasErrors, Is.True);
        Assert.That(summary, Does.Contain("error"));
        Assert.That(summary, Does.Contain("failed"));
    }

    [Test]
    public void DetectErrors_NonErrorLines_NotFlagged()
    {
        var lines = new[] { "[+] ok", "[=] 100 mV", "normal line" };
        var (hasErrors, _) = OutputParser.DetectErrors(lines);

        Assert.That(hasErrors, Is.False);
    }

    [Test]
    public void DetectErrors_EmptyList_ReturnsNoErrors()
    {
        var (hasErrors, summary) = OutputParser.DetectErrors(Array.Empty<string>());

        Assert.That(hasErrors, Is.False);
        Assert.That(summary, Is.Null);
    }

    [Test]
    public void DetectOfflineMode_DetectsOfflineModeMessage()
    {
        var lines = TestFixtures.HwVersionOffline.Split('\n');
        Assert.That(OutputParser.DetectOfflineMode(lines), Is.True);
    }

    [Test]
    public void DetectOfflineMode_DetectsOfflinePrompt()
    {
        var lines = new[] { "[offline|script] pm3 --> hw version" };
        Assert.That(OutputParser.DetectOfflineMode(lines), Is.True);
    }

    [Test]
    public void DetectOfflineMode_ConnectedOutput_ReturnsFalse()
    {
        var lines = TestFixtures.HwVersion.Split('\n');
        Assert.That(OutputParser.DetectOfflineMode(lines), Is.False);
    }

    [Test]
    public void DetectOfflineMode_EmptyList_ReturnsFalse()
    {
        Assert.That(OutputParser.DetectOfflineMode(Array.Empty<string>()), Is.False);
    }
}
