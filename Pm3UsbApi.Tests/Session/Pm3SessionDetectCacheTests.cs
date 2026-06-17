using NUnit.Framework;
using Pm3UsbApi;
using Pm3UsbApi.Commands;
using Pm3UsbApi.Execution;
using Pm3UsbApi.Parsers;
using Pm3UsbApi.Session;
using Tokens;

namespace Pm3UsbApi.Tests.Session;

[TestFixture]
public class Pm3SessionDetectCacheTests
{
  private static CommandResult DetectAndReadSuccess() => new()
  {
    Commands = [new T55DetectCommand(), new T55ReadBlockCommand(5)],
    OutputLines =
    [
      "[=] Chip Type: T55x7",
      "[=] Modulation: ASK",
      "[=] Block0: 0x00148040",
      "[+] Block 5: DEADBEEF",
    ],
    ExitCode = 0,
    HasErrors = false,
  };

  private static CommandResult ReadOnlySuccess() => new()
  {
    Commands = [new T55ReadBlockCommand(5)],
    OutputLines = ["[+] Block 5: DEADBEEF"],
    ExitCode = 0,
    HasErrors = false,
  };

  [Test]
  public async Task ExecuteT55Async_SecondRead_SkipsDetect_OnNativeExecutor()
  {
    var executor = new RecordingExecutor([DetectAndReadSuccess(), ReadOnlySuccess()]);
    var session = new Pm3Session(executor, new Pm3Options
    {
      ExecutorKind = Pm3ExecutorKind.Native,
      DevicePort = "/dev/cu.usbmodem1",
    });

    await session.ExecuteT55Async(new T55ReadBlockCommand(5));
    await session.ExecuteT55Async(new T55ReadBlockCommand(5));

    Assert.That(executor.Batches, Has.Count.EqualTo(2));
    Assert.That(executor.Batches[0], Has.Count.EqualTo(2));
    Assert.That(executor.Batches[0][0], Is.InstanceOf<T55DetectCommand>());
    Assert.That(executor.Batches[1], Has.Count.EqualTo(1));
    Assert.That(executor.Batches[1][0], Is.InstanceOf<T55ReadBlockCommand>());
  }

  [Test]
  public async Task ExecuteT55Async_AlwaysPrependsDetect_OnProcessExecutor()
  {
    var executor = new RecordingExecutor([DetectAndReadSuccess(), DetectAndReadSuccess()]);
    var session = new Pm3Session(executor, new Pm3Options
    {
      ExecutorKind = Pm3ExecutorKind.Process,
      DevicePort = "COM4",
    });

    await session.ExecuteT55Async(new T55ReadBlockCommand(5));
    await session.ExecuteT55Async(new T55ReadBlockCommand(5));

    Assert.That(executor.Batches[0], Has.Count.EqualTo(2));
    Assert.That(executor.Batches[1], Has.Count.EqualTo(2));
  }

  [Test]
  public async Task ExecuteAsync_LfTune_InvalidatesDetectCache()
  {
    var executor = new RecordingExecutor([
      DetectAndReadSuccess(),
      new CommandResult
      {
        Commands = [new LfTuneCommand()],
        OutputLines = ["[=] 12000 mV"],
        ExitCode = 0,
        HasErrors = false,
      },
      DetectAndReadSuccess(),
    ]);

    var session = new Pm3Session(executor, new Pm3Options
    {
      ExecutorKind = Pm3ExecutorKind.Native,
      DevicePort = "/dev/cu.usbmodem1",
    });

    await session.ExecuteT55Async(new T55ReadBlockCommand(5));
    await session.ExecuteAsync([new LfTuneCommand()]);
    await session.ExecuteT55Async(new T55ReadBlockCommand(5));

    Assert.That(executor.Batches[2], Has.Count.EqualTo(2));
    Assert.That(executor.Batches[2][0], Is.InstanceOf<T55DetectCommand>());
  }

  [Test]
  public async Task ExecuteT55Async_Write_InvalidatesDetectCache()
  {
    var executor = new RecordingExecutor([
      DetectAndReadSuccess(),
      new CommandResult
      {
        Commands = [new T55DetectCommand(), new T55WriteBlockCommand(5, new T55Block(0xDEADBEEF))],
        OutputLines =
        [
          "[=] Chip Type: T55x7",
          "[=] Block0: 0x00148040",
          "[=] Writing page 0  block: 05",
        ],
        ExitCode = 0,
        HasErrors = false,
      },
      DetectAndReadSuccess(),
    ]);

    var session = new Pm3Session(executor, new Pm3Options
    {
      ExecutorKind = Pm3ExecutorKind.Native,
      DevicePort = "/dev/cu.usbmodem1",
    });

    await session.ExecuteT55Async(new T55ReadBlockCommand(5));
    await session.ExecuteT55Async(new T55WriteBlockCommand(5, new T55Block(0xDEADBEEF)));
    await session.ExecuteT55Async(new T55ReadBlockCommand(5));

    Assert.That(executor.Batches[2], Has.Count.EqualTo(2));
  }

  [Test]
  public async Task ExecuteT55Async_ReadFailureAfterCacheHit_InvalidatesCache()
  {
    var executor = new RecordingExecutor([
      DetectAndReadSuccess(),
      new CommandResult
      {
        Commands = [new T55ReadBlockCommand(5)],
        OutputLines = ["[!] Could not read block 5"],
        ExitCode = 1,
        HasErrors = true,
      },
      DetectAndReadSuccess(),
    ]);

    var session = new Pm3Session(executor, new Pm3Options
    {
      ExecutorKind = Pm3ExecutorKind.Native,
      DevicePort = "/dev/cu.usbmodem1",
    });

    await session.ExecuteT55Async(new T55ReadBlockCommand(5));
    await session.ExecuteT55Async(new T55ReadBlockCommand(5));
    await session.ExecuteT55Async(new T55ReadBlockCommand(5));

    Assert.That(executor.Batches[1], Has.Count.EqualTo(1));
    Assert.That(executor.Batches[2], Has.Count.EqualTo(2));
  }

  private sealed class RecordingExecutor : IPm3CommandExecutor
  {
    private readonly Queue<CommandResult> _results;

    public RecordingExecutor(IEnumerable<CommandResult> results) =>
      _results = new Queue<CommandResult>(results);

    public List<IReadOnlyList<IPm3DeviceCommand>> Batches { get; } = [];

    public Task<CommandResult> ExecuteAsync(
      IReadOnlyList<IPm3DeviceCommand> commands,
      TimeSpan? timeout = null,
      CancellationToken ct = default,
      string? portOverride = null)
    {
      Batches.Add(commands);
      if (_results.Count == 0)
        throw new InvalidOperationException("No more stubbed results.");

      var template = _results.Dequeue();
      return Task.FromResult(new CommandResult
      {
        Commands = commands,
        OutputLines = template.OutputLines,
        ExitCode = template.ExitCode,
        HasErrors = template.HasErrors,
        ErrorSummary = template.ErrorSummary,
      });
    }

    public Task CancelCurrentAsync(CancellationToken ct = default) => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
  }
}
