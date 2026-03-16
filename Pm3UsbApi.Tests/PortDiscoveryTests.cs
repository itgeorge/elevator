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
