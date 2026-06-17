using System.Runtime.InteropServices;
using NUnit.Framework;

namespace Pm3UsbApi.Tests;

[TestFixture]
[NonParallelizable]
public class PortDiscoveryTests
{
    [Test]
    public async Task ListPortsAsync_OnUnix_UsesPm3FoundOnPath_WhenClientPathIsNull()
    {
        RequireUnixLikePlatform();

        var tempDir = CreateTempDir();
        var originalPath = Environment.GetEnvironmentVariable("PATH");
        try
        {
            WriteFakePm3Script(tempDir, "/dev/tty.usbmodemTEST1", "/dev/tty.usbmodemTEST2");
            PrependToPath(tempDir, originalPath);

            var ports = await PortDiscovery.ListPortsAsync(null);

            Assert.That(ports, Is.EqualTo(new[] { "/dev/tty.usbmodemTEST1", "/dev/tty.usbmodemTEST2" }));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Test]
    public void PreferCalloutDevice_OnMacOs_UsesCuWhenAvailable()
    {
        RequireMacOsPlatform();

        var ttyPort = "/dev/tty.usbmodem1201";
        var cuPort = "/dev/cu.usbmodem1201";
        if (!File.Exists(cuPort))
            Assert.Ignore($"Callout device {cuPort} is not present.");

        Assert.That(PortDiscovery.PreferCalloutDevice(ttyPort), Is.EqualTo(cuPort));
        Assert.That(PortDiscovery.PreferCalloutDevice(cuPort), Is.EqualTo(cuPort));
    }

    [Test]
    public void NormalizeUnixPorts_DeduplicatesTtyAndCuVariants()
    {
        var ports = PortDiscovery.NormalizeUnixPorts([
            "/dev/tty.usbmodem1201",
            "/dev/cu.usbmodem1201",
            "/dev/tty.debug-console",
        ]);

        Assert.That(ports, Has.Count.EqualTo(1));
        Assert.That(ports[0], Does.StartWith("/dev/").And.Contain("usbmodem1201"));
    }

    [Test]
    public void IsLikelyProxmarkSerialName_MatchesCommonUnixPatterns()
    {
        Assert.That(PortDiscovery.IsLikelyProxmarkSerialName("/dev/cu.usbmodem1201"), Is.True);
        Assert.That(PortDiscovery.IsLikelyProxmarkSerialName("/dev/ttyACM0"), Is.True);
        Assert.That(PortDiscovery.IsLikelyProxmarkSerialName("/dev/cu.debug-console"), Is.False);
    }

    [Test]
    public async Task ListPortsAsync_OnUnix_FallsBackToNativeDiscovery_WhenPm3ScriptMissing()
    {
        RequireUnixLikePlatform();

        var tempDir = CreateTempDir();
        var originalPath = Environment.GetEnvironmentVariable("PATH");
        try
        {
            PrependToPath(tempDir, originalPath);

            var ports = await PortDiscovery.ListPortsAsync(null);

            if (ports.Count == 0)
                Assert.Ignore("No Proxmark3 device connected for native discovery fallback test.");

            Assert.That(ports[0], Does.StartWith("/dev/"));
            Assert.That(PortDiscovery.IsLikelyProxmarkSerialName(ports[0]), Is.True);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Test]
    public async Task DiscoverFirstPortAsync_OnUnix_UsesPm3ClientPathResolvedFromPath()
    {
        RequireUnixLikePlatform();

        var tempDir = CreateTempDir();
        var originalPath = Environment.GetEnvironmentVariable("PATH");
        try
        {
            WriteFakePm3Script(tempDir, "/dev/tty.usbmodemTEST9", "/dev/tty.usbmodemTEST8");
            File.WriteAllText(Path.Combine(tempDir, "proxmark3"), "#!/usr/bin/env bash\nexit 0\n");
            PrependToPath(tempDir, originalPath);

            var port = await PortDiscovery.DiscoverFirstPortAsync("proxmark3");

            Assert.That(port, Is.EqualTo("/dev/tty.usbmodemTEST9"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    private static void RequireUnixLikePlatform()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            Assert.Ignore("Unix/macOS-specific port discovery test.");
    }

    private static void RequireMacOsPlatform()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            Assert.Ignore("macOS-specific port discovery test.");
    }

    private static string CreateTempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void WriteFakePm3Script(string dir, params string[] ports)
    {
        var scriptPath = Path.Combine(dir, "pm3");
        var lines = new List<string>
        {
            "#!/usr/bin/env bash",
            "if [ \"$1\" = \"--list\" ]; then"
        };

        for (var i = 0; i < ports.Length; i++)
        {
            lines.Add($"  echo \"{i + 1}: {ports[i]}\"");
        }

        lines.Add("fi");
        File.WriteAllText(scriptPath, string.Join(Environment.NewLine, lines) + Environment.NewLine);
    }

    private static void PrependToPath(string dir, string? originalPath)
    {
        var newPath = string.IsNullOrEmpty(originalPath)
            ? dir
            : dir + Path.PathSeparator + originalPath;
        Environment.SetEnvironmentVariable("PATH", newPath);
    }
}
