using System.Buffers.Binary;
using Pm3UsbApi.Native.Demod;
using Pm3UsbApi.Native.Protocol;
using Pm3UsbApi.Native.Transport;

namespace Pm3UsbApi.Native.T55;

/// <summary>
/// Native USB T55x7 detect and read for elevator tokens (ASK/Manchester).
/// </summary>
internal sealed class Pm3T55NativeService
{
    private static readonly TimeSpan ReadCommandTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan WriteCommandTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan WriteSettleDelay = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan RfSettleDelay = TimeSpan.FromMilliseconds(150);
    private const int WriteMaxAttempts = 3;
    private const int AcquireMaxAttempts = 2;
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromSeconds(8);

    private readonly Pm3SerialTransport _transport;
    private readonly Pm3GraphState _graph = new();
    private readonly byte[] _sampleScratch = new byte[Pm3GraphState.MaxGraphSamples];
    private readonly byte[] _demodWork = new byte[Pm3GraphState.MaxGraphSamples];

    public Pm3T55NativeService(Pm3SerialTransport transport)
    {
        _transport = transport;
    }

    public bool Detect(Pm3T55Config config, CancellationToken ct)
    {
        config.Detected = false;

        foreach (var downlinkMode in new byte[] { 0, 1, 2, 3 })
        {
            ct.ThrowIfCancellationRequested();
            if (!AcquireData(block: 0, page1: false, usePassword: false, password: 0, downlinkMode, ct))
            {
                WaitForRfSettle(ct);
                continue;
            }

            foreach (var invert in new[] { false, true })
            {
                if (!TryAskDetect(invert, downlinkMode, out var candidate))
                    continue;

                if (!Pm3BitUtils.TestKnownConfigBlock(candidate.Block0) &&
                    candidate.Block0 != Pm3BitUtils.T55X7EmUniqueConfigBlock)
                    continue;

                config.ApplyDetection(
                    candidate.Modulation,
                    candidate.Bitrate,
                    candidate.Inverted,
                    candidate.SequenceTerminator,
                    candidate.Offset,
                    downlinkMode,
                    candidate.Block0,
                    candidate.Clock);
                return true;
            }

            WaitForRfSettle(ct);
        }

        return false;
    }

    public bool ReadBlock(Pm3T55Config config, byte block, out uint blockValue, CancellationToken ct)
    {
        blockValue = 0;
        if (!config.Detected)
            return false;

        Span<uint> candidates = stackalloc uint[3];
        var successes = 0;

        for (var attempt = 0; attempt < candidates.Length; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            if (!TryReadBlockOnce(config, block, out var candidate, ct))
                continue;

            candidates[successes++] = candidate;
            for (var i = 0; i < successes - 1; i++)
            {
                if (candidates[i] == candidate)
                {
                    blockValue = candidate;
                    return true;
                }
            }
        }

        if (successes == 0)
            return false;

        // If demod phase jitter gives no majority, prefer the latest successful acquisition.
        blockValue = candidates[successes - 1];
        return true;
    }

    private bool TryReadBlockOnce(Pm3T55Config config, byte block, out uint blockValue, CancellationToken ct)
    {
        blockValue = 0;

        if (!AcquireData(block, page1: false, usePassword: config.UsePassword, config.Password, config.DownlinkMode, ct))
            return false;

        if (!DecodeWithConfig(config))
            return false;

        return TryGetBlockData(config.Offset, out blockValue);
    }

    public bool WriteBlock(Pm3T55Config config, byte block, uint data, CancellationToken ct)
    {
        if (!config.Detected)
            return false;

        var payload = BuildWriteBlockPayload(
            data,
            config.Password,
            block,
            config.UsePassword,
            page1: false,
            testMode: false,
            config.DownlinkMode);

        for (var attempt = 0; attempt < WriteMaxAttempts; attempt++)
        {
            // Native mode can issue T55 commands much faster than the CLI process path.
            // Give the tag a short RF-off recovery window before programming and before
            // read-back verification so marginal writes are retried instead of accepted.
            WaitForWriteSettle(ct);

            var response = _transport.SendCommandAndWait(
                Pm3CommandCodes.CmdLfT55XxWriteBl,
                payload,
                Pm3CommandCodes.CmdLfT55XxWriteBl,
                WriteCommandTimeout,
                ct);

            if (response.Status != Pm3CommandCodes.Pm3Success)
                continue;

            WaitForWriteSettle(ct);

            if (ReadBlock(config, block, out var readBack, ct) && readBack == data)
                return true;
        }

        return false;
    }

    public bool DumpPage0(Pm3T55Config config, uint[] blockValues, CancellationToken ct)
    {
        if (!config.Detected || blockValues.Length < 8)
            return false;

        for (byte block = 0; block < 8; block++)
        {
            ct.ThrowIfCancellationRequested();
            if (!ReadBlock(config, block, out blockValues[block], ct))
                return false;
        }

        return true;
    }

