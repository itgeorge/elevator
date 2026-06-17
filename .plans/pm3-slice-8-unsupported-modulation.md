# Slice 8 — Unsupported Modulation Detection (S4.10 rescoped)

**Status:** 🔲 Not started  
**Depends on:** Slice 5; benefits from Slice 7 (logging)  
**Branch:** `pm3-integration`

## Goal

Do **not** implement broader demod. Instead, detect when a tag's config block indicates a non-ASK modulation and return a clear error directing the user to `PM3_EXECUTOR=process`.

## Background

Native detect only runs ASK/Manchester demod (`Pm3LfDemod.AskDemodExt`). Non-ASK tags fail all 4×2 attempts with generic "detect failed" — indistinguishable from no tag.

## Approach

1. After successful LF acquisition (samples present), if ASK detect fails across all modes:
2. Run **modulation-agnostic config scan** — parse candidate block0 offsets, read `modRead` without `TestModulation(ASK-only)` filter.
3. If plausible T55 config found with `modRead != 0x08` (ASK):
   - Throw `Pm3UnsupportedModulationException` (new type in `Pm3Exception` hierarchy)
   - Message: native supports ASK only; set `PM3_EXECUTOR=process` and ensure proxmark3 client installed.
4. If no plausible config → keep existing detect-failed behavior.

**No auto-fallback** to process executor (requires pm3 install; blurs executor boundary).

## Test-first

- [ ] `Pm3UnsupportedModulationTests` (offline, no device):
  - ASK fixture → normal detect path (or existing offline tests)
  - Synthetic demod buffer with PSK `modRead` in config → `Pm3UnsupportedModulationException`
  - Garbage/no config → generic detect failure (not unsupported)
- [ ] Implement scan in `Pm3T55NativeService` or new `Pm3T55ConfigScanner`
- [ ] Wire through `Pm3NativeExecutor` → `Pm3NativeOutputBuilder.BuildDetectFailedLines` distinction if needed
- [ ] Document in master plan + RidesCli help text

## Key files

- `Pm3UsbApi/Native/T55/Pm3T55NativeService.cs`
- `Pm3UsbApi/Native/Demod/Pm3BitUtils.cs` — add `TryFindConfigOffsetAnyModulation` or similar
- `Pm3UsbApi/Pm3Exception.cs`
- `Pm3UsbApi.Tests/Native/Pm3UnsupportedModulationTests.cs`

## Done when

Offline tests pass; native detect on non-ASK config yields actionable error; S4.10 marked done (demod expansion explicitly skipped).
