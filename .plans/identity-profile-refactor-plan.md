# Identity Profile / Ride Sequence Split Plan

## How agents should use this plan

Read this entire file before making changes. Find the next `[ ]` TODO and work on it; if new relevant work is discovered, add TODOs under the current phase before continuing. Keep each chunk coherent and testable, mark completed items by changing `[ ]` to `[x]`, and document assumptions/deviations in the notes. Commit plan updates in the same commit as corresponding code/test changes so implementation and handoff state stays aligned.

Important repo coordination note: another agent may be working in this repo. Expect uncommitted/untracked files such as `debug/EncodeRideBlock/`, `debug/RideBlockGuessPrototype/`, and `debug/write-variant-profile.sh`. Do not delete, rewrite, format, stage, or commit unrelated files. Start every session with `git status --short --branch`, inspect relevant files before editing, and stage only files for this plan.

---

## What this work is

Refactor the model so a **ride encoding sequence** is distinct from a **token identity/reset profile**.

Recent hardware tests showed that identity blocks `1..4` can vary while the elevator still accepts/decrements known ride block encodings in blocks `5/6`:

- Venus-like variant `21FF0031-5BA494A3-D6D1C733-D6D1C733` accepted Venus ride encoding:
  - `128 -> 127`: `BBC7C940 -> 48C736BF`
  - `1 -> 0`: `48C74858 -> 48C74948`
- Earth-like variant `D3FE005D-A4578D3A-650432F5-650432F5` accepted Earth ride encoding:
  - `128 -> 127`: `EB129210 -> 18126DEF`
  - `1 -> 0`: `18121308 -> 18121218`

This means the current `EncodingSequence` shape is doing two jobs:

1. ride block encoding/decoding (`Mercury`, `Venus`, `Earth`, `Mars` families/ranges), and
2. canonical reset identity (`ResetImageFileName`).

We want a minimal split, not a large UX/product expansion.

---

## End goal of this plan

- `EncodingSequence` represents only ride block encoding ranges/families.
- A new identity profile abstraction represents fixed token identity blocks and optional reset image metadata.
- Existing canonical reset behavior still works for `mercury`, `venus`, `earth`, and `mars`.
- Validated variant identities can be recognized and mapped to their ride sequence without adding production reset images for them.
- Existing commands/tests remain backward-compatible where practical, especially existing `reset --sequence <name>` usage.
- `RideCaptureCli` can use the identity profile registry for known-token detection so validated variants do not need to remain `UNKNOWN_TOKEN` solely because they are not canonical reset identities.

---

## Key working assumptions

- Keep this minimal: do not add new production reset images for `venus21ff` or `earth-a457` unless the user explicitly asks later.
- Keep the current ride encoding algorithms unchanged.
- Keep `UNKNOWN_TOKEN` identity-based, but known variant identities should no longer be unknown once registered as profiles.
- `RidesCli reset` should reset only profiles with reset images. Canonical profiles are resettable; validated variants are recognition-only for now.
- Do not change block safety rules: reset writes may write blocks `1..6` through existing safe per-block reset path; normal set/add writes only blocks `5/6`; never write blocks `0` or `7`.
- Backwards compatibility is preferred: existing `reset --sequence venus` should continue to work, even if internally it resolves a resettable identity profile named `venus`.

---

# Phase 0 — Baseline inspection and coordination

## Todos

- [x] Run `git status --short --branch` and note untracked/uncommitted files from other work.
- [x] Read the relevant current files before editing:
  - `Tokens/EncodingSequence.cs`
  - `Tokens/TokenBlockUtils.cs`
  - `RidesCli/ResetPage0BlocksLoader.cs`
  - `RidesCli/RidesCommandHandler.cs`
  - `RideCaptureCli/SeededTokenCatalog.cs`
  - `RideCaptureCli/CaptureSequenceService.cs`
  - relevant tests in `Tokens.Tests`, `RidesCli.Tests`, `RideCaptureCli.Tests`
- [x] Confirm current tests pass or record the pre-existing failure before making changes:

  ```bash
  dotnet test --filter "Category!=Integration&Category!=IntegrationParity"
  ```

## Agent notes / assumptions

- Notes: Baseline non-integration suite passed (298 tests, 1 skipped integration test) before changes. Untracked unrelated files present: `debug/EncodeRideBlock/`, `debug/RideBlockGuessPrototype/.idea/`, `debug/write-variant-profile.sh`.
- Assumptions: No pre-existing failures to record.

---

# Phase 1 — Introduce identity profile model

## Todos

