# Slice 10 — Dump Performance Tuning (S4.8 rescoped)

**Status:** 🔲 Not started  
**Depends on:** Slice 9 (detect cache) recommended first  
**Branch:** `pm3-integration`

## Goal

Improve native `DumpPage0` performance without `CMD_LF_T55XX_RESET_READ`.

## Important correction

`CMD_LF_T55XX_RESET_READ` (0x0216) is **`lf t55xx resetread`** — one reset + LF acquisition for stream analysis. Proxmark3 **`lf t55xx dump` uses 8× `READBL`**, same as our native path. RESET_READ is **not** a faster dump.

## Scope (Option A — agreed)

- [ ] Tune `Pm3T55NativeService.DumpPage0`:
  - Reduce inter-block RF settle when config already known (vs single-block read)
  - Avoid redundant `DiscardPendingInput` / buffer clears between blocks where safe
  - Reuse detected config without re-searching clock per block (already mostly true via `DecodeWithConfig`)
- [ ] Benchmark before/after:
  - Integration test or extend `Pm3NativeLoadTests` with dump timing log
  - Target: measurable reduction in dump duration on hardware
- [ ] **Do not** replace 8 reads with resetread
- [ ] Optional: expose `resetread` in `debug/NativeT55Probe` only (diagnostic, not dump)

## Key files

- `Pm3UsbApi/Native/T55/Pm3T55NativeService.cs` — `DumpPage0`, `ReadBlock`, settle delays
- `Pm3UsbApi/Native/Pm3NativeExecutor.cs` — `ExecuteT55Dump`
- `Pm3UsbApi.Tests/Integration/Pm3IntegrationTests.cs` — dump timing (native fixture)

## Done when

Dump integration test passes; documented timing improvement on macOS hardware; S4.8 marked `[x]` with corrected description in master plan.
