using System.Text;
using Pm3UsbApi.Native.Protocol;

namespace Pm3UsbApi.Native;

/// <summary>
/// Builds CommandResult output lines that existing parsers understand.
/// </summary>
internal static class Pm3NativeOutputBuilder
{
    public static IReadOnlyList<string> BuildHwVersionLines(Pm3ResponseFrame response)
    {
        if (response.Data.Length < 12)
            return ["[!] Proxmark3 version response too short"];

        var id = BitConverter.ToUInt32(response.Data, 0);
        var versionLen = BitConverter.ToUInt32(response.Data, 8);
        var version = versionLen > 0 && response.Data.Length >= 12 + versionLen
            ? Encoding.ASCII.GetString(response.Data, 12, (int)Math.Min(versionLen, response.Data.Length - 12))
            : "Proxmark3";

        version = version.TrimEnd('\0', '\r', '\n');
        var lines = new List<string>
        {
            "[+] Communicating with PM3 over USB-CDC",
            $"[+] Proxmark3 {version}",
            $"[=] Device ID: 0x{id:X8}",
        };

        if (version.Contains("RDV4", StringComparison.OrdinalIgnoreCase) ||
            version.Contains("Device", StringComparison.OrdinalIgnoreCase) ||
            version.Contains("Bootrom", StringComparison.OrdinalIgnoreCase) ||
            version.Contains("AT91SAM", StringComparison.OrdinalIgnoreCase))
        {
            lines.Add($"[=] {version}");
        }

        return lines;
    }

    public static IReadOnlyList<string> BuildLfTuneLines(uint peakMilliVolts) =>
        [$"[=] {peakMilliVolts} mV"];

    public static IReadOnlyList<string> BuildErrorLines(string message) =>
        [$"[-] {message}"];
}
