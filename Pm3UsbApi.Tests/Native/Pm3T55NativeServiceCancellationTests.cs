using NUnit.Framework;
using Pm3UsbApi.Native.Demod;
using Pm3UsbApi.Native.Protocol;
using Pm3UsbApi.Native.T55;

namespace Pm3UsbApi.Tests.Native;

[TestFixture]
public class Pm3T55NativeServiceCancellationTests
{
    [Test]
    public void ReadBlock_DownloadCancellationOnSecondAcquireAttempt_IsRethrownWithoutFurtherRetries()
    {
        var cancellation = new OperationCanceledException();
        var transport = new FakeTransport(
            new TimeoutException(),
            cancellation)
        {
            DefaultDownloadException = cancellation,
        };
        var service = new Pm3T55NativeService(transport);

        var exception = Assert.Throws<OperationCanceledException>(() =>
            service.ReadBlock(DetectedConfig(), block: 0, out _, CancellationToken.None));

        Assert.That(exception, Is.SameAs(cancellation));
        Assert.That(transport.ReadCommandCalls, Is.EqualTo(2));
        Assert.That(transport.DownloadCalls, Is.EqualTo(2));
    }

    [Test]
    public void WriteBlock_VerifyDownloadCancellation_IsRethrownWithoutFurtherWriteRetries()
    {
        var cancellation = new OperationCanceledException();
        var transport = new FakeTransport(
            new TimeoutException(),
            cancellation)
        {
            DefaultDownloadException = cancellation,
        };
        var service = new Pm3T55NativeService(transport);

        var exception = Assert.Throws<OperationCanceledException>(() =>
            service.WriteBlock(DetectedConfig(), block: 0, data: 0x12345678, CancellationToken.None));

        Assert.That(exception, Is.SameAs(cancellation));
        Assert.That(transport.WriteCommandCalls, Is.EqualTo(1));
        Assert.That(transport.ReadCommandCalls, Is.EqualTo(2));
        Assert.That(transport.DownloadCalls, Is.EqualTo(2));
    }

    [Test]
    public void ReadBlock_DownloadTimeout_RetainsRetryBehavior()
    {
        var transport = new FakeTransport
        {
            DefaultDownloadException = new TimeoutException(),
        };
        var service = new Pm3T55NativeService(transport);

        var result = service.ReadBlock(DetectedConfig(), block: 0, out _, CancellationToken.None);

        Assert.That(result, Is.False);
        Assert.That(transport.ReadCommandCalls, Is.EqualTo(6));
        Assert.That(transport.DownloadCalls, Is.EqualTo(6));
    }

    private static Pm3T55Config DetectedConfig() => new()
    {
        Detected = true,
        Clock = Pm3LfDemod.TokenClock,
        Offset = 32,
    };

    private sealed class FakeTransport(params Exception?[] downloadExceptions) : IPm3T55Transport
    {
        private readonly Queue<Exception?> _downloadExceptions = new(downloadExceptions);

        public Pm3Capabilities Capabilities => Pm3Capabilities.CreateDefault();
        public Exception? DefaultDownloadException { get; init; }
        public int WriteCommandCalls { get; private set; }
        public int ReadCommandCalls { get; private set; }
        public int DownloadCalls { get; private set; }

        public void DiscardPendingInput()
        {
        }

        public Pm3ResponseFrame SendCommandAndWait(
            ushort command,
            ReadOnlySpan<byte> payload,
            ushort expectedResponseCommand,
            TimeSpan timeout,
            CancellationToken ct)
        {
            if (command == Pm3CommandCodes.CmdLfT55XxWriteBl)
                WriteCommandCalls++;
            else if (command == Pm3CommandCodes.CmdLfT55XxReadBl)
                ReadCommandCalls++;

            return new Pm3ResponseFrame
            {
                Command = expectedResponseCommand,
                Status = Pm3CommandCodes.Pm3Success,
                Reason = 0,
                IsNg = true,
                Data = [],
            };
        }

        public byte[] DownloadBigBuf(uint startIndex, uint byteCount, TimeSpan timeout, CancellationToken ct)
        {
            DownloadCalls++;
            var exception = _downloadExceptions.Count > 0
                ? _downloadExceptions.Dequeue()
                : DefaultDownloadException;
            if (exception is not null)
                throw exception;

            return [];
        }
    }
}
