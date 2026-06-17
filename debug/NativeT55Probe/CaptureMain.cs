using System.Diagnostics;
using Pm3UsbApi;
using Pm3UsbApi.Native.Demod;
using Pm3UsbApi.Native.Protocol;
using Pm3UsbApi.Native.T55;
using Pm3UsbApi.Native.Transport;

namespace NativeT55Probe;

internal static class CaptureMain
{
    private const string DefaultPort = "/dev/cu.usbmodem1201";

    public static async Task RunAsync(string[] args)
    {
        var port = GetArg(args, "--port") ?? DefaultPort;
        var timeout = TimeSpan.FromSeconds(12);
        var fixtureDir = Path.Combine(
            Directory.GetCurrentDirectory(),
            "Pm3UsbApi.Tests",
            "Fixtures",
            "Native");
        Directory.CreateDirectory(fixtureDir);

        await RunStep("ping", async () =>
        {
            await using var t = Open(port);
            var ok = t.TryPing(TimeSpan.FromSeconds(2), CancellationToken.None);
            Log($"ping={ok}");
        });

        await RunStep("readbl+download block0", async () =>
        {
            await using var t = Open(port);
            t.DiscardPendingInput();
            Span<byte> payload = stackalloc byte[8];
            var sw = Stopwatch.StartNew();
            var resp = t.SendCommandAndWait(
                Pm3CommandCodes.CmdLfT55XxReadBl,
                payload,
                Pm3CommandCodes.CmdLfT55XxReadBl,
                timeout,
                CancellationToken.None);
            Log($"readbl status={resp.Status} cmd=0x{resp.Command:X4} ms={sw.ElapsedMilliseconds}");
            sw.Restart();
            var raw = t.DownloadBigBuf(0, Pm3CommandCodes.T55SampleCount, timeout, CancellationToken.None);
            Log($"download bytes={raw.Length} ms={sw.ElapsedMilliseconds}");
            var path = Path.Combine(fixtureDir, "t55-block0-samples.bin");
            await File.WriteAllBytesAsync(path, raw);
            Log($"saved {path}");

            var graph = new Pm3GraphState();
            graph.LoadSamples(raw);
            var bytes = new byte[raw.Length];
            var len = graph.CopyToByteSamples(bytes);
            graph.Signal.Compute(bytes.AsSpan(0, len));
            Log($"signal noise={graph.Signal.IsNoise} amp={graph.Signal.Amplitude}");

            var service = new Pm3T55NativeService(t);
            var cfg = new Pm3T55Config();
            sw.Restart();
            var outcome = service.Detect(cfg, CancellationToken.None);
            Log($"detect={outcome.IsFound} block0=0x{cfg.Block0:X8} offset={cfg.Offset} ms={sw.ElapsedMilliseconds}");
        });

        await RunStep("tune then detect", async () =>
        {
            await using var pm3 = CreatePm3(port, timeout);
            var sw = Stopwatch.StartNew();
            await pm3.ConnectAsync();
            await pm3.StartLfTuneAsync();
            var mv = await pm3.GetLfTuneLastMilliVoltsAsync();
            Log($"tune mv={mv} ms={sw.ElapsedMilliseconds}");
            sw.Restart();
            await pm3.EnsureT55SessionActiveAsync();
            Log($"detect ms={sw.ElapsedMilliseconds}");
        });

        await RunStep("tune then dump (rides read path)", async () =>
        {
            await using var pm3 = CreatePm3(port, timeout);
            var sw = Stopwatch.StartNew();
            await pm3.ConnectAsync();
            await pm3.StartLfTuneAsync();
            _ = await pm3.GetLfTuneLastMilliVoltsAsync();
            Log($"tune done ms={sw.ElapsedMilliseconds}");
            sw.Restart();
            var dump = await pm3.DumpAsync();
            Log($"dump len={dump.Length} ms={sw.ElapsedMilliseconds}");
            foreach (var line in dump.Split('\n').Take(12))
                Log(line.TrimEnd());
        });
    }

    private static Pm3 CreatePm3(string port, TimeSpan timeout) =>
        new(new Pm3Options
        {
            ExecutorKind = Pm3ExecutorKind.Native,
            DevicePort = port,
            DefaultCommandTimeout = timeout,
            ConnectTimeout = timeout,
        });

    private static Pm3SerialTransport Open(string port)
    {
        var t = new Pm3SerialTransport(port);
        t.Open();
        return t;
    }

    private static string? GetArg(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private static void Log(string msg)
    {
        Console.WriteLine(msg);
        Console.Out.Flush();
    }

    private static async Task RunStep(string name, Func<Task> action)
    {
        Console.WriteLine($"=== {name} ===");
        var sw = Stopwatch.StartNew();
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await action().WaitAsync(cts.Token);
            Console.WriteLine($"OK ({sw.ElapsedMilliseconds}ms)");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAIL ({sw.ElapsedMilliseconds}ms): {ex.GetType().Name}: {ex.Message}");
        }

        Console.WriteLine();
    }
}