    internal static byte[] BuildWriteBlockPayload(
        uint data,
        uint password,
        byte block,
        bool usePassword,
        bool page1,
        bool testMode,
        byte downlinkMode)
    {
        var payload = new byte[10];
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0), data);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4), password);
        payload[8] = block;
        payload[9] = BuildWriteFlags(usePassword, page1, testMode, downlinkMode);
        return payload;
    }

    internal static byte BuildWriteFlags(bool usePassword, bool page1, bool testMode, byte downlinkMode)
    {
        byte flags = 0;
        if (usePassword)
            flags |= 0x1;
        if (page1)
            flags |= 0x2;
        if (testMode)
            flags |= 0x4;
        flags |= (byte)(downlinkMode << 3);
        return flags;
    }

    private static void WaitForWriteSettle(CancellationToken ct)
    {
        var deadline = Environment.TickCount64 + (long)WriteSettleDelay.TotalMilliseconds;
        while (Environment.TickCount64 < deadline)
        {
            ct.ThrowIfCancellationRequested();
            Thread.Sleep(Math.Min(10, Math.Max(1, (int)(deadline - Environment.TickCount64))));
        }
    }

    private bool AcquireData(byte block, bool page1, bool usePassword, uint password, byte downlinkMode, CancellationToken ct)
    {
        Span<byte> payload = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, password);
        payload[4] = block;
        payload[5] = (byte)(page1 ? 1 : 0);
        payload[6] = (byte)(usePassword ? 1 : 0);
        payload[7] = downlinkMode;

        for (var attempt = 0; attempt < AcquireMaxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            if (attempt > 0)
                WaitForRfSettle(ct);

            _transport.DiscardPendingInput();

            Pm3ResponseFrame response;
            try
            {
                response = _transport.SendCommandAndWait(
                    Pm3CommandCodes.CmdLfT55XxReadBl,
                    payload,
                    Pm3CommandCodes.CmdLfT55XxReadBl,
                    ReadCommandTimeout,
                    ct);
            }
            catch (TimeoutException)
            {
                continue;
            }

            if (response.Status != Pm3CommandCodes.Pm3Success)
                continue;

            byte[] raw;
            try
            {
                raw = _transport.DownloadBigBuf(0, Pm3CommandCodes.T55SampleCount, DownloadTimeout, ct);
            }
            catch (Exception)
            {
                continue;
            }

            _graph.LoadSamples(raw);
            var sampleLen = _graph.CopyToByteSamples(_sampleScratch);
            _graph.Signal.Compute(_sampleScratch.AsSpan(0, sampleLen));
            if (_graph.Signal.IsNoise)
                continue;

            return true;
        }

        return false;
    }

    private static void WaitForRfSettle(CancellationToken ct)
    {
        var deadline = Environment.TickCount64 + (long)RfSettleDelay.TotalMilliseconds;
        while (Environment.TickCount64 < deadline)
        {
            ct.ThrowIfCancellationRequested();
            Thread.Sleep(Math.Min(10, Math.Max(1, (int)(deadline - Environment.TickCount64))));
        }
    }

    private bool TryAskDetect(bool invert, byte downlinkMode, out DetectCandidate candidate)
    {
        candidate = default;
        var sampleLen = _graph.CopyToByteSamples(_sampleScratch);
        if (sampleLen < 255)
            return false;

        _sampleScratch.AsSpan(0, sampleLen).CopyTo(_demodWork);
        var bitLen = sampleLen;
        var clk = 0;
        var invertInt = invert ? 1 : 0;
        var st = true;

        var err = Pm3LfDemod.AskDemodExt(
            _demodWork,
            ref bitLen,
            ref clk,
            ref invertInt,
            maxErr: 1,
            askType: 1,
            ref st,
            _graph.Signal);

        if (err < 0 || err > 1 || bitLen < 64)
            return false;

        _graph.SetDemodBuffer(_demodWork.AsSpan(0, bitLen));

        if (!Pm3BitUtils.TryFindConfigOffset(_demodWork.AsSpan(0, bitLen), Pm3BitUtils.DemodAsk, clk, out var offset, out var bitrate))
            return false;

        var block0 = Pm3BitUtils.PackBits(offset, 32, _demodWork.AsSpan(0, bitLen));
        candidate = new DetectCandidate
        {
            Modulation = Pm3BitUtils.DemodAsk,
            Bitrate = bitrate,
            Inverted = invertInt == 1,
            SequenceTerminator = st,
            Offset = offset,
            DownlinkMode = downlinkMode,
            Block0 = block0,
            Clock = clk,
        };
        return true;
    }

    private bool DecodeWithConfig(Pm3T55Config config)
    {
        var sampleLen = _graph.CopyToByteSamples(_sampleScratch);
        if (sampleLen < 255)
            return false;

        _sampleScratch.AsSpan(0, sampleLen).CopyTo(_demodWork);
        var bitLen = sampleLen;
        var clk = config.Clock > 0 ? config.Clock : Pm3LfDemod.TokenClock;
        var invert = config.Inverted ? 1 : 0;
        var st = config.SequenceTerminator;

        var err = Pm3LfDemod.AskDemodExt(
            _demodWork,
            ref bitLen,
            ref clk,
            ref invert,
            maxErr: 1,
            askType: 1,
            ref st,
            _graph.Signal);

        if (err < 0 || err > 1)
            return false;

        _graph.SetDemodBuffer(_demodWork.AsSpan(0, bitLen));
        return bitLen >= config.Offset + 32;
    }

    private bool TryGetBlockData(byte offset, out uint blockValue)
    {
        blockValue = 0;
        if (_graph.DemodLength < offset + 32)
            return false;

        blockValue = Pm3BitUtils.PackBits(offset, 32, _graph.DemodBuffer.AsSpan(0, _graph.DemodLength));
        return true;
    }

    private readonly struct DetectCandidate
    {
        public byte Modulation { get; init; }
        public byte Bitrate { get; init; }
        public bool Inverted { get; init; }
        public bool SequenceTerminator { get; init; }
        public byte Offset { get; init; }
        public byte DownlinkMode { get; init; }
        public uint Block0 { get; init; }
        public int Clock { get; init; }
    }
}
