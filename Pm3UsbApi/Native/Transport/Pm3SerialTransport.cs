using System.Buffers.Binary;
using System.IO.Ports;
using System.Runtime.InteropServices;
using Pm3UsbApi.Native.Protocol;

namespace Pm3UsbApi.Native.Transport;

/// <summary>
/// USB CDC serial transport for Proxmark3 NG packets.
/// </summary>
internal sealed class Pm3SerialTransport : IAsyncDisposable
{
    private readonly string _portName;
    private readonly int _baudRate;
    private SerialPort? _port;
    private readonly object _lock = new();

    public Pm3SerialTransport(string portName, int baudRate = 115200)
    {
        if (string.IsNullOrWhiteSpace(portName))
            throw new ArgumentException("Port name is required.", nameof(portName));
        _portName = portName.Trim();
        _baudRate = baudRate;
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
                ReadTimeout = 500,
                WriteTimeout = 2000,
                DtrEnable = true,
                RtsEnable = true,
            };
            _port.Open();
            _port.DiscardInBuffer();
            _port.DiscardOutBuffer();
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

    public Pm3ResponseFrame SendCommand(ushort command, ReadOnlySpan<byte> payload, TimeSpan timeout, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var frame = Pm3NgPacketCodec.EncodeCommand(command, payload);
        Write(frame);
        var responseBytes = ReadResponseFrame(timeout, ct);
        return Pm3NgPacketCodec.DecodeResponse(responseBytes);
    }

    public bool TryPing(TimeSpan timeout, CancellationToken ct)
    {
        try
        {
            Open();
            var response = SendCommand(Pm3CommandCodes.CmdPing, ReadOnlySpan<byte>.Empty, timeout, ct);
            return response.Command == Pm3CommandCodes.CmdPing && response.Status == Pm3CommandCodes.Pm3Success;
        }
        catch
        {
            return false;
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
        var buffer = new List<byte>(128);

        while (Environment.TickCount64 < deadline)
        {
            ct.ThrowIfCancellationRequested();
            var chunk = ReadAvailable(Math.Max(1, (int)(deadline - Environment.TickCount64)));
            if (chunk.Length == 0)
            {
                Thread.Sleep(1);
                continue;
            }

            buffer.AddRange(chunk);
            if (TryExtractResponseFrame(buffer, out var frame))
                return frame;
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
                port.ReadTimeout = Math.Clamp(maxWaitMs, 1, 500);
                var buffer = new byte[1024];
                var read = 0;
                try
                {
                    read = port.Read(buffer, 0, buffer.Length);
                }
                catch (TimeoutException)
                {
                    return [];
                }

                return buffer.AsSpan(0, read).ToArray();
            }
            finally
            {
                port.ReadTimeout = originalTimeout;
            }
        }
    }

    private static bool TryExtractResponseFrame(List<byte> buffer, out byte[] frame)
    {
        frame = [];
        for (var i = 0; i <= buffer.Count - 10; i++)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(CollectionsMarshal.AsSpan(buffer).Slice(i)) !=
                Pm3CommandCodes.ResponsePreambleMagic)
                continue;

            Pm3NgPacketCodec.UnpackLengthNg(
                BinaryPrimitives.ReadUInt16LittleEndian(CollectionsMarshal.AsSpan(buffer).Slice(i + 4)),
                out var length,
                out _);

            var total = 10 + length + 2;
            if (buffer.Count < i + total)
                continue;

            frame = buffer.Skip(i).Take(total).ToArray();
            return true;
        }

        return false;
    }

    public ValueTask DisposeAsync()
    {
        Close();
        return ValueTask.CompletedTask;
    }
}
