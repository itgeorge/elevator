using NUnit.Framework;
using RidesBridge;

namespace RidesBridge.Tests;

[TestFixture]
public sealed class MutationSessionTests
{
    [Test]
    public async Task ProviderSessionUsesPreflightAndVerifiedWritesWithoutExtraReads()
    {
        var session = new RecordingMutationSession(new Dictionary<int, string>
        {
            [5] = "AAAAAAAA", [6] = "CCCCCCCC",
        });
        var device = new SessionDevice(session);

        var response = await new Page0ConditionalWriter(device).ExecuteAsync(Request(
            new Page0Mutation(5, "AAAAAAAA", "BBBBBBBB"),
            new Page0Mutation(6, "CCCCCCCC", "DDDDDDDD")));

        Assert.That(response.Status, Is.EqualTo("written"));
        Assert.That(response.Results.Select(r => r.Actual), Is.EqualTo(new[] { "BBBBBBBB", "DDDDDDDD" }));
        Assert.That(session.Calls, Is.EqualTo(new[] {
            "read5", "read6", "write5=BBBBBBBB", "write6=DDDDDDDD",
        }));
        Assert.That(session.OpenCount, Is.EqualTo(1));
        Assert.That(session.Disposed, Is.True);
    }

    [Test]
    public async Task IncorrectVerifiedWriteValueFailsAndRollsBackUsingSession()
    {
        var session = new RecordingMutationSession(new Dictionary<int, string> { [5] = "AAAAAAAA" })
        {
            VerifiedWriteResults = new Queue<string>(["EEEEEEEE", "AAAAAAAA"]),
        };
        var response = await new Page0ConditionalWriter(new SessionDevice(session)).ExecuteAsync(
            Request(new Page0Mutation(5, "AAAAAAAA", "BBBBBBBB")));

        Assert.That(response.Status, Is.EqualTo("verifyFailed"));
        Assert.That(response.Results[0].Status, Is.EqualTo("verifyFailed"));
        Assert.That(response.RollbackStatus, Is.EqualTo("rollbackSucceeded"));
        Assert.That(response.Rollback.Single().Actual, Is.EqualTo("AAAAAAAA"));
        Assert.That(session.Calls, Is.EqualTo(new[] {
            "read5", "write5=BBBBBBBB", "write5=AAAAAAAA",
        }));
        Assert.That(session.Disposed, Is.True);
    }

    [Test]
    public async Task MalformedVerifiedValueFailsAndRollsBack()
    {
        var session = new RecordingMutationSession(new Dictionary<int, string> { [5] = "AAAAAAAA" })
        {
            VerifiedWriteResults = new Queue<string>(["bad", "AAAAAAAA"]),
        };
        var response = await new Page0ConditionalWriter(new SessionDevice(session)).ExecuteAsync(
            Request(new Page0Mutation(5, "AAAAAAAA", "BBBBBBBB")));

        Assert.That(response.Status, Is.EqualTo("verifyFailed"));
        Assert.That(response.Rollback.Single().Succeeded, Is.True);
        Assert.That(session.Disposed, Is.True);
    }

    [Test]
    public async Task CanceledWriteRollsBackWithIndependentRecoveryTokenAndDisposesScope()
    {
        using var operationCts = new CancellationTokenSource();
        var session = new RecordingMutationSession(new Dictionary<int, string> { [5] = "AAAAAAAA" })
        {
            CancelOnFirstWrite = operationCts,
        };

        var response = await new Page0ConditionalWriter(new SessionDevice(session)).ExecuteAsync(
            Request(new Page0Mutation(5, "AAAAAAAA", "BBBBBBBB")), operationCts.Token);

        Assert.That(operationCts.IsCancellationRequested, Is.True);
        Assert.That(response.Status, Is.EqualTo("verifyFailed"));
        Assert.That(response.RollbackStatus, Is.EqualTo("rollbackSucceeded"));
        Assert.That(response.Rollback.Single().Actual, Is.EqualTo("AAAAAAAA"));
        Assert.That(session.WriteTokens, Has.Count.EqualTo(2));
        Assert.That(session.WriteTokens[0], Is.EqualTo(operationCts.Token));
        Assert.That(session.WriteTokens[1].IsCancellationRequested, Is.False);
        Assert.That(session.WriteTokens[1], Is.Not.EqualTo(operationCts.Token));
        Assert.That(session.Disposed, Is.True);
    }

