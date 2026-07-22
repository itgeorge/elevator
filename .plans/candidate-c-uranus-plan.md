# Candidate C / Uranus Registration Plan

## How agents should use this plan

Read this entire file before making changes. Start each session with `git status --short --branch` and inspect the current versions of all files relevant to the next task. Find the next `[ ]` TODO and work on it; if new relevant work is discovered, add TODOs under the current phase before continuing. Keep working until the current TODO, or a coherent group of TODOs that forms a testable chunk, is complete. Mark completed items by changing `[ ]` to `[x]`, and document assumptions, deviations, hardware results, and design decisions in this file.

Commit completed plan updates in the same commit as the corresponding code/tests so this handoff remains aligned with implementation. Stage only files belonging to this work. Do not delete, rewrite, format, stage, or commit unrelated untracked files.

Important working-tree note at plan creation: the unrelated untracked paths visible in this checkout were:

```text
debug/EncodeRideBlock/
debug/RideBlockGuessPrototype/.idea/
debug/write-variant-profile.sh
```

Do not touch those paths unless the user explicitly changes scope. Another agent may already have implemented Saturn from `.plans/candidate-b-saturn-plan.md`; start from the latest branch/commit the user wants reviewed, and adapt this plan to the current code state before editing.

---

## What this work is

Register hardware-validated Candidate C as the production ride sequence **Uranus**.

Candidate C uses the already-implemented generalized ride counter codec:

```text
friendlyName = uranus
zeroBlock    = 891249D0
rotation     = 0
range        = 0..500
```

This is not a codec refactor. Jupiter proved production/elevator support for rotation 0, and Saturn/Candidate B should be implemented in parallel or already completed. This plan is sequence-specific registration, tests, documentation, and reset-profile work for Candidate C/Uranus.

---

## End goal of this plan

- `EncodingSequences.Uranus` is registered for `0..500` using `zeroBlock=891249D0`, `rotation=0`.
- Uranus read/set/add flows work through `RidesCli`, preserving Uranus after writes.
- Uranus blocks decode structurally through the registered-sequence registry.
- Uranus identity recognition is added using the canonical Candidate-C identity:

  ```text
  FBFE002A-F1003C92-F5D1D766-F5D1D766
  ```

- Uranus reset support is implemented using the confirmed zero/reset image, then hardware-smoke-tested before final handoff.
- Documentation and exploration/oracle scripts reflect Uranus as production.
- Saturn remains production if already implemented; do not regress or rename Saturn.
- All targeted and non-integration tests pass.

---

## Key working assumptions and non-goals

- Do not change the generalized codec unless a failing test proves the existing implementation is wrong.
- Do not infer arbitrary rotation-0 blocks. Production decode must continue to require a registered exact structural match.
- Do not rename or alter Jupiter or Saturn semantics while adding Uranus.
- Uranus `1 -> 0` is confirmed, so the reset image is known; still smoke-test `reset --profile uranus` on a sacrificial token before declaring reset support complete.
- Normal set/add writes only blocks 5/6. Reset writes only blocks 1..6 through the existing safe verified path. Never write blocks 0 or 7.
- Identity blocks 1..4 are metadata/reset profile identity, not ride-encoding inputs. Uranus ride blocks were validated on EBFE/Jupiter identity tokens, which is acceptable evidence for the ride sequence.
- The app-supported range remains `0..500` even though the counter representation supports `0..511`.
- If any newly discovered hardware result differs from expected, stop and update this plan/model before registering or enabling reset.

---

## Hardware evidence for Uranus / Candidate C

### Pre-existing evidence

```text
107 7A1222BB -> 106 7A1223BA  historical Candidate-C anchor/post-ride
```

### Newly validated transition tests, 2026-07-22

These were tested by writing only page-0 blocks 5/6 on sacrificial card/fob tokens whose identity blocks were EBFE/Jupiter. Blocks 5 and 6 matched in each confirmed post-ride read.

```text
128 8912C950 -> 127 7A1236AF  confirmed on fob
256 891349D1 -> 255 7A12B62F  confirmed on card
384 8913C951 -> 383 7A1336AE  confirmed on fob, one ride
8   7A1241D8 -> 7   89124ED7  confirmed on card, one ride
1   891248D1 -> 0   891249D0  confirmed on fob
```

The final zero transition confirms the Uranus zero block and makes the Uranus reset image known:

```text
block0 00148040
block1 FBFE002A
block2 F1003C92
block3 F5D1D766
block4 F5D1D766
block5 891249D0
block6 891249D0
block7 00000000
```

The implementing agent may create/register `RidesCli/Data/uranus-0-rides.bin`, but must still run the normal reset safety tests and perform a hardware reset smoke test on a user-confirmed sacrificial token before declaring reset support complete.

