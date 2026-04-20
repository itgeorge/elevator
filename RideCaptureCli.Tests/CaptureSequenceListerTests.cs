using NUnit.Framework;

namespace RideCaptureCli.Tests;

public class CaptureSequenceListerTests
{
    [Test]
    public void Build_groups_by_sequence_and_sorts_by_latest_timestamp_descending()
    {
        var records = new List<CaptureRecord>
        {
            new()
            {
                Timestamp = "2026-04-20T10:00:00.0000000+03:00",
                SequenceId = "SEQ-A",
                TokenId = "TOKEN-A",
                TrackedCount = 100,
                RealRideCount = 98,
                Block5 = "AAAA",
                Block6 = "AAAA",
                Warnings = string.Empty
            },
            new()
            {
                Timestamp = "2026-04-20T10:05:00.0000000+03:00",
                SequenceId = "SEQ-B",
                TokenId = "TOKEN-B",
                TrackedCount = 50,
                RealRideCount = null,
                Block5 = "BBBB",
                Block6 = "BBBB",
                Warnings = "UNKNOWN_TOKEN"
            },
            new()
            {
                Timestamp = "2026-04-20T10:10:00.0000000+03:00",
                SequenceId = "SEQ-A",
                TokenId = "TOKEN-A",
                TrackedCount = 99,
                RealRideCount = 97,
                Block5 = "AAAB",
                Block6 = "AAAB",
                Warnings = string.Empty
            }
        };

        var lister = new CaptureSequenceLister();
        var summaries = lister.Build(records);

        Assert.That(summaries, Has.Count.EqualTo(2));
        Assert.That(summaries[0].SequenceId, Is.EqualTo("SEQ-A"));
        Assert.That(summaries[0].RowCount, Is.EqualTo(2));
        Assert.That(summaries[0].LatestTrackedCount, Is.EqualTo(99));
        Assert.That(summaries[0].LatestRealRideCount, Is.EqualTo(97));
        Assert.That(summaries[0].LatestState, Is.EqualTo("AAAB-AAAB"));

        Assert.That(summaries[1].SequenceId, Is.EqualTo("SEQ-B"));
        Assert.That(summaries[1].LatestWarnings, Is.EqualTo("UNKNOWN_TOKEN"));
    }
}
