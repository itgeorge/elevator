using NUnit.Framework;
using Tokens;

namespace RidesCli.Tests;

[TestFixture]
public class ResetPage0BlocksLoaderTests
{
    [Test]
    public void All_resettable_profiles_have_embedded_reset_images()
    {
        foreach (var profile in TokenIdentityProfiles.Resettable)
        {
            var blocks = ResetPage0BlocksLoader.Load(profile);

            Assert.That(blocks, Has.Count.EqualTo(8),
                $"Profile '{profile.FriendlyName}' reset image '{profile.ResetImageFileName}' must contain 8 page-0 blocks.");
            Assert.That(blocks[1], Is.EqualTo(profile.Block1),
                $"Profile '{profile.FriendlyName}' reset image block 1 must match profile identity.");
            Assert.That(blocks[2], Is.EqualTo(profile.Block2),
                $"Profile '{profile.FriendlyName}' reset image block 2 must match profile identity.");
            Assert.That(blocks[3], Is.EqualTo(profile.Block3),
                $"Profile '{profile.FriendlyName}' reset image block 3 must match profile identity.");
            Assert.That(blocks[4], Is.EqualTo(profile.Block4),
                $"Profile '{profile.FriendlyName}' reset image block 4 must match profile identity.");
        }
    }

    [Test]
    public void Neptune_reset_image_is_the_canonical_zero_ride_capture()
    {
        var blocks = ResetPage0BlocksLoader.Load(TokenIdentityProfiles.Neptune);

        Assert.That(blocks.Select(block => block.ToHex()), Is.EqualTo(new[]
        {
            "00148040", "8BFE002A", "F100C6A2", "95D15917",
            "95D15917", "8F1249B0", "8F1249B0", "57F674C3",
        }));
        Assert.That(blocks[5], Is.EqualTo(EncodingSequences.Neptune.Encode(0)));
        Assert.That(blocks[6], Is.EqualTo(EncodingSequences.Neptune.Encode(0)));
    }
}
