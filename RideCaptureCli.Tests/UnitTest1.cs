using NUnit.Framework;

namespace RideCaptureCli.Tests;

public class BasicProjectTests
{
    [Test]
    public void Project_loads()
    {
        Assert.That(typeof(Program).Namespace, Is.EqualTo("RideCaptureCli"));
    }
}
