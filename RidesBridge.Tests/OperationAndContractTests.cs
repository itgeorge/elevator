using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using NUnit.Framework;
using Pm3UsbApi;
using RidesBridge;

namespace RidesBridge.Tests;

[TestFixture]
public sealed class OperationAndContractTests
{
    [TestCase(0)]
    [TestCase(30)]
    public void GateRejectsWaitTimeoutAtOrBeyondTabletDeadline(double seconds)
    {
        Assert.That(() => new BridgeOperationGate(TimeSpan.FromSeconds(seconds)),
            Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public async Task GateSerializesOperations()
    {
        var gate = new BridgeOperationGate();
        var active = 0;
        var maximum = 0;
        async Task<int> Work(CancellationToken ct)
        {
            var now = Interlocked.Increment(ref active);
            InterlockedExtensions.Max(ref maximum, now);
            await Task.Delay(30, ct);
            Interlocked.Decrement(ref active);
            return now;
        }

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => gate.ExecuteAsync(Work)));
        Assert.That(results, Is.All.EqualTo(1));
        Assert.That(maximum, Is.EqualTo(1));
    }

    [Test]
    public async Task GateCancelsHardwareOperationAtServerDeadlineAndDoesNotReportBusy()
    {
        var gate = new BridgeOperationGate(TimeSpan.FromSeconds(1));
        var cancellationObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var error = Assert.ThrowsAsync<BridgeHardwareException>(async () => await gate.ExecuteAsync(async ct =>
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                return true;
            }
            catch (OperationCanceledException)
            {
                cancellationObserved.SetResult();
                throw;
            }
        }, CancellationToken.None, waitTimeout: TimeSpan.FromSeconds(1), operationTimeout: TimeSpan.FromMilliseconds(40)));

        Assert.That(error!.Error, Is.EqualTo(BridgeHardwareError.Timeout));
        await cancellationObserved.Task;
    }

    [Test]
    public async Task GatePreservesRequestCancellationInsteadOfMappingItToHardwareTimeout()
    {
        var request = new CancellationTokenSource();
        var operationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var operation = new BridgeOperationGate().ExecuteAsync(async ct =>
        {
            operationStarted.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return true;
        }, request.Token, waitTimeout: TimeSpan.FromSeconds(1), operationTimeout: TimeSpan.FromSeconds(1));

        await operationStarted.Task;
        request.Cancel();
        var error = Assert.CatchAsync(async () => await operation);
        Assert.That(error, Is.InstanceOf<OperationCanceledException>());
        Assert.That(error, Is.Not.InstanceOf<BridgeHardwareException>());
    }

    [Test]
    public async Task HardwareEndpointMapsServerDeadlineToStableTimeoutResponse()
    {
        var options = new BridgeOptions
        {
            BindUrl = "http://127.0.0.1:5080",
            DataDirectory = Path.GetTempPath(),
            HardwareExecutionTimeout = TimeSpan.FromMilliseconds(40),
            OperationWaitTimeout = TimeSpan.FromSeconds(1),
        };
        var cancellationObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fake = new FakeBridgePm3Device(read: async ct =>
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                return "A1B2C3D4";
            }
            catch (OperationCanceledException)
            {
                cancellationObserved.SetResult();
                throw;
            }
        });
        await using var host = await BridgeTestHost.CreateAsync(fake, options: options);
        host.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await host.PairAsync());

        var response = await host.Client.GetAsync("/api/v1/hardware/page0/block5");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.GatewayTimeout));
        var error = await response.Content.ReadFromJsonAsync<BridgeErrorResponse>();
        Assert.That(error, Is.EqualTo(new BridgeErrorResponse("pm3_timeout", "Proxmark3 operation timed out.")));
        await cancellationObserved.Task;
    }

    [Test]
    public async Task GateWaitTimeoutRemainsBusyAndDoesNotConsumeHardwareExecutionDeadline()
    {
        var gate = new BridgeOperationGate(TimeSpan.FromMilliseconds(30));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = gate.ExecuteAsync(async ct =>
        {
            entered.SetResult();
            await release.Task.WaitAsync(ct);
            return true;
        }, CancellationToken.None, waitTimeout: TimeSpan.FromSeconds(1), operationTimeout: TimeSpan.FromSeconds(1));
        await entered.Task;

        var error = Assert.ThrowsAsync<BridgeHardwareException>(async () =>
            await gate.ExecuteAsync(_ => Task.FromResult(true), CancellationToken.None, waitTimeout: TimeSpan.FromMilliseconds(30), operationTimeout: TimeSpan.FromSeconds(1)));

        Assert.That(error!.Error, Is.EqualTo(BridgeHardwareError.Busy));
        release.SetResult();
        await first;
    }

    [Test]
    public async Task GateWaitTimeDoesNotConsumeHardwareExecutionBudget()
    {
        var gate = new BridgeOperationGate(TimeSpan.FromSeconds(1));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = gate.ExecuteAsync(async ct =>
        {
            entered.SetResult();
            await release.Task.WaitAsync(ct);
            return true;
        });
        await entered.Task;

        var second = gate.ExecuteAsync(async ct =>
        {
            await Task.Delay(10, ct);
            return true;
        }, CancellationToken.None, waitTimeout: TimeSpan.FromSeconds(1), operationTimeout: TimeSpan.FromMilliseconds(50));
        await Task.Delay(120);
        release.SetResult();

        Assert.That(await second, Is.True);
        await first;
    }

    [Test]
    public async Task GateReportsBusyAfterBoundedWaitWithoutEnteringOperation()
    {
        var gate = new BridgeOperationGate(TimeSpan.FromMilliseconds(20));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = gate.ExecuteAsync(async ct =>
        {
            entered.SetResult();
            await release.Task.WaitAsync(ct);
            return true;
        });
        await entered.Task;
        var error = Assert.ThrowsAsync<BridgeHardwareException>(async () => await gate.ExecuteAsync(_ => Task.FromResult(true)));
        Assert.That(error!.Error, Is.EqualTo(BridgeHardwareError.Busy));
        release.SetResult();
        await first;
    }

    [Test]
    public async Task CancellationWhileWaitingNeverEntersDeviceCall()
    {
        var gate = new BridgeOperationGate();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var first = gate.ExecuteAsync(async ct =>
        {
            Interlocked.Increment(ref calls);
            entered.SetResult();
            await release.Task.WaitAsync(ct);
            return true;
        });
        await entered.Task;
        using var cts = new CancellationTokenSource();
        var waiting = gate.ExecuteAsync(async ct =>
        {
            Interlocked.Increment(ref calls);
            await Task.Yield();
            return true;
        }, cts.Token);
        cts.Cancel();
        Assert.ThrowsAsync<OperationCanceledException>(async () => await waiting);
        release.SetResult();
        await first;
        Assert.That(calls, Is.EqualTo(1));
    }

    [Test]
    public async Task LifecycleStopWaitsForOperationGateBeforeDisposingDeviceAndIsIdempotent()
    {
        var gate = new BridgeOperationGate();
        var device = new FakeBridgePm3Device();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var operation = gate.ExecuteAsync(async ct =>
        {
            entered.SetResult();
            await release.Task.WaitAsync(ct);
            return true;
        });
        await entered.Task;

        var lifecycle = new BridgeLifecycleService(device, gate);
        var stop = lifecycle.StopAsync(CancellationToken.None);
        await Task.Delay(30);
        Assert.That(device.Disposed, Is.False);

        release.SetResult();
        await operation;
        await stop;
        await lifecycle.StopAsync(CancellationToken.None);
        Assert.That(device.Disposed, Is.True);
    }

    [Test]
    public async Task AdapterInvalidatesT55DetectionBeforeEveryBlock5Read()
    {
        var calls = new List<string>();
        var session = new FakeBridgePm3Session(calls);
        var adapter = new Pm3BridgeDeviceAdapter(
            new BridgeOptions { BindUrl = "http://127.0.0.1:5080", DataDirectory = Path.GetTempPath() },
            _ => session);

        Assert.That(await adapter.ReadPage0Block5Async(), Is.EqualTo("A1B2C3D4"));
        Assert.That(await adapter.ReadPage0Block5Async(), Is.EqualTo("A1B2C3D4"));
        await adapter.DisposeAsync();

        Assert.That(calls, Is.EqualTo(new[]
        {
            "connected", "invalidate", "ensure", "read",
            "connected", "invalidate", "ensure", "read", "dispose",
        }));
    }

    [Test]
    public async Task AdapterReleasesOperationLockAfterFailedRead()
    {
        var readCount = 0;
        var calls = new List<string>();
        var session = new FakeBridgePm3Session(calls, _ =>
            Interlocked.Increment(ref readCount) == 1
                ? Task.FromException<string>(new IOException("read failed"))
                : Task.FromResult("A1B2C3D4"));
        var adapter = new Pm3BridgeDeviceAdapter(
            new BridgeOptions { BindUrl = "http://127.0.0.1:5080", DataDirectory = Path.GetTempPath() },
            _ => session);

        var error = Assert.ThrowsAsync<BridgeHardwareException>(async () => await adapter.ReadPage0Block5Async());
        var value = await adapter.ReadPage0Block5Async();

        Assert.That(error!.Error, Is.EqualTo(BridgeHardwareError.Unavailable));
        Assert.That(value, Is.EqualTo("A1B2C3D4"));
        await adapter.DisposeAsync();
    }

    [Test]
    public async Task AdapterReleasesOperationLockAfterCancelledReadAndUsesFreshSession()
    {
        var firstCalls = new List<string>();
        var secondCalls = new List<string>();
        var first = new FakeBridgePm3Session(firstCalls, async ct =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return "A1B2C3D4";
        });
        var second = new FakeBridgePm3Session(secondCalls);
        var sessions = new Queue<FakeBridgePm3Session>([first, second]);
        var adapter = new Pm3BridgeDeviceAdapter(
            new BridgeOptions { BindUrl = "http://127.0.0.1:5080", DataDirectory = Path.GetTempPath() },
            _ => sessions.Dequeue());
        using var request = new CancellationTokenSource(TimeSpan.FromMilliseconds(40));

        var error = Assert.CatchAsync(async () => await adapter.ReadPage0Block5Async(request.Token));
        var value = await adapter.ReadPage0Block5Async();

        Assert.That(error, Is.InstanceOf<OperationCanceledException>());
        Assert.That(error, Is.Not.InstanceOf<BridgeHardwareException>());
        Assert.That(first.Disposed, Is.True);
        Assert.That(value, Is.EqualTo("A1B2C3D4"));
        Assert.That(second.Disposed, Is.False);
        await adapter.DisposeAsync();
    }

    [Test]
    public async Task TimedOutReadDisposesSessionReleasesGateAndLifecycleShutdownDisposesRecoverySession()
    {
        var first = new FakeBridgePm3Session([], async ct =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return "A1B2C3D4";
        });
        var second = new FakeBridgePm3Session([]);
        var sessions = new Queue<FakeBridgePm3Session>([first, second]);
        var adapter = new Pm3BridgeDeviceAdapter(
            new BridgeOptions { BindUrl = "http://127.0.0.1:5080", DataDirectory = Path.GetTempPath() },
            _ => sessions.Dequeue());
        var gate = new BridgeOperationGate(TimeSpan.FromSeconds(1));
        var lifecycle = new BridgeLifecycleService(adapter, gate);

        try
        {
            var error = Assert.ThrowsAsync<BridgeHardwareException>(async () => await gate.ExecuteAsync(
                ct => adapter.ReadPage0Block5Async(ct),
                CancellationToken.None,
                waitTimeout: TimeSpan.FromSeconds(1),
                operationTimeout: TimeSpan.FromMilliseconds(40)));
            Assert.That(error!.Error, Is.EqualTo(BridgeHardwareError.Timeout));
            Assert.That(first.Disposed, Is.True);

            var value = await gate.ExecuteAsync(
                ct => adapter.ReadPage0Block5Async(ct),
                CancellationToken.None,
                waitTimeout: TimeSpan.FromSeconds(1),
                operationTimeout: TimeSpan.FromSeconds(1));
            Assert.That(value, Is.EqualTo("A1B2C3D4"));
        }
        finally
        {
            await lifecycle.StopAsync(CancellationToken.None);
        }

        Assert.That(second.Disposed, Is.True);
    }

    [Test]
    public async Task CancelledReadReleasesGateAndAllowsLifecycleShutdown()
    {
        var operationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var session = new FakeBridgePm3Session([], async ct =>
        {
            operationStarted.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return "A1B2C3D4";
        });
        var adapter = new Pm3BridgeDeviceAdapter(
            new BridgeOptions { BindUrl = "http://127.0.0.1:5080", DataDirectory = Path.GetTempPath() },
            _ => session);
        var gate = new BridgeOperationGate(TimeSpan.FromSeconds(1));
        var lifecycle = new BridgeLifecycleService(adapter, gate);
        using var request = new CancellationTokenSource();

        var operation = gate.ExecuteAsync(
            ct => adapter.ReadPage0Block5Async(ct),
            request.Token,
            waitTimeout: TimeSpan.FromSeconds(1),
            operationTimeout: TimeSpan.FromSeconds(1));
        await operationStarted.Task;
        request.Cancel();
        var error = Assert.CatchAsync(async () => await operation);

        Assert.That(error, Is.InstanceOf<OperationCanceledException>());
        Assert.That(session.Disposed, Is.True);
        await lifecycle.StopAsync(CancellationToken.None);
        var late = Assert.ThrowsAsync<BridgeHardwareException>(async () => await adapter.ReadPage0Block5Async());
        Assert.That(late!.Error, Is.EqualTo(BridgeHardwareError.Unavailable));
    }

    [Test]
    public async Task AdapterDisposesSessionAfterPm3TimeoutAndUsesFreshSession()
    {
        var first = new FakeBridgePm3Session(
            [],
            _ => Task.FromException<string>(new Pm3TimeoutException("timed out")),
            new IOException("cleanup failed"));
        var second = new FakeBridgePm3Session([]);
        var sessions = new Queue<FakeBridgePm3Session>([first, second]);
        var adapter = new Pm3BridgeDeviceAdapter(
            new BridgeOptions { BindUrl = "http://127.0.0.1:5080", DataDirectory = Path.GetTempPath() },
            _ => sessions.Dequeue());

        var error = Assert.ThrowsAsync<BridgeHardwareException>(async () => await adapter.ReadPage0Block5Async());
        var value = await adapter.ReadPage0Block5Async();

        Assert.That(error!.Error, Is.EqualTo(BridgeHardwareError.Timeout));
        Assert.That(first.Disposed, Is.True);
        Assert.That(value, Is.EqualTo("A1B2C3D4"));
        await adapter.DisposeAsync();
    }

    [Test]
    public async Task AdapterCanBeDisposedRepeatedlyAndGuardsLateCalls()
    {
        var adapter = new Pm3BridgeDeviceAdapter(new BridgeOptions
        {
            BindUrl = "http://127.0.0.1:5080",
            DataDirectory = Path.GetTempPath(),
            Pm3AutoDiscover = false,
            Pm3Port = "/dev/does-not-exist",
        });

        await adapter.DisposeAsync();
        await adapter.DisposeAsync();
        var error = Assert.ThrowsAsync<BridgeHardwareException>(async () => await adapter.ReadPage0Block5Async());
        Assert.That(error!.Error, Is.EqualTo(BridgeHardwareError.Unavailable));
    }

    [Test]
    public async Task HealthContractIsVersionedSecretFreeAndHardwareReadIsExactUppercaseHex()
    {
        var device = new FakeBridgePm3Device("deadbeef");
        await using var host = await BridgeTestHost.CreateAsync(device);
        var health = await host.Client.GetFromJsonAsync<HealthResponse>("/api/v1/health");
        Assert.That(health, Is.EqualTo(new HealthResponse("ok", "v1", "1.0.0")));
        var token = await host.PairAsync();
        host.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var read = await host.Client.GetFromJsonAsync<BlockReadResponse>("/api/v1/hardware/page0/block5");
        Assert.That(read!.Value, Is.EqualTo("DEADBEEF"));
        Assert.That(read.Block, Is.EqualTo(5));
    }

    [Test]
    public async Task MalformedDeviceResponseIsMappedAndDoesNotExposeRawData()
    {
        await using var host = await BridgeTestHost.CreateAsync(new FakeBridgePm3Device("not-a-block"));
        host.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await host.PairAsync());
        var response = await host.Client.GetAsync("/api/v1/hardware/page0/block5");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadGateway));
        var error = await response.Content.ReadFromJsonAsync<BridgeErrorResponse>();
        Assert.That(error!.Code, Is.EqualTo("malformed_device_response"));
        Assert.That(error.Message, Does.Not.Contain("not-a-block"));
    }

    [Test]
    public async Task HardwareErrorsRemainMappedToStableStates()
    {
        var fake = new FakeBridgePm3Device(read: _ => Task.FromException<string>(
            new BridgeHardwareException(BridgeHardwareError.NoChip, "internal details")));
        await using var host = await BridgeTestHost.CreateAsync(fake);
        host.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await host.PairAsync());
        var response = await host.Client.GetAsync("/api/v1/hardware/page0/block5");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        var error = await response.Content.ReadFromJsonAsync<BridgeErrorResponse>();
        Assert.That(error!.Code, Is.EqualTo("no_chip"));
    }

    [TestCase(typeof(IOException), "pm3_unavailable")]
    [TestCase(typeof(UnauthorizedAccessException), "pm3_unavailable")]
    [TestCase(typeof(InvalidOperationException), "pm3_unavailable")]
    [TestCase(typeof(ObjectDisposedException), "pm3_unavailable")]
    public async Task ExpectedSerialFailuresAreMappedWithoutExposingNativeDetails(Type exceptionType, string expectedCode)
    {
        var exception = (Exception)Activator.CreateInstance(exceptionType, "native detail")!;
        await using var host = await BridgeTestHost.CreateAsync(
            new FakeBridgePm3Device(read: _ => Task.FromException<string>(exception)));
        host.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await host.PairAsync());
        var response = await host.Client.GetAsync("/api/v1/hardware/page0/block5");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
        var error = await response.Content.ReadFromJsonAsync<BridgeErrorResponse>();
        Assert.That(error!.Code, Is.EqualTo(expectedCode));
        Assert.That(error.Message, Does.Not.Contain("native detail"));
    }

    private static class InterlockedExtensions
    {
        public static void Max(ref int location, int value)
        {
            int original;
            do
            {
                original = Volatile.Read(ref location);
                if (original >= value) return;
            } while (Interlocked.CompareExchange(ref location, value, original) != original);
        }
    }
}
