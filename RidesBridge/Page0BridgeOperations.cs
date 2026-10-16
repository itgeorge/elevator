using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Logging;

namespace RidesBridge;

internal sealed record ValidatedPage0Mutation(int Block, string Expected, string Desired);

internal static class Page0MutationValidator
{
    public static bool TryValidate(
        Page0MutationRequest? request,
        out IReadOnlyList<ValidatedPage0Mutation> mutations,
        out BridgeErrorResponse? error)
    {
        mutations = [];
        error = null;

        if (request is null || !string.Equals(request.Version, BridgeOptions.ApiVersion, StringComparison.Ordinal))
        {
            error = new BridgeErrorResponse("unsupported_api_version", "The mutation request version is not supported.");
            return false;
        }

        if (request.Mutations is null || request.Mutations.Count is < 1 or > 6)
        {
            error = new BridgeErrorResponse("invalid_mutation_request", "One to six page-0 mutations are required.");
            return false;
        }

        var seen = new HashSet<int>();
        var valid = new List<ValidatedPage0Mutation>(request.Mutations.Count);
        foreach (var mutation in request.Mutations)
        {
            if (mutation is null)
            {
                error = new BridgeErrorResponse("invalid_mutation_request", "Mutation entries are required.");
                return false;
            }

            if (mutation.Block < 1 || mutation.Block > 6)
            {
                error = new BridgeErrorResponse("invalid_mutation_block", "Only page-0 blocks 1 through 6 are valid mutation targets.");
                return false;
            }

            if (!seen.Add(mutation.Block))
            {
                error = new BridgeErrorResponse("duplicate_mutation_block", "Each mutation block must be distinct.");
                return false;
            }

            if (!TryNormalizeBlockHex(mutation.Expected, out var expected)
                || !TryNormalizeBlockHex(mutation.Desired, out var desired))
            {
                error = new BridgeErrorResponse("invalid_block_hex", "Expected and desired values must be exactly eight hexadecimal digits.");
                return false;
            }

            valid.Add(new ValidatedPage0Mutation(mutation.Block, expected, desired));
        }

        mutations = valid;
        return true;
    }

    internal static bool TryNormalizeBlockHex(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (value is null || value.Length != 8)
            return false;
        if (!value.All(Uri.IsHexDigit))
            return false;

        // Parse as a 32-bit value as well as checking the shape. This makes the contract
        // explicit and avoids accepting a future wider numeric representation.
        if (!uint.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var parsed))
            return false;
        normalized = parsed.ToString("X8", CultureInfo.InvariantCulture);
        return true;
    }
}

/// <summary>
/// Applies the narrow, page-0 conditional mutation contract. It deliberately has no
/// generic block-number or command route: the only legal targets are page-0 blocks 1 through 6.
/// </summary>
public sealed class Page0ConditionalWriter
{
    private readonly IBridgePm3Device _device;
    private readonly TimeSpan _recoveryTimeout;
    private readonly ILogger<Page0ConditionalWriter>? _logger;