---

# Phase 0 — Baseline inspection and evidence preservation

## Todos

- [x] Run `git status --short --branch` and record any changed/untracked paths in Agent notes before editing.
- [x] Confirm whether Saturn has already been implemented in the current branch. If yes, preserve it and add Uranus alongside it. If no, coordinate with the Saturn implementation branch/agent before editing shared registries/tests/docs.
- [x] Read the current versions of these files before editing:
  - `Tokens/EncodingSequence.cs`
  - `Tokens/TokenBlockUtils.cs`
  - `Tokens/TokenIdentityProfile.cs`
  - `Tokens.Tests/TokenBlockUtilsTest.cs`
  - `Tokens.Tests/TokenIdentityProfilesTests.cs`
  - `RidesCli/RideBlockResolver.cs`
  - `RidesCli/RidesCommandHandler.cs`
  - `RidesCli/RidesCli.csproj`
  - `RidesCli.Tests/RideBlockResolverTests.cs`
  - `RidesCli.Tests/RidesCommandHandlerTests.cs`
  - `RidesCli.Tests/ResetPage0BlocksLoaderTests.cs`
  - `RideCaptureCli/CaptureSequenceService.cs`
  - `RideCaptureCli.Tests/CaptureSequenceServiceTests.cs`
  - `.docs/ride-encoding-algorithm-hypothesis-2026-07-21.md`
  - `.docs/ride-encoding-exploration-2026-07-21.md`
  - `debug/ride-encoding-hypothesis.py`
- [x] Preserve the confirmed Uranus hardware observations above in tests and documentation before changing any old Candidate-C unknown assertions.
- [x] Record baseline test status before editing, or note why baseline is intentionally skipped:

  ```bash
  dotnet test Tokens.Tests --no-restore
  dotnet test RidesCli.Tests --no-restore
  dotnet test RideCaptureCli.Tests --no-restore
  python3 debug/ride-encoding-hypothesis.py
  ```

## Agent notes / assumptions

- Notes: Session start `## master...origin/master [ahead 4]` with unrelated untracked `debug/EncodeRideBlock/`, `debug/RideBlockGuessPrototype/.idea/`, `debug/write-variant-profile.sh`. Saturn already implemented (commits `f1dabb3`, `dba5d3c`).
- Notes: Baseline before edits — Tokens.Tests 77, RidesCli.Tests 113, RideCaptureCli.Tests 32, oracle PASS (8 sequences, 39 observations).
- Assumptions: Saturn preserved unchanged; Uranus added after Saturn in registries.

---

# Phase 1 — Test-first production expectations

## Todos

- [x] Add or update `Tokens.Tests/TokenBlockUtilsTest.cs` fixtures so Uranus expected values are explicit and independent of production registration.
- [x] Update collision tests: Uranus should be part of the registered sequence set over `0..500`.
- [x] If Saturn is present, ensure collision tests include Jupiter, Saturn, and Uranus together over `0..500` and diagnostic `0..511`.
- [x] Replace any tests asserting Candidate C anchors are unknown with tests asserting Uranus decode succeeds.
- [x] Add registered sequence range/constructor tests as needed so Uranus is covered wherever Jupiter/Saturn are covered.

## Agent notes / assumptions

- Notes: Added `Uranus_hardware_observations_are_encoded_and_decoded`; replaced `Candidate_c_remains_collision_free_but_unregistered` with `All_registered_rotation_zero_sequences_have_unique_blocks_over_app_range`.
- Assumptions: None.

---

# Phase 2 — Register Uranus ride sequence

## Todos

- [x] Add `EncodingSequences.Uranus` in `Tokens/EncodingSequence.cs`.
- [x] Add Uranus to `EncodingSequences.All` in an order that keeps friendly-name listings stable and intentional.
- [x] Confirm `EncodingSequences.BuildRegistry` collision checks still pass after Uranus is registered.
- [x] Ensure `TokenBlockUtils.Decode`, `TryDecode`, `Encode`, and `EncodePreservingSequence` work for Uranus through existing registry paths without special cases.
- [x] Run `dotnet test Tokens.Tests --no-restore` — 90 passed.

## Agent notes / assumptions

- Notes: Uranus appended after Saturn in `All`.
- Assumptions: None.

---

# Phase 3 — Identity and reset profile registration

## Todos

- [x] Add `TokenIdentityProfiles.Uranus` with canonical Candidate-C identity and `uranus-0-rides.bin`.
- [x] Add Uranus to `TokenIdentityProfiles.All`.
- [x] Add/update `Tokens.Tests/TokenIdentityProfilesTests.cs` coverage (Resettable count 8).
- [x] Ensure `RideCaptureCli` known-token detection recognizes the canonical Uranus identity.

## Agent notes / assumptions

