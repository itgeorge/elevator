using System.Buffers.Binary;
using System.IO.Ports;
using System.Runtime.InteropServices;
using Pm3UsbApi.Native.Protocol;

namespace Pm3UsbApi.Native.Transport;

/// <summary>
/// USB CDC serial transport for Proxmark3 NG/MIX/OLD packets.
/// </summary>
internal sealed class Pm3SerialTransport : IAsyncDisposable
{
    private const int MaxReceiveBufferBytes = 256 * 1024;

    private readonly string _portName;
    private readonly int _baudRate;
    private readonly Action<string>? _nativeTrace;
    private SerialPort? _port;
    private readonly object _lock = new();
    private readonly List<byte> _receiveBuffer = new(8192);
    private Pm3Capabilities _capabilities = Pm3Capabilities.CreateDefault();

    public Pm3Capabilities Capabilities => _capabilities;

    public bool CapabilitiesFetched { get; private set; }

    public Pm3SerialTransport(string portName, int baudRate = 115200, Action<string>? nativeTrace = null)
    {
        if (string.IsNullOrWhiteSpace(portName))
            throw new ArgumentException("Port name is required.", nameof(portName));
        _portName = portName.Trim();
        _baudRate = baudRate;
        _nativeTrace = nativeTrace;
    }

    public bool IsOpen
    {
        get
        {
            lock (_lock)
                return _port?.IsOpen == true;
        }
    }

    public void Open()
    {
        lock (_lock)
        {
            if (_port?.IsOpen == true)
                return;

            _port?.Dispose();
            _port = new SerialPort(_portName, _baudRate, Parity.None, 8, StopBits.One)
            {
                ReadTimeout = 250,
                WriteTimeout = 2000,
                DtrEnable = true,
                RtsEnable = true,
            };
            _port.Open();
            _port.DiscardInBuffer();
            _port.DiscardOutBuffer();
            lock (_lock)
            {
                _receiveBuffer.Clear();
            }
        }
    }

    public void Close()
    {
        lock (_lock)
        {
            if (_port is null)
                return;
            try
            {
                if (_port.IsOpen)
                    _port.Close();
            }
            finally
            {
                _port.Dispose();
                _port = null;
            }
        }
    }

    public void DiscardPendingInput()
    {
        lock (_lock)
        {
            if (_port?.IsOpen == true)
                _port.DiscardInBuffer();
            _receiveBuffer.Clear();
        }
    }

    public void ClearReceiveBuffer()
    {
        lock (_lock)
        {
            _receiveBuffer.Clear();
        }
    }

    public Pm3ResponseFrame SendCommand(ushort command, ReadOnlySpan<byte> payload, TimeSpan timeout, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Trace($"send cmd=0x{command:X4} payload={payload.Length}B");
        Write(Pm3NgPacketCodec.EncodeCommand(command, payload));
        var response = Pm3NgPacketCodec.DecodeAnyResponse(ReadResponseFrame(timeout, ct));
        Trace($"recv cmd=0x{response.Command:X4} status={response.Status} reason={response.Reason}");
        return response;
    }

    public Pm3ResponseFrame SendCommandAndWait(
        ushort command,
        ReadOnlySpan<byte> payload,
        ushort expectedResponseCommand,
        TimeSpan timeout,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Trace($"send cmd=0x{command:X4} wait=0x{expectedResponseCommand:X4} payload={payload.Length}B");
        Write(Pm3NgPacketCodec.EncodeCommand(command, payload));
        var response = WaitForResponse(expectedResponseCommand, timeout, ct);
        Trace($"recv cmd=0x{response.Command:X4} status={response.Status} reason={response.Reason}");
        return response;
    }

