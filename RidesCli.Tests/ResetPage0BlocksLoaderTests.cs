using NUnit.Framework;
using Tokens;

namespace RidesCli.Tests;

[TestFixture]
public class ResetPage0BlocksLoaderTests
{
    [Test]
    public void All_encoding_sequences_have_embedded_reset_images()
    {
        foreach (var sequence in EncodingSequences.All)
        {
            var blocks = ResetPage0BlocksLoader.Load(sequence);

            Assert.That(blocks, Has.Count.EqualTo(8),
                $"Sequence '{sequence.FriendlyName}' reset image '{sequence.ResetImageFileName}' must contain 8 page-0 blocks.");
        }
    }
}
