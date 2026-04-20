using NUnit.Framework;

namespace RideCaptureCli.Tests;

public class SequenceIdGeneratorTests
{
    [Test]
    public void CreateNext_uses_token_prefix_timestamp_and_incrementing_counter()
    {
        var generator = new SequenceIdGenerator();
        var existing = new List<CaptureRecord>
        {
            new() { TokenId = "D3FE005D-522BC69D-650432F5-650432F5", SequenceId = "D3FE005D-20260420-163621-s01" },
            new() { TokenId = "D3FE005D-522BC69D-650432F5-650432F5", SequenceId = "D3FE005D-20260421-163621-s02" },
            new() { TokenId = "AAAAAAAA-BBBBBBBB-CCCCCCCC-DDDDDDDD", SequenceId = "AAAAAAAA-20260420-163621-s07" }
        };

        var value = generator.CreateNext("D3FE005D-522BC69D-650432F5-650432F5", existing, new DateTimeOffset(2026, 4, 22, 9, 30, 11, TimeSpan.FromHours(3)));

        Assert.That(value, Is.EqualTo("D3FE005D-20260422-093011-s03"));
    }
}
