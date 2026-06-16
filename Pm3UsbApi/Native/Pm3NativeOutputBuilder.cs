using System.Text;
using Pm3UsbApi.Native.Demod;
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

    public static IReadOnlyList<string> BuildDetectLines(Pm3T55Config config) =>
    [
        "[=] Chip Type: T55x7",
        $"[=] Modulation: {Pm3T55Config.ModulationName(config.Modulation)}",
        $"[=] Block0: 0x{config.Block0:X8}",
        $"[=] Block 0 .......... 0x{config.Block0:X8}",
    ];

    public static IReadOnlyList<string> BuildReadBlockLines(uint block, uint blockValue) =>
    [
        $"[+] Block {block}: {blockValue:X8}",
    ];

    public static IReadOnlyList<string> BuildDetectFailedLines() =>
    [
        "[!] Could not detect modulation automatically. Try setting it manually with 'lf t55xx config'",
    ];

    public static IReadOnlyList<string> BuildReadFailedLines(uint block) =>
    [
        $"[!] Could not read block {block}",
    ];

    public static IReadOnlyList<string> BuildErrorLines(string message) =>
        [$"[-] {message}"];
}