- Notes: None.
- Assumptions: None.

---

# Phase 4 — Reset image implementation

## Todos

- [x] Create `RidesCli/Data/uranus-0-rides.bin`.
- [x] Embed the reset image in `RidesCli/RidesCli.csproj`.
- [x] Ensure `TokenIdentityProfiles.Uranus` uses `uranus-0-rides.bin`.
- [x] Add reset image parser/existence tests through `ResetPage0BlocksLoaderTests.cs` (via existing all-resettable loop).
- [x] Add `reset --profile uranus` and `reset --sequence uranus` tests.
- [x] Verify reset tests prove blocks 0 and 7 are never written.

## Agent notes / assumptions

- Notes: None.
- Assumptions: Rollback coverage inherited from existing Jupiter/Saturn reset tests.

---

# Phase 5 — RidesCli read/set/add coverage

## Todos

- [x] Add `RideBlockResolverTests` Uranus mirror cases and anchor decode.
- [x] Add `RidesCommandHandlerTests` read/set/add coverage for Uranus.
- [x] Ensure no unknown-candidate tests remain for Candidate C.
- [x] Run `dotnet test RidesCli.Tests --no-restore` — 126 passed.

## Agent notes / assumptions

- Notes: None.
- Assumptions: None.

---

# Phase 6 — RideCaptureCli behavior

## Todos

- [x] Add CaptureSequenceService tests for Uranus decode, canonical identity, boundaries, stale history.
- [x] Run `dotnet test RideCaptureCli.Tests --no-restore` — 36 passed.

## Agent notes / assumptions

- Notes: None.
- Assumptions: None.

---

# Phase 7 — Documentation and oracle updates

## Todos

- [x] Update algorithm hypothesis and exploration docs for Uranus production status.
- [x] Update `debug/ride-encoding-hypothesis.py` — Uranus registered, 49 observations, 8 sequences.
- [x] Run `python3 debug/ride-encoding-hypothesis.py` — PASS.

## Agent notes / assumptions

- Notes: None.
- Assumptions: None.

---

# Phase 8 — Hardware reset smoke test

## Todos

- [x] Before hardware reset, run a read-only preflight and verify the intended sacrificial token with the user.
- [x] Hardware-smoke-test Uranus reset through the normal safe reset path.
- [x] Record final reset smoke-test evidence in this plan and docs.

## Agent notes / assumptions

- Notes: **Hardware reset smoke test passed 2026-07-22.** Sacrificial Saturn card on PM3 pre-reset: identity `23FE007B-D88CBD8A-5D04593D-5D04593D`, blocks 5/6 `8B13BD05` (500 rides, sequence saturn). `reset --profile uranus` succeeded. Post-reset blocks: 0=`00148040`, 1=`FBFE002A`, 2=`F1003C92`, 3=`F5D1D766`, 4=`F5D1D766`, 5=`891249D0`, 6=`891249D0`, 7=`00000000`. `RidesCli read` reported `sequence: uranus`, `rides remaining: 0`.
- Assumptions: None.

---

# Phase 9 — Final validation, cleanup, and handoff

## Todos

- [x] Run targeted tests — Tokens 90, RidesCli 126, RideCaptureCli 36, oracle PASS.
- [x] Run complete non-integration suite — all passed (Pm3UsbApi 120, 1 skipped).
- [x] Run `git diff --check` — clean.
- [x] Inspect `git status` — unrelated untracked paths untouched.
- [x] Update this plan's Agent notes (below).
- [x] Commit the code/test/docs/plan updates together.
- [x] Hand off to the reviewer/user.

## Agent notes / assumptions

- Notes:
  - **Uranus parameters:** `friendlyName=uranus`, `zeroBlock=891249D0`, `rotation=0`, `range=0..500`, identity `FBFE002A-F1003C92-F5D1D766-F5D1D766`.
  - **Hardware summary:** All listed boundary transitions and `1 -> 0` confirmed with matching blocks 5/6.
  - **Reset status:** Implemented, automated-test verified, and **hardware smoke test passed** (2026-07-22).
  - **Saturn/Jupiter:** Unchanged; no regressions observed.
  - **Test results:** Tokens.Tests 90, RidesCli.Tests 126, RideCaptureCli.Tests 36, oracle PASS (8 sequences, 49 observations).
- Assumptions: None.

---

# Follow-up work explicitly outside this plan

- [x] Complete Uranus hardware reset smoke test on user-confirmed sacrificial token. **Done 2026-07-22** — see Phase 8 Agent notes.
- [ ] Investigate whether rotations 1,2,3,5,6,7 occur in real tokens.
- [ ] Consider a future tool for inferring `(zeroBlock, rotation)` from multiple trusted anchors while refusing single-block ambiguity.
