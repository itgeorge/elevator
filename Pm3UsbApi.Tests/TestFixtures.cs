namespace Pm3UsbApi.Tests;

/// <summary>
/// Captured Proxmark3 output samples for parser unit tests.
/// Based on RRG/Iceman lf t55 / lf t55xx output formats.
/// </summary>
public static class TestFixtures
{
    // --- lf t55 detect (successful, T55x7 chip found, colon format) ---
    public const string DetectSuccess = """
        [=] Session log C:/temp/.proxmark3/logs/log.txt
        [+] loaded preferences.json
        [+] execute command from commandline: lf t55 detect

        [+] Using UART port COM5
        [+] Communicating with PM3 over USB-CDC
        [usb|script] pm3 --> lf t55 detect

        [=] Chip Type: T55x7
        [=] Modulation: ASK
        [=] Bit Rate: 5 - RF/64
        [=] Inverted: No
        [=] Offset: 32
        [=] Seq. Term.: Yes
        [=] Block0: 0x00323240
        [=] Downlink Mode: default/fixed bit length
        [=] Password Set: No

        [usb|script] pm3 -->
        """;

    // --- lf t55 detect (Iceman format with dots as fill) ---
    public const string DetectSuccessIcemanFormat = """
        [=]  Chip type......... T55x7
        [=]  Modulation........ ASK
        [=]  Bit rate.......... 5 - RF/64
        [=]  Block 0 .......... 0x00148040
        [usb|script] pm3 -->
        """;

    // --- lf t55 detect (failed, no tag) ---
    public const string DetectNoTag = """
        [usb|script] pm3 --> lf t55 detect

        [!] Could not detect modulation automatically. Try setting it manually with 'lf t55xx config'

        [usb|script] pm3 -->
        """;

    // --- lf t55 detect (alternative: chip type none) ---
    public const string DetectChipNone = """
        [=] Chip Type: none
        [=] Modulation: unknown
        [usb|script] pm3 -->
        """;

    // --- lf tune ---
    public const string TuneSuccess = """
        [usb|script] pm3 --> lf tune

        [=] 60276 mV / 60 V / 60 Vmax

        [usb|script] pm3 -->
        """;

    // --- lf tune with multiple mV lines (use last) ---
    public const string TuneMultipleMv = """
        [=] 45100 mV / 45 V / 45 Vmax
        [=] 60276 mV / 60 V / 60 Vmax

        [usb|script] pm3 -->
        """;

    // --- lf tune no mV (failure) ---
    public const string TuneNoMv = """
        [!] antenna tuning failed
        [usb|script] pm3 -->
        """;

    // --- lf t55 read -b 0 ---
    public const string ReadBlock0 = """
        [usb|script] pm3 --> lf t55 detect
        [=] Chip Type: T55x7
        [usb|script] pm3 --> lf t55 read -b 0

        blk | data
        ----+----------
         0  | 00148040

        [usb|script] pm3 -->
        """;

    // --- lf t55 read -b 2 (different format with [+]) ---
    public const string ReadBlock2 = """
        [+] lf t55 detect
        [+] Block 2: 01242422

        [usb|script] pm3 -->
        """;

    // --- lf t55 read failed ---
    public const string ReadBlockFailed = """
        [!] Could not read block
        [usb|script] pm3 -->
        """;

    // --- lf t55 dump (full 8 blocks) ---
    public const string DumpSuccess = """
        blk | hex data | binary
        ----+----------+--------------------------------
        0   | 00107060 | 00000000000100000111000001100000
        1   | 01242422 | 00000001001001000010010000100010
        2   | BA3A3B1B | 10111010001110100011101100011011
        3   | 48111111 | 01001000000100010001000100010001
        4   | 11111111 | 00010001000100010001000100010001
        5   | 22222222 | 00100010001000100010001000100010
        6   | 33333333 | 00110011001100110011001100110011
        7   | 44444444 | 01000100010001000100010001000100

        [usb|script] pm3 -->
        """;

    // --- hw version (for OutputParser tests) ---
    public const string HwVersion = """
        [ Proxmark3 ]
        [ Client ]
         Iceman/master/v4.20728 2025-09-14
        [ Model ]
         Device.................... RDV4
        [ ARM ]
         Bootrom.... Iceman/master
        """;

    // --- ANSI color codes (for OutputParser.StripAnsi) ---
    public const string WithAnsiCodes = "\x1B[32m[+]\x1B[0m Chip Type: \x1B[1mT55x7\x1B[0m";

    public const string WithAnsiStripped = "[+] Chip Type: T55x7";

    // --- Mixed output for OutputParser.DetectErrors (errors + non-error lines like [+], [=]) ---
    public const string OutputWithErrors = """
        [+] something ok
        [!] this is an error
        [-] this too
        error: something failed
        failed to connect
        [=] 100 mV
        """;
}