    public byte[] DownloadBigBuf(uint startIndex, uint byteCount, TimeSpan timeout, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Trace($"download bigbuf start={startIndex} count={byteCount}");
        ClearReceiveBuffer();
        Write(Pm3NgPacketCodec.EncodeMixCommand(Pm3CommandCodes.CmdDownloadBigBuf, startIndex, byteCount, 0));

        var dest = new byte[byteCount];
        var deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        uint bytesCompleted = 0;
        var ignoredFrames = 0;

        while (bytesCompleted < byteCount)
        {
            ct.ThrowIfCancellationRequested();
            if (Environment.TickCount64 >= deadline)
            {
                Trace($"download bigbuf timeout {bytesCompleted}/{byteCount}");
                throw new TimeoutException($"Timed out downloading BigBuf ({bytesCompleted}/{byteCount} bytes).");
            }

            var remaining = Math.Max(1, (int)(deadline - Environment.TickCount64));
            var response = Pm3NgPacketCodec.DecodeAnyResponse(ReadResponseFrame(TimeSpan.FromMilliseconds(remaining), ct));

            if (response.Command == Pm3CommandCodes.CmdAck)
            {
                if (bytesCompleted >= byteCount)
                    return dest;
                continue;
            }

            if (response.Command == Pm3CommandCodes.CmdWtx)
            {
                ignoredFrames = 0;
                if (TryExtendDeadlineForWtx(response, ref deadline))
                    continue;
            }

            if (response.Command != Pm3CommandCodes.CmdDownloadedBigBuf)
            {
                if (++ignoredFrames > 32)
                    throw new InvalidOperationException($"Unexpected response 0x{response.Command:X4} during BigBuf download.");
                continue;
            }

            ignoredFrames = 0;
            var offset = (uint)response.OldArg[0];
            var copyBytes = (uint)Math.Min(response.OldArg[1], byteCount - bytesCompleted);
            copyBytes = Math.Min(copyBytes, Pm3CommandCodes.MaxDataSize);
            if (copyBytes == 0)
                throw new InvalidOperationException("BigBuf download chunk length was zero.");

            if (offset + copyBytes > byteCount)
                throw new InvalidOperationException($"BigBuf download chunk out of range at offset {offset}.");

            response.Data.AsSpan(0, (int)copyBytes).CopyTo(dest.AsSpan((int)offset));
            bytesCompleted += copyBytes;
        }

        DrainDownloadAck(TimeSpan.FromMilliseconds(250), ct);
        Trace($"download bigbuf complete bytes={byteCount}");
        return dest;
    }

    public Pm3ResponseFrame WaitForResponse(ushort expectedCommand, TimeSpan timeout, CancellationToken ct)
    {
        var deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        while (Environment.TickCount64 < deadline)
        {
            ct.ThrowIfCancellationRequested();
            var remaining = Math.Max(1, (int)(deadline - Environment.TickCount64));
            var response = Pm3NgPacketCodec.DecodeAnyResponse(ReadResponseFrame(TimeSpan.FromMilliseconds(remaining), ct));
            if (response.Command == expectedCommand)
                return response;

            if (response.Command == Pm3CommandCodes.CmdWtx)
            {
                TryExtendDeadlineForWtx(response, ref deadline);
            }
        }

        throw new TimeoutException($"Timed out waiting for response command 0x{expectedCommand:X4}.");
    }

    public void FetchCapabilities(TimeSpan timeout, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var response = SendCommandAndWait(
            Pm3CommandCodes.CmdCapabilities,
            ReadOnlySpan<byte>.Empty,
            Pm3CommandCodes.CmdCapabilities,
            timeout,
            ct);

        if (response.Status != Pm3CommandCodes.Pm3Success)
            throw new Pm3CapabilitiesException($"CMD_CAPABILITIES failed with status {response.Status}.");

        _capabilities = Pm3Capabilities.Decode(response.Data);
        CapabilitiesFetched = true;
        Trace(
            $"capabilities v{_capabilities.Version} bigbuf={_capabilities.BigBufSize} " +
            $"lf={_capabilities.CompiledWithLf} rdv4={_capabilities.IsRdv4}");
    }

    public bool TryPing(TimeSpan timeout, CancellationToken ct)
    {
        var openedHere = false;
        try
        {
            if (!IsOpen)
            {
                Open();
                openedHere = true;
            }

            var response = SendCommand(Pm3CommandCodes.CmdPing, ReadOnlySpan<byte>.Empty, timeout, ct);
            return response.Command == Pm3CommandCodes.CmdPing && response.Status == Pm3CommandCodes.Pm3Success;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (openedHere)
                Close();
        }
    }

