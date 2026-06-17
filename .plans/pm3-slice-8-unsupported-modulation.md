# Slice 8 — Unsupported Modulation Detection (S4.10 rescoped)

**Status:** ✅ Done (`pm3-integration`)  
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

## Implementation

- [x] `Pm3UnsupportedModulationException` + `Pm3T55ModulationNames`
- [x] `Pm3BitUtils.TryFindPlausibleConfig` (modulation-agnostic)
- [x] `Pm3T55ModulationScanner` + scan hook in `Pm3T55NativeService.Detect`
- [x] `Pm3NativeExecutor` throws `Pm3UnsupportedModulationException` with output lines
- [x] `Pm3UnsupportedModulationTests` (offline fixture + PSK mutation + garbage)
- [x] `Pm3NativeUnsupportedModulationIntegrationTests` — ASK tag must **not** false-positive

## Validation

| Layer | Command | Expected |
|-------|---------|----------|
| Unit (offline) | `dotnet test --filter "FullyQualifiedName~Pm3UnsupportedModulationTests"` | 5 pass |
| Unit (all non-integration) | `dotnet test --filter "Category!=Integration&Category!=IntegrationParity"` | 99 pass |
| Hardware (negative) | `dotnet test --filter "FullyQualifiedName~Pm3NativeUnsupportedModulationIntegrationTests" -- NUnit.RunExplicitTests=true` | ASK token read succeeds, no `Pm3UnsupportedModulationException` |

Positive hardware validation (real PSK/FSK tag → unsupported error) deferred — no non-ASK tag in test kit.

## Key files

- `Pm3UsbApi/Pm3Exception.cs` — `Pm3UnsupportedModulationException`
- `Pm3UsbApi/Pm3T55ModulationNames.cs`
- `Pm3UsbApi/Native/Demod/Pm3BitUtils.cs` — `TryFindPlausibleConfig`
- `Pm3UsbApi/Native/T55/Pm3T55ModulationScanner.cs`
- `Pm3UsbApi/Native/T55/Pm3T55NativeService.cs`
- `Pm3UsbApi/Native/Pm3NativeExecutor.cs`
- `Pm3UsbApi.Tests/Native/Pm3UnsupportedModulationTests.cs`
- `Pm3UsbApi.Tests/Integration/Pm3NativeUnsupportedModulationIntegrationTests.cs`

## Done when

Offline tests pass; native detect on non-ASK config yields actionable error; S4.10 marked done (demod expansion explicitly skipped).
