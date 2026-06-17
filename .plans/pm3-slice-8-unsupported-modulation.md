# Slice 8 — Unsupported Modulation + Chip Type Detection (S4.10 rescoped)

**Status:** ✅ Done (`pm3-integration`)  
**Depends on:** Slice 5; benefits from Slice 7 (logging)  
**Branch:** `pm3-integration`

## Goal

Do **not** implement broader demod. Instead:

1. Detect **real** non-ASK T55 configs (known block0 variants) → `Pm3UnsupportedModulationException` + `PM3_EXECUTOR=process`
2. Detect **non-T55 LF tags** (e.g. EM410x false positives on T55 ReadBl samples) → `Pm3UnsupportedChipTypeException`

## Background

Native detect only runs ASK/Manchester demod (`Pm3LfDemod.AskDemodExt`). Non-ASK T55 tags and alien LF chips (EM410x) both failed with generic "detect failed" or misleading FSK2 false positives.

## Approach

After successful LF acquisition, if ASK T55 detect fails:

1. Try **EM410x decode** on raw samples (`Pm3LfEm410x`) when demod preamble is present
2. Run modulation-agnostic T55 config scan
3. If non-ASK config with **known elevator block0 / modulation variant** → unsupported modulation
4. If non-ASK config with **unknown block0** (e.g. `0x600E5BFF` from EM410x on T55 path) → **non-T55 LF chip type**

**No auto-fallback** to process executor.

## Implementation

- [x] `Pm3UnsupportedModulationException` + `Pm3T55ModulationNames`
- [x] `Pm3UnsupportedChipTypeException` + `Pm3LfChipFamily` (`Em410x`, `NonT55Lf`)
- [x] `Pm3LfEm410x` decode (proxmark3 `Em410xDecode` port)
- [x] `Pm3BitUtils.TryFindPlausibleConfig` + `IsKnownConfigModulationVariant`
- [x] `Pm3T55ModulationScanner` + chip-type routing in `Pm3T55NativeService.Detect`
- [x] Offline fixtures:
  - `Fixtures/Native/em410x-samples.bin` — BigBuf from T55 ReadBl after tune (read-only capture)
  - `Fixtures/Native/em410x-samples.json` — pm3 reader ID `1400711C5D`, false-positive metadata
- [x] `Pm3UnsupportedModulationTests` + `Pm3UnsupportedChipTypeTests`
- [x] Hardware: `Pm3NativeUnsupportedChipTypeIntegrationTests`

## Validation

| Layer | Command | Expected |
|-------|---------|----------|
| Unit | `dotnet test --filter "FullyQualifiedName~Pm3UnsupportedChipTypeTests"` | pass (fixture-based, no device) |
| Unit | `dotnet test --filter "Category!=Integration&Category!=IntegrationParity"` | 104 pass |
| Hardware EM410x | `dotnet test --filter "Native_Em410xTag" -- NUnit.RunExplicitTests=true` | `Pm3UnsupportedChipTypeException` (non-T55 LF) |
| Hardware ASK T55 | `dotnet test --filter "Native_AskToken" -- NUnit.RunExplicitTests=true` | no chip-type/modulation false positive |

### Re-capture EM410x fixture (read-only)

```bash
dotnet run --project debug/NativeT55Probe -- --capture --fixture em410x-samples.bin --port /dev/cu.usbmodem1201
```

Uses LF tune + `CMD_LF_T55XX_READBL` BigBuf download only (no writes).

## Key files

- `Pm3UsbApi/Native/Demod/Pm3LfEm410x.cs`
- `Pm3UsbApi/Native/T55/Pm3LfChipTypeScanner.cs`
- `Pm3UsbApi/Native/T55/Pm3T55NativeService.cs`
- `Pm3UsbApi/Pm3Exception.cs`
- `Pm3UsbApi.Tests/Fixtures/Native/em410x-samples.*`
- `Pm3UsbApi.Tests/Native/Pm3UnsupportedChipTypeTests.cs`

## Done when

Offline fixture tests pass; EM410x tag on hardware yields chip-type error (not FSK2 modulation); real non-ASK T55 known configs still yield modulation + process fallback hint.
