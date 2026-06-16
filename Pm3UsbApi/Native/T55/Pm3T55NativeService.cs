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
    private static readonly TimeSpan ReadCommandTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromSeconds(4);

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
        foreach (var downlinkMode in new byte[] { 0, 1, 2, 3 })
        {
            ct.ThrowIfCancellationRequested();
            if (!AcquireData(block: 0, page1: false, usePassword: false, password: 0, downlinkMode, ct))
                continue;

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
        }

        return false;
    }

    public bool ReadBlock(Pm3T55Config config, byte block, out uint blockValue, CancellationToken ct)
    {
        blockValue = 0;
        if (!config.Detected)
            return false;

        if (!AcquireData(block, page1: false, usePassword: config.UsePassword, config.Password, config.DownlinkMode, ct))
            return false;

        if (!DecodeWithConfig(config))
            return false;

        return TryGetBlockData(config.Offset, out blockValue);
    }

    private bool AcquireData(byte block, bool page1, bool usePassword, uint password, byte downlinkMode, CancellationToken ct)
    {
        Span<byte> payload = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, password);
        payload[4] = block;
        payload[5] = (byte)(page1 ? 1 : 0);
        payload[6] = (byte)(usePassword ? 1 : 0);
        payload[7] = downlinkMode;

        var response = _transport.SendCommandAndWait(
            Pm3CommandCodes.CmdLfT55XxReadBl,
            payload,
            Pm3CommandCodes.CmdLfT55XxReadBl,
            ReadCommandTimeout,
            ct);

        if (response.Status != Pm3CommandCodes.Pm3Success)
            return false;

        byte[] raw;
        try
        {
            raw = _transport.DownloadBigBuf(0, Pm3CommandCodes.T55SampleCount, DownloadTimeout, ct);
        }
        catch (Exception)
        {
            return false;
        }
        _graph.Signal.Compute(raw);
        if (_graph.Signal.IsNoise)
            return false;

        _graph.LoadSamples(raw);
        return true;
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
