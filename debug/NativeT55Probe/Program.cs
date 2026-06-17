using System.Buffers.Binary;
using System.Diagnostics;
using Pm3UsbApi;
using Pm3UsbApi.Native.Demod;
using Pm3UsbApi.Native.Protocol;
using Pm3UsbApi.Native.T55;
using Pm3UsbApi.Native.Transport;

using NativeT55Probe;

if (args.Contains("--capture"))
{
    await CaptureMain.RunAsync(args);
    return;
}

if (args.Contains("--load-test"))
{
    await LoadTestMain.RunAsync(args);
    return;
}

var options = new Pm3Options { DevicePort = null, AutoConnect = true };
var port = await DiscoverPortAsync(options) ?? throw new InvalidOperationException("No port");
Log($"port={port}");

var transport = new Pm3SerialTransport(port, options.SerialBaudRate);
transport.Open();

try
{
    var sw = System.Diagnostics.Stopwatch.StartNew();
    Span<byte> payload = stackalloc byte[8];
    transport.SendCommandAndWait(
        Pm3CommandCodes.CmdLfT55XxReadBl, payload, Pm3CommandCodes.CmdLfT55XxReadBl,
        TimeSpan.FromSeconds(12), CancellationToken.None);
    var raw = transport.DownloadBigBuf(0, Pm3CommandCodes.T55SampleCount, TimeSpan.FromSeconds(12), CancellationToken.None);
    Log($"samples={raw.Length} ({sw.ElapsedMilliseconds}ms)");

    var graph = new Pm3GraphState();
    graph.LoadSamples(raw);
    var demodBytes = new byte[raw.Length];
    var demodLen = graph.CopyToByteSamples(demodBytes);
    var signal = graph.Signal;
    signal.Compute(demodBytes.AsSpan(0, demodLen));
    Log($"signal noise={signal.IsNoise} amp={signal.Amplitude}");

    var work = demodBytes;
    foreach (var invert in new[] { 0, 1 })
    {
        var sample = (byte[])work.Clone();
        var bitLen = sample.Length;
        var clk = 0;
        var invertInt = invert;
        var st = true;
        sw.Restart();
        var err = Pm3LfDemod.AskDemodExt(sample, ref bitLen, ref clk, ref invertInt, maxErr: 1, askType: 1, ref st, signal);
        Log($"demod inv={invert} err={err} clk={clk} bits={bitLen} ({sw.ElapsedMilliseconds}ms)");
        if (err >= 0 && bitLen >= 64)
        {
            var ok = Pm3BitUtils.TryFindConfigOffset(sample.AsSpan(0, bitLen), Pm3BitUtils.DemodAsk, clk, out var offset, out _);
            var block0 = ok ? Pm3BitUtils.PackBits(offset, 32, sample.AsSpan(0, bitLen)) : 0u;
            Log($"  config ok={ok} offset={offset} block0=0x{block0:X8}");
        }
    }

    sw.Restart();
    var service = new Pm3T55NativeService(transport);
    var config = new Pm3T55Config();
    var outcome = service.Detect(config, CancellationToken.None);
    if (!outcome.IsFound)
        throw new InvalidOperationException(outcome.IsUnsupportedChipType
            ? $"Unsupported chip: {Pm3LfChipFamilyNames.Name(outcome.UnsupportedChip.ChipFamily)} {outcome.UnsupportedChip.CardId:X}"
            : outcome.IsUnsupportedModulation
                ? $"Unsupported modulation: {Pm3T55ModulationNames.Name(outcome.UnsupportedInfo.Modulation)}"
                : "Detect failed");
    Log($"detect {sw.ElapsedMilliseconds}ms block0=0x{config.Block0:X8} clk={config.Clock} offset={config.Offset}");

    sw.Restart();
    if (!service.ReadBlock(config, 5, out var block5, CancellationToken.None))
        throw new InvalidOperationException("Read block 5 failed");
    Log($"read block5=0x{block5:X8} ({sw.ElapsedMilliseconds}ms)");
}
finally
{
    transport.Close();
}

static void Log(string msg)
{
    Console.WriteLine(msg);
    Console.Out.Flush();
}

static async Task<string?> DiscoverPortAsync(Pm3Options options)
{
    foreach (var p in await PortDiscovery.ListPortsAsync(options.Pm3ClientPath, CancellationToken.None))
    {
        await using var probe = new Pm3SerialTransport(p, options.SerialBaudRate);
        if (probe.TryPing(TimeSpan.FromSeconds(2), CancellationToken.None))
            return p;
    }

    return null;
}
