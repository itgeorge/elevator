using NUnit.Framework;
using Pm3UsbApi.Commands;
using Pm3UsbApi.Parsers;

namespace RideCaptureCli.Tests;

public class CaptureScannerTests
{
    private const string RealDump = """
        [=] Session log /Users/itgeorge/.proxmark3/logs/log_20260420220514.txt
        [+] loaded `/Users/itgeorge/.proxmark3/preferences.json`
        [+] execute command from commandline: lf t55 detect; lf t55 dump

        [+] Using UART port /dev/tty.usbmodem11401
        [+] Communicating with PM3 over USB-CDC
        [usb|script] pm3 --> lf t55 detect
        [=]  Chip type......... T55x7
        [=]  Modulation........ ASK
        [=]  Bit rate.......... 5 - RF/64
        [=]  Inverted.......... No
        [=]  Offset............ 32
        [=]  Seq. terminator... Yes
        [=]  Block0............ 00148040 (auto detect)
        [=]  Downlink mode..... default/fixed bit length
        [=]  Password set...... No

        [usb|script] pm3 --> lf t55 dump

        [=] ------------------------- T55xx tag memory -----------------------------

        [+] Page 0
        [+] blk | hex data | binary                           | ascii
        [+] ----+----------+----------------------------------+-------
        [+]  00 | 00148040 | 00000000000101001000000001000000 | ...@
        [+]  01 | D3FE005D | 11010011111111100000000001011101 | ...]
        [+]  02 | 522BC69D | 01010010001010111100011010011101 | R+..
        [+]  03 | 650432F5 | 01100101000001000011001011110101 | e.2.
        [+]  04 | 650432F5 | 01100101000001000011001011110101 | e.2.
        [+]  05 | 18121218 | 00011000000100100001001000011000 | ....
        [+]  06 | 18121218 | 00011000000100100001001000011000 | ....
        [+]  07 | FFFFFFFF | 11111111111111111111111111111111 | ....

        [+] Page 1
        [+] blk | hex data | binary                           | ascii
        [+] ----+----------+----------------------------------+-------
        [+]  00 | 00148040 | 00000000000101001000000001000000 | ...@
        [+]  01 | E01500D0 | 11100000000101010000000011010000 | ....
        [+]  02 | D7B5C64C | 11010111101101011100011001001100 | ...L
        [+]  03 | 00A00003 | 00000000101000000000000000000011 | ....
        [+] Saved 48 bytes to binary file `/Users/itgeorge/lf-t55xx-D3FE005D-522BC69D-650432F5-650432F5-18121218-18121218-dump-003.bin`
        [+] Saved to json file /Users/itgeorge/lf-t55xx-D3FE005D-522BC69D-650432F5-650432F5-18121218-18121218-dump-003.json
        """;

    private static readonly string ModifiedRealDump = RealDump.Replace("18121218", "18121568").Replace("FFFFFFFF", "00000000");

    [Test]
    public async Task ScanAsync_uses_single_parsed_dump_and_no_block_reads()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var pm3 = new FakeRideCapturePm3Api(RealDump, signalMv: 24552);
            var config = new RideCaptureConfig
            {
                OutputRootDirectory = tempDir,
                ProxmarkDumpSearchDirectory = Path.Combine(tempDir, "search"),
                MaxAcceptableSignalMv = 29000
            };
            var scanner = new CaptureScanner(pm3, config, new CapturePaths(config.OutputRootDirectory));

            var scan = await scanner.ScanAsync();

            Assert.That(pm3.DetectCalls, Is.EqualTo(1));
            Assert.That(pm3.TuneCalls, Is.EqualTo(1));
            Assert.That(pm3.DumpCalls, Is.EqualTo(1));
            Assert.That(pm3.ReadCalls, Is.EqualTo(0));
            Assert.That(scan.Blocks, Has.Count.EqualTo(8));
            Assert.That(scan.TokenId, Is.EqualTo("D3FE005D-522BC69D-650432F5-650432F5"));
            Assert.That(scan.EncodedState, Is.EqualTo("18121218-18121218"));
            Assert.That(scan.SignalMv, Is.EqualTo(24552));
            Assert.That(scan.WeakSignal, Is.False);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Test]
    public async Task ScanAsync_parses_modified_real_dump_values_offline()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var pm3 = new FakeRideCapturePm3Api(ModifiedRealDump, signalMv: 30001);
            var config = new RideCaptureConfig
            {
                OutputRootDirectory = tempDir,
                ProxmarkDumpSearchDirectory = Path.Combine(tempDir, "search"),
                MaxAcceptableSignalMv = 29000
            };
            var scanner = new CaptureScanner(pm3, config, new CapturePaths(config.OutputRootDirectory));

            var scan = await scanner.ScanAsync();

            Assert.That(scan.Blocks[5], Is.EqualTo("18121568"));
            Assert.That(scan.Blocks[6], Is.EqualTo("18121568"));
            Assert.That(scan.Blocks[7], Is.EqualTo("00000000"));
            Assert.That(scan.WeakSignal, Is.True);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    private sealed class FakeRideCapturePm3Api : IRideCapturePm3Api
    {
        private readonly DumpResult _dumpResult;
        private readonly uint _signalMv;

        public FakeRideCapturePm3Api(string rawDump, uint signalMv)
        {
            _dumpResult = DumpParser.Parse(new Pm3UsbApi.CommandResult
            {
                Commands = [new T55DumpCommand()],
                OutputLines = rawDump.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(s => s.TrimEnd()).ToList(),
                ExitCode = 0,
                HasErrors = false
            });
            _signalMv = signalMv;
        }

        public int DetectCalls { get; private set; }
        public int TuneCalls { get; private set; }
        public int DumpCalls { get; private set; }
        public int ReadCalls { get; private set; }

        public Task<bool> TryDetectTokenAsync(CancellationToken ct = default)
        {
            DetectCalls++;
            return Task.FromResult(true);
        }

        public Task<string> ReadPage0BlockAsync(uint block, CancellationToken ct = default)
        {
            ReadCalls++;
            throw new InvalidOperationException("ReadPage0BlockAsync should not be called by optimized CaptureScanner.");
        }

        public Task<DumpResult> DumpParsedAsync(CancellationToken ct = default)
        {
            DumpCalls++;
            return Task.FromResult(_dumpResult);
        }

        public Task<uint> GetSignalStrengthMvAsync(CancellationToken ct = default)
        {
            TuneCalls++;
            return Task.FromResult(_signalMv);
        }
    }
}