    [TestCase("99999999", "conflict")]
    [TestCase("BBBBBBBB", "alreadyApplied")]
    public async Task ConflictOrAlreadyAppliedPerformsNoWritesAndDisposes(string current, string expectedStatus)
    {
        var session = new RecordingMutationSession(new Dictionary<int, string> { [5] = current });
        var response = await new Page0ConditionalWriter(new SessionDevice(session)).ExecuteAsync(
            Request(new Page0Mutation(5, "AAAAAAAA", "BBBBBBBB")));

        Assert.That(response.Status, Is.EqualTo(expectedStatus));
        Assert.That(session.Calls, Is.EqualTo(new[] { "read5" }));
        Assert.That(session.Disposed, Is.True);
    }

    [TestCase(0)]
    [TestCase(7)]
    public void InvalidBlockIsRejectedBeforeOpeningScope(int block)
    {
        var session = new RecordingMutationSession(new Dictionary<int, string>());
        var writer = new Page0ConditionalWriter(new SessionDevice(session));

        Assert.ThrowsAsync<Page0MutationValidationException>(async () => await writer.ExecuteAsync(
            Request(new Page0Mutation(block, "AAAAAAAA", "BBBBBBBB"))));
        Assert.That(session.OpenCount, Is.Zero);
    }

    [Test]
    public async Task AdapterMutationScopesDetectOnceAndUseOnlyDetectedNativeMethods()
    {
        var spy = new NativeSessionSpy();
        var options = new BridgeOptions { DataDirectory = Path.GetTempPath() };
        await using var adapter = new Pm3BridgeDeviceAdapter(options, _ => spy);
        var writer = new Page0ConditionalWriter(adapter);

        var first = await writer.ExecuteAsync(Request(
            new Page0Mutation(5, "AAAAAAAA", "BBBBBBBB"),
            new Page0Mutation(6, "CCCCCCCC", "DDDDDDDD")));
        var second = await writer.ExecuteAsync(Request(
            new Page0Mutation(5, "BBBBBBBB", "EEEEEEEE"),
            new Page0Mutation(6, "DDDDDDDD", "FFFFFFFF")));

        Assert.That(first.Status, Is.EqualTo("written"));
        Assert.That(second.Status, Is.EqualTo("written"));
        Assert.That(spy.EnsureCount, Is.EqualTo(2), "Each opened mutation scope must freshly detect once.");
        Assert.That(spy.IsConnectedCount, Is.EqualTo(2), "Connectivity should be checked at scope open, not per block.");
        Assert.That(spy.DetectedReads, Is.EqualTo(new uint[] { 5, 6, 5, 6 }));
        Assert.That(spy.VerifiedWrites, Is.EqualTo(new uint[] { 5, 6, 5, 6 }));
        Assert.That(spy.LegacyReads, Is.Empty);
        Assert.That(spy.LegacyWrites, Is.Empty);
        Assert.That(spy.Blocks[5], Is.EqualTo("EEEEEEEE"));
        Assert.That(spy.Blocks[6], Is.EqualTo("FFFFFFFF"));

        // A subsequent operation succeeding proves the previous scope released the adapter lock.
        Assert.That((await adapter.ReadPage0Block1To6Async(5)), Is.EqualTo("EEEEEEEE"));
    }

    private static Page0MutationRequest Request(params Page0Mutation[] mutations) => new("v1", mutations);

    private sealed class SessionDevice(RecordingMutationSession session) : IBridgePm3Device, IPage0MutationSessionProvider
    {
        public Task<IPage0MutationSession> OpenMutationSessionAsync(CancellationToken ct = default)
        {
            session.OpenCount++;
            return Task.FromResult<IPage0MutationSession>(session);
        }

