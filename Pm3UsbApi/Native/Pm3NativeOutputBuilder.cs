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

    public static IReadOnlyList<string> BuildWriteBlockLines(uint block, uint data) =>
    [
        $"[=] Writing page 0  block: {block:D2}  data: 0x{data:X8}",
        $"[+] Writing T55x7 block {block} data {data:X8}",
    ];

    public static IReadOnlyList<string> BuildWriteFailedLines(uint block) =>
    [
        $"[-] Write failed (block {block})",
    ];

    public static IReadOnlyList<string> BuildDumpLines(IReadOnlyList<uint> blockValues)
    {
        var lines = new List<string>
        {
            "[+] Page 0",
            "[+] blk | hex data | binary                           | ascii",
            "[+] ----+----------+----------------------------------+-------",
        };

        for (var i = 0; i < blockValues.Count; i++)
        {
            var value = blockValues[i];
            var hex = value.ToString("X8");
            var binary = Convert.ToString(value, 2).PadLeft(32, '0');
            var ascii = ToAsciiColumn(value);
            lines.Add($"[+] {i,1}   | {hex} | {binary} | {ascii}");
        }

        return lines;
    }

    public static IReadOnlyList<string> BuildDumpFailedLines() =>
    [
        "[-] Failed to dump T55 page 0",
    ];

    public static IReadOnlyList<string> BuildErrorLines(string message) =>
        [$"[-] {message}"];

    private static string ToAsciiColumn(uint value)
    {
        Span<char> chars = stackalloc char[4];
        for (var i = 0; i < 4; i++)
        {
            var b = (byte)((value >> (8 * (3 - i))) & 0xFF);
            chars[i] = b is >= 32 and <= 126 ? (char)b : '.';
        }

        return new string(chars);
    }
}