    public Page0ConditionalWriter(
        IBridgePm3Device device,
        TimeSpan? recoveryTimeout = null,
        ILogger<Page0ConditionalWriter>? logger = null)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _recoveryTimeout = recoveryTimeout ?? TimeSpan.FromSeconds(5);
        _logger = logger;
        if (_recoveryTimeout <= TimeSpan.Zero || _recoveryTimeout == Timeout.InfiniteTimeSpan)
            throw new ArgumentOutOfRangeException(nameof(recoveryTimeout), "Recovery timeout must be finite and greater than zero.");
    }

    internal TimeSpan RecoveryTimeoutForTesting => _recoveryTimeout;

    public async Task<Page0MutationResponse> ExecuteAsync(
        Page0MutationRequest request,
        CancellationToken ct = default)
    {
        if (!Page0MutationValidator.TryValidate(request, out var validated, out var error))
            throw new Page0MutationValidationException(error!);

        var total = Stopwatch.StartNew();
        var outcome = "failed";
        var changed = new List<MutationState>();
        try
        {
            var openTimer = Stopwatch.StartNew();
            var session = _device is IPage0MutationSessionProvider provider
                ? await provider.OpenMutationSessionAsync(ct).ConfigureAwait(false)
                : new ReadbackPage0MutationSession(_device);
            openTimer.Stop();
            _logger?.LogInformation("Page-0 mutation session opened in {ElapsedMs}ms; optimized={Optimized}",
                openTimer.Elapsed.TotalMilliseconds, _device is IPage0MutationSessionProvider);
            await using (session.ConfigureAwait(false))
            {
                var ordered = validated.OrderBy(m => m.Block).ToArray();
                var states = ordered.Select(m => new MutationState(m)).ToArray();
                var preflightTimer = Stopwatch.StartNew();
                // Read every target exactly once before deciding whether a write is allowed.
                foreach (var state in states)
                    state.Actual = await ReadAsync(session, state.Mutation.Block, ct).ConfigureAwait(false);
                preflightTimer.Stop();
                _logger?.LogInformation("Page-0 preflight completed in {ElapsedMs}ms for {TargetCount} targets",
                    preflightTimer.Elapsed.TotalMilliseconds, states.Length);

                if (states.Any(s => s.Actual != s.Mutation.Expected && s.Actual != s.Mutation.Desired))
                {
                    foreach (var state in states) state.Status = "conflict";
                    outcome = "conflict";
                    return CreateResponse("conflict", states, "notNeeded", []);
                }

                foreach (var state in states.Where(s => s.Actual == s.Mutation.Desired))
                    state.Status = "alreadyApplied";
                if (states.All(s => s.Actual == s.Mutation.Desired))
                {
                    outcome = "alreadyApplied";
                    return CreateResponse("alreadyApplied", states, "notNeeded", []);
                }

                var mutationStarted = false;
                var writeTimer = Stopwatch.StartNew();
                foreach (var state in states.Where(s => s.Actual == s.Mutation.Expected))
                {
                    mutationStarted = true;
                    try
                    {
                        // Include the target before issuing the command: it may apply even if
                        // its response is lost, and recovery can then safely restore expected.
                        changed.Add(state);
                        state.Actual = null;
                        var actual = await session.WriteVerifiedBlockAsync(
                            state.Mutation.Block, state.Mutation.Desired, ct).ConfigureAwait(false);
                        state.Actual = NormalizeActual(actual);
                        if (state.Actual != state.Mutation.Desired)
                            throw new VerificationFailureException();
                        state.Status = "written";
                    }
                    catch (OperationCanceledException) when (mutationStarted)
                    {
                        state.Status = "verifyFailed";
                        writeTimer.Stop();
                        _logger?.LogInformation("Page-0 write/verify stage ended in {ElapsedMs}ms for {WriteCount} writes",
                            writeTimer.Elapsed.TotalMilliseconds, changed.Count);
                        var failed = await FailureAsync(session, states, changed).ConfigureAwait(false);
                        outcome = failed.Status;
                        return failed;
                    }
                    catch (Exception ex) when (IsDeviceFailure(ex))
                    {
                        state.Status = "verifyFailed";
                        writeTimer.Stop();
                        _logger?.LogInformation("Page-0 write/verify stage ended in {ElapsedMs}ms for {WriteCount} writes",
                            writeTimer.Elapsed.TotalMilliseconds, changed.Count);
                        var failed = await FailureAsync(session, states, changed).ConfigureAwait(false);
                        outcome = failed.Status;
                        return failed;
                    }
                }

                writeTimer.Stop();
                _logger?.LogInformation("Page-0 write/verify stage completed in {ElapsedMs}ms for {WriteCount} writes",
                    writeTimer.Elapsed.TotalMilliseconds, changed.Count);
                outcome = "written";
                return CreateResponse("written", states, "notNeeded", []);
            }
        }
        finally
        {
            total.Stop();
            _logger?.LogInformation("Page-0 mutation completed with outcome {Outcome} in {ElapsedMs}ms; targetCount={TargetCount} writeCount={WriteCount}",
                outcome, total.Elapsed.TotalMilliseconds, validated.Count, changed.Count);
        }
    }

    private async Task<Page0MutationResponse> FailureAsync(
        IPage0MutationSession session,
        IReadOnlyList<MutationState> states,
        IReadOnlyList<MutationState> changed)
    {
        // Never pass the request/operation cancellation token to recovery. A disconnected
        // client, or the server's operation deadline, must not abandon verification/rollback.
        // Recovery nevertheless has its own finite server-owned budget.
        using var recoveryCts = new CancellationTokenSource(_recoveryTimeout);
        var recoveryCt = recoveryCts.Token;
        var recoveryTimer = Stopwatch.StartNew();
        var rollback = new List<Page0RollbackResult>(changed.Count);
        foreach (var state in changed.Reverse())
        {
            string? actual = null;
            var succeeded = false;
            state.Actual = null;
            try
            {
                actual = NormalizeActual(await session.WriteVerifiedBlockAsync(
                    state.Mutation.Block, state.Mutation.Expected, recoveryCt).ConfigureAwait(false));
                state.Actual = actual;
                succeeded = actual == state.Mutation.Expected;
            }
            catch
            {
                // Recovery is best effort. The response contains only stable outcome data, never
                // native exception text or command output.
            }

            rollback.Add(new Page0RollbackResult(
                state.Mutation.Block,
                state.Mutation.Expected,
                actual,
                succeeded));
        }

        foreach (var state in states.Where(s => s.Status == "pending"))
            state.Status = "notAttempted";

        recoveryTimer.Stop();
        _logger?.LogInformation("Page-0 recovery completed in {ElapsedMs}ms for {RecoveryCount} recovery writes",
            recoveryTimer.Elapsed.TotalMilliseconds, rollback.Count);
        var rollbackStatus = changed.Count == 0
            ? "notNeeded"
            : rollback.All(r => r.Succeeded) ? "rollbackSucceeded" : "rollbackIncomplete";
        return CreateResponse("verifyFailed", states, rollbackStatus, rollback);
    }

    private static async Task<string> ReadAsync(IPage0MutationSession session, int block, CancellationToken ct)
    {
        var value = await session.ReadBlockAsync(block, ct).ConfigureAwait(false);
        if (!Page0MutationValidator.TryNormalizeBlockHex(value, out var normalized))
            throw new BridgeHardwareException(BridgeHardwareError.MalformedResponse, "PM3 returned a malformed block response.");
        return normalized;
    }

    private static string NormalizeActual(string? value)
    {
        if (!Page0MutationValidator.TryNormalizeBlockHex(value, out var normalized))
            throw new BridgeHardwareException(BridgeHardwareError.MalformedResponse, "PM3 returned a malformed block response.");
        return normalized;
    }

    private static bool IsDeviceFailure(Exception ex) => ex is BridgeHardwareException
        or IOException
        or UnauthorizedAccessException
        or ObjectDisposedException
        or InvalidOperationException
        or TimeoutException
        or FormatException
        or VerificationFailureException;

    private static Page0MutationResponse CreateResponse(
        string status,
        IEnumerable<MutationState> states,
        string rollbackStatus,
        IReadOnlyList<Page0RollbackResult> rollback)
    {
        var results = states.Select(s => new Page0MutationBlockResult(
            s.Mutation.Block,
            s.Status,
            s.Mutation.Expected,
            s.Mutation.Desired,
            s.Actual)).ToArray();
        return new Page0MutationResponse(BridgeOptions.ApiVersion, status, results, rollbackStatus, rollback);
    }

    private sealed class MutationState(ValidatedPage0Mutation mutation)
    {
        public ValidatedPage0Mutation Mutation { get; } = mutation;
        public string? Actual { get; set; }
        public string Status { get; set; } = "pending";
    }

    private sealed class VerificationFailureException : Exception;
}

public sealed class Page0MutationValidationException : Exception
{
    public BridgeErrorResponse Error { get; }

    internal Page0MutationValidationException(BridgeErrorResponse error)
        : base(error.Message) => Error = error;
}
