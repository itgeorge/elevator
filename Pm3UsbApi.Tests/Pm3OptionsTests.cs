using NUnit.Framework;
using Pm3UsbApi;

namespace Pm3UsbApi.Tests;

[TestFixture]
public class Pm3OptionsTests
{
    [Test]
    public void NativeLfTuneDefaults_AreSixtySamplesAndThreeSecondTimeout()
    {
        var options = new Pm3Options();
        Assert.That(options.NativeLfTuneSampleCount, Is.EqualTo(60));
        Assert.That(options.NativeLfTuneTimeout, Is.EqualTo(TimeSpan.FromSeconds(3)));
    }

    [Test]
    public void DefaultExecutorKind_IsNative()
    {
        var options = new Pm3Options();
        Assert.That(options.ExecutorKind, Is.EqualTo(Pm3ExecutorKind.Native));
    }

    [Test]
    public void ReadExecutorKindFromEnvironment_WhenUnset_ReturnsNative()
    {
        var prior = Environment.GetEnvironmentVariable("PM3_EXECUTOR");
        try
        {
            Environment.SetEnvironmentVariable("PM3_EXECUTOR", null);
            Assert.That(Pm3Options.ReadExecutorKindFromEnvironment(), Is.EqualTo(Pm3ExecutorKind.Native));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PM3_EXECUTOR", prior);
        }
    }

    [Test]
    public void ReadExecutorKindFromEnvironment_WhenProcess_ReturnsProcess()
    {
        var prior = Environment.GetEnvironmentVariable("PM3_EXECUTOR");
        try
        {
            Environment.SetEnvironmentVariable("PM3_EXECUTOR", "process");
            Assert.That(Pm3Options.ReadExecutorKindFromEnvironment(), Is.EqualTo(Pm3ExecutorKind.Process));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PM3_EXECUTOR", prior);
        }
    }

    [Test]
    public void ReadExecutorKindFromEnvironment_WhenNative_ReturnsNative()
    {
        var prior = Environment.GetEnvironmentVariable("PM3_EXECUTOR");
        try
        {
            Environment.SetEnvironmentVariable("PM3_EXECUTOR", "native");
            Assert.That(Pm3Options.ReadExecutorKindFromEnvironment(), Is.EqualTo(Pm3ExecutorKind.Native));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PM3_EXECUTOR", prior);
        }
    }
}