- [x] Add a small identity profile abstraction in the shared `Tokens` project so both `RidesCli` and `RideCaptureCli` can use it.

  Suggested starting shape, adjustable if implementation reveals a better fit:

  ```csharp
  public sealed record TokenIdentityProfile(
      string FriendlyName,
      EncodingSequence RideSequence,
      T55Block Block1,
      T55Block Block2,
      T55Block Block3,
      T55Block Block4,
      string? ResetImageFileName = null)
  {
      public string TokenId => $"{Block1}-{Block2}-{Block3}-{Block4}";
      public bool CanReset => !string.IsNullOrWhiteSpace(ResetImageFileName);
  }
  ```

- [x] Add a `TokenIdentityProfiles` registry with canonical resettable profiles:
  - `mercury` -> sequence `EncodingSequences.Mercury`, reset image `default-500-rides.bin`, identity `9BFE0062-5BA4A3DE-D5D1D713-D5D1D713`
  - `venus` -> sequence `EncodingSequences.Venus`, reset image `venus-0-rides.bin`, identity `43FE0062-5BA494A3-D6D1C733-D6D1C733`
  - `earth` -> sequence `EncodingSequences.Earth`, reset image `earth-0-rides.bin`, identity `D3FE005D-522BC69D-650432F5-650432F5`
  - `mars` -> sequence `EncodingSequences.Mars`, reset image `mars-0-rides.bin`, identity `C3FE0031-20C60722-B6D14924-B6D14924`
- [x] Add recognition-only validated variant profiles with no reset image:
  - `venus21ff` -> sequence `Venus`, identity `21FF0031-5BA494A3-D6D1C733-D6D1C733`
  - `earth-a457` -> sequence `Earth`, identity `D3FE005D-A4578D3A-650432F5-650432F5`
- [x] Provide lookup helpers:
  - `TokenIdentityProfiles.All`
  - `TokenIdentityProfiles.Resettable`
  - `TryGetByFriendlyName(string, out TokenIdentityProfile?)`
  - `TryGetByTokenId(string, out TokenIdentityProfile?)`
  - optional `FormatKnownFriendlyNames()` / `FormatResettableFriendlyNames()`

## Agent notes / assumptions

- Notes: Implemented in `Tokens/TokenIdentityProfile.cs`. `TokenId` uses `T55Block.ToHex()` formatting.
- Assumptions: Friendly names remain lowercase via `EncodingSequence` conventions for canonical profiles.

---

# Phase 2 — Make EncodingSequence ride-only

## Todos

- [x] Remove reset image ownership from `EncodingSequence` if feasible in this chunk:
  - remove constructor parameter `resetImageFileName`
  - remove `ResetImageFileName` property
  - keep `FriendlyName`, `MinRides`, `MaxRides`, `Segments`, `Encode`, and family lookup behavior
- [x] Move all reset image references to `TokenIdentityProfile` / `TokenIdentityProfiles`.
- [x] If full removal would cause too much churn, keep `EncodingSequence.ResetImageFileName` temporarily marked as compatibility/deprecated in comments, but still route new code through profiles. Prefer full removal if tests remain straightforward.
- [x] Ensure `EncodingSequences.All` remains the source of truth for ride encoding families, not identity profiles.

## Agent notes / assumptions

- Notes: Full removal completed without compatibility shim.
- Assumptions: Debug tools outside the solution were left untouched.

---

# Phase 3 — Update reset loading and RidesCli reset

## Todos

- [x] Change `ResetPage0BlocksLoader.Load(...)` to load by `TokenIdentityProfile` instead of `EncodingSequence`.
- [x] Keep the existing reset safety behavior in `RidesCli`:
  - read/snapshot blocks `1..6`
  - write changed blocks one at a time
  - verify each block
  - retry once after delay
  - rollback best-effort on failure
  - never write block `0` or `7`
- [x] Update `RidesCli reset` parsing minimally:
  - existing `reset --sequence <name>` continues to resolve canonical resettable profiles named `mercury`, `venus`, `earth`, `mars`
  - optionally add `reset --profile <name>` as clearer terminology
  - if a recognition-only profile such as `venus21ff` or `earth-a457` is requested, print a clear error that the profile has no reset image and is not resettable
- [x] Update help text to avoid implying that a ride sequence and reset profile are the same thing. Minimal wording is fine, e.g.:

  ```text
  reset --sequence <name>   Reset token using a resettable identity profile (known: mercury, venus, earth, mars)
  ```

- [x] Ensure `set` / `add` behavior still preserves the ride encoding sequence detected from blocks `5/6` and is not coupled to profile identity.

## Agent notes / assumptions

- Notes: `reset --profile` added as alias for `--sequence`. Reset prompt now references profile name.
- Assumptions: Canonical profile friendly names remain aligned with ride sequence names for compatibility.

