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
        }
    }
}
