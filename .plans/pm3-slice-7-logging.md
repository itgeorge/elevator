# Slice 7 — Diagnostic Logging (temp files)

**Status:** ✅ Complete  
**Depends on:** Slice 5  
**Branch:** `pm3-integration`

## Goal

Always-on diagnostic logging to the system temp directory, including unhandled exceptions before process exit.

## Agreed design

**Base directory:**

```csharp
Path.Combine(Path.GetTempPath(), "elevator")
```

**Per-session files** (pid + UTC timestamp in filename to avoid collisions):

```
{temp}/elevator/pm3-{pid}-{yyyyMMddHHmmss}-session.log   # operations (always on)
{temp}/elevator/pm3-{pid}-{yyyyMMddHHmmss}-errors.log    # errors + unhandled exceptions (always on)
{temp}/elevator/pm3-{pid}-{yyyyMMddHHmmss}-native.log    # frame-level trace (opt-in)
```

**Env overrides:**

| Variable | Purpose |
|----------|---------|
| `PM3_LOG_DIR` | Override base dir (default: `{GetTempPath()}/elevator`) |
| `PM3_NATIVE_TRACE=1` | Enable native frame trace log |

**Log once at session start:** `PM3 log directory: ...` (session.log path).

## Tasks

- [ ] Add `Pm3DiagnosticLog` helper (thread-safe append, never throw from logging)
- [ ] Create log paths on first `Pm3` / `Pm3Session` connect
- [ ] Log: executor kind, port, command batches, durations, success/failure summaries
- [ ] Log errors: `Pm3Exception`, timeouts, detect failures, native NACKs
- [ ] **Unhandled exception hook:** `AppDomain.CurrentDomain.UnhandledException` + top-level catch in CLIs (`RidesCli`, `Pm3Cli`) writing to errors.log before exit
- [ ] Native executor: optional frame trace behind `PM3_NATIVE_TRACE`
- [ ] Unit tests: path construction, append, log failure is non-fatal
- [ ] Keep existing opt-in `EnableTranscriptLogging` (CLI transcript) — distinct from diagnostic logs

## Key files

- New: `Pm3UsbApi/Diagnostics/Pm3DiagnosticLog.cs`
- `Pm3UsbApi/Session/Pm3Session.cs`
- `Pm3UsbApi/Native/Pm3NativeExecutor.cs`
- `Pm3UsbApi/Native/Transport/Pm3SerialTransport.cs` (trace)
- `RidesCli/RidesCliProgram.cs`, `Pm3Cli/Pm3CliProgram.cs` (unhandled handler)

## Done when

Every PM3 session writes session + errors logs under temp; unhandled exceptions are captured; tests pass.
