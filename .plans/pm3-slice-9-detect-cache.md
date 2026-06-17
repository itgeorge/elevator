# Slice 9 — T55 Detect Cache (S4.9, test-first)

**Status:** ✅ Complete  
**Depends on:** Slice 5  
**Branch:** `pm3-integration`

## Goal

Skip redundant 4×2 detect search when session config is still valid. **TTL: 30 seconds max.**

## Existing scaffolding

- `Pm3Session._lastDetectTime` / `_detectCacheTtl` — stamped on detect but **not used** for skip logic
- `Pm3NativeExecutor._t55Config` — persists across commands in one executor instance
- `ExecuteT55Async` always chains `[T55DetectCommand, command]`

## Cache policy (implement as pure class first)

**Cache key:** `(executor kind, port, block0, downlink, inversion, modulation, clock, offset)`

**Use cache (skip prepended detect) when:**

- Prior successful detect in same session
- Within **30s TTL**
- No invalidating event since detect
- Follow-on command is read/write/dump (not another detect)

**Invalidate on:**

| Event | Reason |
|-------|--------|
| `StartLfTuneAsync` / tune in batch | RF field changed |
| Any T55 **write** | Tag state may change |
| Disconnect / new executor | Session boundary |
| TTL elapsed (>30s) | Tag may have moved |
| Read verify mismatch | Stale config |
| Explicit `InvalidateT55Cache()` | API escape hatch |

**Process executor:** cache at session layer still skips detect command in batch, but each process invocation is stateless — detect still runs inside proxmark3 unless we add process-side session file (out of scope). **Native executor** gets the real win.

## Test-first tasks

- [x] `Pm3T55DetectCache` policy class + `Pm3T55DetectCacheTests` (no hardware)
- [x] `Pm3SessionDetectCacheTests` with recording executor
- [x] Wire into `Pm3Session.ExecuteT55Async` — strip `T55DetectCommand` when cache valid (native only)
- [x] Native executor honors skipped detect via persisted `_t55Config`
- [x] `Pm3.InvalidateT55DetectCache()` escape hatch

## Key files

- New: `Pm3UsbApi/Session/Pm3T55DetectCache.cs`
- `Pm3UsbApi/Session/Pm3Session.cs`
- `Pm3UsbApi/Native/Pm3NativeExecutor.cs`
- `Pm3UsbApi.Tests/Session/Pm3T55DetectCacheTests.cs`

## Done when

Unit tests cover hit/miss/invalidation; native second read skips detect within 30s; S4.9 marked `[x]`.