    private void DrainDownloadAck(TimeSpan timeout, CancellationToken ct)
    {
        var deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        while (Environment.TickCount64 < deadline)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var remaining = Math.Max(1, (int)(deadline - Environment.TickCount64));
                var response = Pm3NgPacketCodec.DecodeAnyResponse(
                    ReadResponseFrame(TimeSpan.FromMilliseconds(remaining), ct));
                if (response.Command == Pm3CommandCodes.CmdAck)
                    return;
            }
            catch (TimeoutException)
            {
                return;
            }
        }
    }

    private void Write(byte[] data)
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_port is null || !_port.IsOpen, this);
            _port!.Write(data, 0, data.Length);
        }
    }

    private byte[] ReadResponseFrame(TimeSpan timeout, CancellationToken ct)
    {
        var deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;

        while (Environment.TickCount64 < deadline)
        {
            ct.ThrowIfCancellationRequested();

            lock (_lock)
            {
                if (_receiveBuffer.Count > MaxReceiveBufferBytes)
                    throw new InvalidOperationException("Proxmark3 receive buffer grew too large while parsing frames.");

                if (TryExtractResponseFrame(_receiveBuffer, out var frame))
                    return frame;
            }

            var chunk = ReadAvailable(Math.Max(1, (int)(deadline - Environment.TickCount64)));
            if (chunk.Length == 0)
            {
                Thread.Sleep(1);
                continue;
            }

            lock (_lock)
            {
                _receiveBuffer.AddRange(chunk);
            }
        }

        throw new TimeoutException("Timed out waiting for Proxmark3 response frame.");
    }

    private byte[] ReadAvailable(int maxWaitMs)
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_port is null || !_port.IsOpen, this);
            var port = _port!;
            var originalTimeout = port.ReadTimeout;
            try
            {
                port.ReadTimeout = Math.Clamp(maxWaitMs, 1, 250);
                var buffer = new byte[4096];
                try
                {
                    var read = port.Read(buffer, 0, buffer.Length);
                    return buffer.AsSpan(0, read).ToArray();
                }
                catch (TimeoutException)
                {
                    return [];
                }
            }
            finally
            {
                port.ReadTimeout = originalTimeout;
            }
        }
    }

    private static bool TryExtendDeadlineForWtx(Pm3ResponseFrame response, ref long deadline)
    {
        if (response.Data.Length < sizeof(ushort))
            return false;

        var wtxMs = BitConverter.ToUInt16(response.Data, 0) & 0xFFFF;
        if (wtxMs == 0)
            return false;

        deadline += wtxMs;
        return true;
    }

    internal static bool TryExtractResponseFrame(List<byte> buffer, out byte[] frame)
    {
        frame = [];
        if (buffer.Count < 10)
            return false;

        var span = CollectionsMarshal.AsSpan(buffer);
        if (BinaryPrimitives.ReadUInt32LittleEndian(span) == Pm3CommandCodes.ResponsePreambleMagic)
        {
            Pm3NgPacketCodec.UnpackLengthNg(
                BinaryPrimitives.ReadUInt16LittleEndian(span[4..]),
                out var length,
                out _);

            var total = 10 + length + 2;
            if (buffer.Count < total)
                return false;

            frame = buffer.Take(total).ToArray();
            buffer.RemoveRange(0, total);
            return true;
        }

        if (buffer.Count < Pm3CommandCodes.OldFrameSize)
            return false;

        frame = buffer.Take(Pm3CommandCodes.OldFrameSize).ToArray();
        buffer.RemoveRange(0, Pm3CommandCodes.OldFrameSize);
        return true;
    }

    public ValueTask DisposeAsync()
    {
        Close();
        return ValueTask.CompletedTask;
    }

    private void Trace(string message) => _nativeTrace?.Invoke(message);
}
