# Slice 10 — Dump Performance Tuning (S4.8 rescoped)

**Status:** ✅ Complete (scoped)  
**Depends on:** Slice 9 (detect cache)  
**Branch:** `pm3-integration`

## Goal

Improve native `DumpPage0` performance without `CMD_LF_T55XX_RESET_READ`.

## Important correction

`CMD_LF_T55XX_RESET_READ` (0x0216) is **`lf t55xx resetread`** — one reset + LF acquisition for stream analysis. Proxmark3 **`lf t55xx dump` uses 8× `READBL`**, same as our native path. RESET_READ is **not** a faster dump.

## Scope (agreed — items 2 & 3 only)

RF settle times unchanged (150 ms).

- [x] Tune `Pm3T55NativeService.DumpPage0`:
  - **One** `DiscardPendingInput` at dump start; skip per-block serial flushes when stream is already synced
  - Retry path still flushes before re-acquire
  - `AcquireData` accepts `discardPendingInput` (default true for detect/single read)
- [x] Reuse detected config on dump path:
  - `TryReadBlockWithKnownConfig` — single acquire + `DecodeWithConfig` per block (no 3× majority voting)
  - One retry with RF settle + flush on failure only
- [ ] ~~Reduce inter-block RF settle~~ — **skipped** (user preference)
- [ ] ~~Benchmark timing log~~ — deferred; verify via existing dump integration tests
- [x] **Do not** replace 8 reads with resetread

## Key files

- `Pm3UsbApi/Native/T55/Pm3T55NativeService.cs` — `DumpPage0`, `TryReadBlockWithKnownConfig`, `AcquireData`
- `Pm3UsbApi/Native/Pm3NativeExecutor.cs` — `ExecuteT55Dump` (unchanged)

## Done when

Dump integration tests pass on hardware; S4.8 marked `[x]` with scoped description in master plan.
