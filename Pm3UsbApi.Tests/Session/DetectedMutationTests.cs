using NUnit.Framework;
using Pm3UsbApi;
using Pm3UsbApi.Commands;
using Pm3UsbApi.Execution;
using Pm3UsbApi.Session;
using Tokens;

namespace Pm3UsbApi.Tests.Session;

[TestFixture]
public sealed class DetectedMutationTests
{
    [Test]
    public async Task VerifiedDetectedWrite_UsesSingleRawWriteCommand()
    {
        var executor = new RecordingExecutor();
        var session = new Pm3Session(executor, new Pm3Options
        {
            ExecutorKind = Pm3ExecutorKind.Native,
            DevicePort = "/dev/test",
        });
        await using var pm3 = new Pm3(session, Pm3ExecutorKind.Native);

        var actual = await pm3.WriteDetectedPage0BlockVerifiedAsync(5, new T55Block(0xDEADBEEF));

        Assert.That(actual, Is.EqualTo("DEADBEEF"));
        Assert.That(executor.Batches, Has.Count.EqualTo(1));
        Assert.That(executor.Batches[0], Has.Count.EqualTo(1));
        Assert.That(executor.Batches[0][0], Is.InstanceOf<T55WriteBlockCommand>());
        var write = (T55WriteBlockCommand)executor.Batches[0][0];
        Assert.That(write.Block, Is.EqualTo(5));
        Assert.That(write.Data.ToHex(), Is.EqualTo("DEADBEEF"));
    }

    [Test]
    public void VerifiedDetectedWrite_RejectsNonNativeExecutor()
    {
        var executor = new RecordingExecutor();
        var session = new Pm3Session(executor, new Pm3Options
        {
            ExecutorKind = Pm3ExecutorKind.Process,
            DevicePort = "COM1",
        });
        var pm3 = new Pm3(session, Pm3ExecutorKind.Process);

        Assert.ThrowsAsync<InvalidOperationException>(() =>
            pm3.WriteDetectedPage0BlockVerifiedAsync(5, new T55Block(0xDEADBEEF)));
        Assert.That(executor.Batches, Is.Empty);
    }

    private sealed class RecordingExecutor : IPm3CommandExecutor
    {
        public List<IReadOnlyList<IPm3DeviceCommand>> Batches { get; } = [];

        public Task<CommandResult> ExecuteAsync(
            IReadOnlyList<IPm3DeviceCommand> commands,
            TimeSpan? timeout = null,
            CancellationToken ct = default,
            string? portOverride = null)
        {
            Batches.Add(commands);
            return Task.FromResult(new CommandResult
            {
                Commands = commands,
                OutputLines = ["[=] Writing page 0 block: 05 (successfully verified)"],
                ExitCode = 0,
                HasErrors = false,
            });
        }

        public Task CancelCurrentAsync(CancellationToken ct = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