---

# Phase 4 — Update RideCaptureCli known-token recognition

## Todos

- [x] Update `SeededTokenCatalog` so known token IDs are not only derived from historical seeded start states.
- [x] Use `TokenIdentityProfiles.TryGetByTokenId(...)` or equivalent to recognize canonical and validated variant identities.
- [x] Preserve decode-first capture-start behavior:
  - mirrored blocks `5/6` should be decoded with `TokenBlockUtils` first
  - seed fallback should remain state-specific and only apply to exact historical token id + block5 + block6 matches
- [x] Preserve identity-based warning semantics:
  - known canonical and variant profiles should not produce `UNKNOWN_TOKEN`
  - decodable ride blocks for an unregistered identity may establish counts, but should still warn `UNKNOWN_TOKEN`
- [x] Do not change historical seed ride counts.

## Agent notes / assumptions

- Notes: `SeededTokenCatalog.IsKnownTokenId` now checks profiles first, then historical seeds.
- Assumptions: `CaptureSequenceService` warning logic unchanged beyond catalog lookup.

---

# Phase 5 — Tests

## Todos

- [x] Add/update `Tokens.Tests` for the new profile registry:
  - canonical profiles map to expected ride sequences and token IDs
  - canonical profiles have reset image file names
  - variant profiles map to expected ride sequences and token IDs
  - variant profiles have no reset image / `CanReset == false`
  - `EncodingSequence` no longer owns reset image metadata, if removed
- [x] Update `RidesCli.Tests`:
  - `reset --sequence venus` still writes canonical Venus identity blocks and zero ride encoding
  - `reset --sequence earth` still writes canonical Earth identity blocks and zero ride encoding
  - `reset --sequence mars` still writes canonical Mars identity blocks and zero ride encoding
  - recognition-only profile reset attempt fails clearly without writing, if profile names are accepted by parser
  - existing set/add sequence-preservation tests still pass
- [x] Update `RideCaptureCli.Tests`:
  - variant Venus identity `21FF0031-5BA494A3-D6D1C733-D6D1C733` with Venus ride blocks is recognized as known identity and decodes ride count
  - variant Earth identity `D3FE005D-A4578D3A-650432F5-650432F5` with Earth ride blocks is recognized as known identity and decodes ride count
  - a decodable but unregistered identity still gets `UNKNOWN_TOKEN`
  - historical seed exact-state fallback still works
- [x] Update any reset image existence tests to iterate resettable profiles, not all ride sequences.

## Agent notes / assumptions

- Notes: Added `Tokens.Tests/TokenIdentityProfilesTests.cs`, new RidesCli and RideCaptureCli tests.
- Assumptions: Existing tests cover unregistered decodable identity and seed fallback behavior.

---

# Phase 6 — Documentation and validation

## Todos

- [x] Update relevant README/docs with minimal wording only:
  - `RidesCli` reset uses resettable identity profiles associated with ride sequences
  - `RideCaptureCli` known identity recognition includes validated identity variants
  - no production reset images exist for `venus21ff` / `earth-a457` yet
- [x] Run targeted tests first:

  ```bash
  dotnet test Tokens.Tests
  dotnet test RidesCli.Tests
  dotnet test RideCaptureCli.Tests
  ```

- [x] Run the non-integration suite:

  ```bash
  dotnet test --filter "Category!=Integration&Category!=IntegrationParity"
  ```

- [ ] Optional hardware smoke test only if user wants it and a token is available:
  - read-only preflight with `Pm3Cli`
  - `RidesCli reset --sequence venus` on a sacrificial token still writes canonical Venus safely
  - `RidesCli read` after reset decodes correctly
- [x] Before committing, re-run `git status --short --branch` and confirm only files for this plan are staged.
- [x] Commit plan updates with code/test changes.

## Agent notes / assumptions

- Notes: Updated `RideCaptureCli/README.md`. RidesCli help text updated in code. Hardware smoke test skipped (no token requested).
- Assumptions: Help text in `RidesCommandHandler` is sufficient RidesCli documentation for this change.

---

# Open questions / follow-up decisions

- [ ] Decide later whether to expose production reset profiles for `venus21ff` and `earth-a457`. Current assumption: no reset image / no reset command support for variants.
- [ ] Decide later whether `reset --sequence` should be renamed to `reset --profile` in user-facing CLI. Current assumption: keep `--sequence` for compatibility and optionally add `--profile` as an alias.
- [ ] Decide later whether token identity profiles belong permanently in `Tokens` or should move to a separate shared project if more apps need richer metadata.