        public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<string> ReadPage0Block5Async(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<string> ReadPage0Block6Async(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<(string Block5Hex, string Block6Hex)> ReadPage0MirrorAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task WritePage0Block5Async(string value, CancellationToken ct = default) => throw new NotImplementedException();
        public Task WritePage0Block6Async(string value, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<string> ReadPage0Block1To6Async(int block, CancellationToken ct = default) => throw new NotImplementedException();
        public Task WritePage0Block1To6Async(int block, string value, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<Page0BlockReadResult>> ReadPage0Blocks1To6Async(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Page0ScanReadResult> ScanPage0Async(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<Page0BlockReadResult>> ReadPage0MissingBlocksAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class NativeSessionSpy : IBridgePm3Session
    {
        public Dictionary<uint, string> Blocks { get; } = new() { [5] = "AAAAAAAA", [6] = "CCCCCCCC" };
        public List<uint> DetectedReads { get; } = [];
        public List<uint> VerifiedWrites { get; } = [];
        public List<uint> LegacyReads { get; } = [];
        public List<uint> LegacyWrites { get; } = [];
        public int EnsureCount { get; private set; }
        public int IsConnectedCount { get; private set; }

        public Task<bool> IsConnectedAsync(CancellationToken ct = default) { IsConnectedCount++; return Task.FromResult(true); }
        public Task ConnectAsync(CancellationToken ct = default) => Task.CompletedTask;
        public void InvalidateT55DetectCache() { }
        public Task EnsureT55SessionActiveAsync(CancellationToken ct = default) { EnsureCount++; return Task.CompletedTask; }
        public Task StartLfTuneAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<uint> GetLfTuneLastMilliVoltsAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<string> ReadPage0BlockAsync(uint block, CancellationToken ct = default)
        {
            LegacyReads.Add(block);
            return Task.FromResult(Blocks[block]);
        }
        public Task WritePage0BlockAsync(uint block, Tokens.T55Block data, CancellationToken ct = default)
        {
            LegacyWrites.Add(block);
            Blocks[block] = data.ToHex().ToUpperInvariant();
            return Task.CompletedTask;
        }
        public Task<string> ReadDetectedPage0BlockAsync(uint block, CancellationToken ct = default)
        {
            DetectedReads.Add(block);
            return Task.FromResult(Blocks[block]);
        }
        public Task<string> WriteDetectedPage0BlockVerifiedAsync(uint block, Tokens.T55Block data, CancellationToken ct = default)
        {
            VerifiedWrites.Add(block);
            var actual = data.ToHex().ToUpperInvariant();
            Blocks[block] = actual;
            return Task.FromResult(actual);
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingMutationSession(Dictionary<int, string> blocks) : IPage0MutationSession
    {
        public List<string> Calls { get; } = [];
        public Queue<string>? VerifiedWriteResults { get; init; }
        public CancellationTokenSource? CancelOnFirstWrite { get; init; }
        public List<CancellationToken> WriteTokens { get; } = [];
        public int OpenCount { get; set; }
        public bool Disposed { get; private set; }

        public Task<string> ReadBlockAsync(int block, CancellationToken ct = default)
        {
            Calls.Add($"read{block}");
            return Task.FromResult(blocks[block]);
        }

        public Task<string> WriteVerifiedBlockAsync(int block, string value, CancellationToken ct = default)
        {
            Calls.Add($"write{block}={value}");
            WriteTokens.Add(ct);
            if (WriteTokens.Count == 1 && CancelOnFirstWrite is not null)
            {
                CancelOnFirstWrite.Cancel();
                throw new OperationCanceledException(ct);
            }
            blocks[block] = value;
            var actual = VerifiedWriteResults is { Count: > 0 } ? VerifiedWriteResults.Dequeue() : value;
            blocks[block] = actual;
            return Task.FromResult(actual);
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
