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
}
