using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using RidesBridge;

namespace RidesBridge.Tests;

internal sealed class FakeBridgePm3Device : IBridgePm3Device
{
    private readonly Func<CancellationToken, Task<string>> _read;
    public int ReadCalls;
    public int StartCalls;
    public Exception? StartException { get; init; }
    public bool Disposed { get; private set; }

    public FakeBridgePm3Device(string value = "A1B2C3D4", Func<CancellationToken, Task<string>>? read = null)
    {
        Value = value;
        _read = read ?? (_ => Task.FromResult(Value));
    }

    public string Value { get; set; }

    public Task StartAsync(CancellationToken ct = default)
    {
        StartCalls++;
        ct.ThrowIfCancellationRequested();
        if (StartException is not null)
            return Task.FromException(StartException);
        return Task.CompletedTask;
    }

    public async Task<string> ReadPage0Block5Async(CancellationToken ct = default)
    {
        Interlocked.Increment(ref ReadCalls);
        return await _read(ct);
    }

    public Task<string> ReadPage0Block6Async(CancellationToken ct = default)
    {
        Interlocked.Increment(ref ReadCalls);
        return Task.FromResult(Value);
    }

    public Task<(string Block5Hex, string Block6Hex)> ReadMercuryMirrorAsync(CancellationToken ct = default) =>
        Task.FromResult((Value, Value));

    public Task WritePage0Block5Async(string value, CancellationToken ct = default)
    {
        Value = value;
        return Task.CompletedTask;
    }

    public Task WritePage0Block6Async(string value, CancellationToken ct = default)
    {
        Value = value;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}

internal sealed class FakeBridgePm3Session : IBridgePm3Session
{
    private readonly List<string> _calls;
    private readonly Func<CancellationToken, Task<string>> _read;
    private readonly Exception? _disposeException;

    public bool Disposed { get; private set; }

    public FakeBridgePm3Session(
        List<string> calls,
        Func<CancellationToken, Task<string>>? read = null,
        Exception? disposeException = null)
    {
        _calls = calls;
        _read = read ?? (_ => Task.FromResult("A1B2C3D4"));
        _disposeException = disposeException;
    }

    public Task<bool> IsConnectedAsync(CancellationToken ct = default)
    {
        _calls.Add("connected");
        return Task.FromResult(true);
    }

    public Task ConnectAsync(CancellationToken ct = default)
    {
        _calls.Add("connect");
        return Task.CompletedTask;
    }

    public void InvalidateT55DetectCache() => _calls.Add("invalidate");

    public Task EnsureT55SessionActiveAsync(CancellationToken ct = default)
    {
        _calls.Add("ensure");
        return Task.CompletedTask;
    }

    public Task<string> ReadPage0BlockAsync(uint block, CancellationToken ct = default)
    {
        _calls.Add(block == 5 ? "read" : $"read{block}");
        return _read(ct);
    }

    public Task WritePage0BlockAsync(uint block, Tokens.T55Block data, CancellationToken ct = default)
    {
        _calls.Add($"write{block}:{data.ToHex()}");
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _calls.Add("dispose");
        Disposed = true;
        if (_disposeException is not null)
            throw _disposeException;
        return ValueTask.CompletedTask;
    }
}

internal sealed class TestClock : TimeProvider
{
    private DateTimeOffset _now;
    public TestClock(DateTimeOffset now) => _now = now;
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan amount) => _now += amount;
}

internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    public List<string> Messages { get; } = [];
    public ILogger CreateLogger(string categoryName) => new CapturingLogger(Messages);
    public void Dispose() { }

    private sealed class CapturingLogger(List<string> messages) : ILogger
    {
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => messages.Add(formatter(state, exception));

        private sealed class NullScope : IDisposable
        {
            public static NullScope Instance { get; } = new();
            public void Dispose() { }
        }
    }
}

internal sealed class BridgeTestHost : IAsyncDisposable
{
    public WebApplication App { get; }
    public HttpClient Client { get; }
    public FakeBridgePm3Device Device { get; }

    private BridgeTestHost(WebApplication app, FakeBridgePm3Device device)
    {
        App = app;
        Client = app.GetTestClient();
        Device = device;
    }

    public static async Task<BridgeTestHost> CreateAsync(
        FakeBridgePm3Device? device = null,
        string? path = null,
        ILoggerProvider? loggerProvider = null,
        BridgeOptions? options = null)
    {
        device ??= new FakeBridgePm3Device();
        path ??= Path.Combine(Path.GetTempPath(), "ridesbridge-tests", Guid.NewGuid().ToString("N"), "paired.json");
        options ??= new BridgeOptions { BindUrl = "http://127.0.0.1:5080", PairedClientsPath = path };
        if (options.PairedClientsPath is null)
            options = options with { PairedClientsPath = path };
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(BridgeApplication).Assembly.GetName().Name,
            EnvironmentName = "Testing",
        });
        builder.WebHost.UseTestServer();
        if (loggerProvider is not null)
            builder.Logging.AddProvider(loggerProvider);
        builder.Services.AddRidesBridge(options, device);
        var app = builder.Build();
        app.MapRidesBridge();
        await app.StartAsync();
        return new BridgeTestHost(app, device);
    }

    public Task<string> IssuePinAsync()
    {
        var service = App.Services.GetRequiredService<PairingCodeService>();
        return Task.FromResult(service.IssueCode().Value);
    }

    public async Task<string> PairAsync()
    {
        var response = await Client.PostAsJsonAsync("/api/v1/pair", new PairRequest(await IssuePinAsync()));
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<PairResponse>();
        Assert.That(body, Is.Not.Null);
        return body!.AccessToken;
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await App.DisposeAsync();
    }
}
