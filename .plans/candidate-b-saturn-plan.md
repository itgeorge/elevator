# Candidate B / Saturn Registration Plan

> **Superseding update:** This plan was written before Candidate C was registered. Candidate C is now registered as **Uranus** in `.plans/candidate-c-uranus-plan.md`. Notes below that say Candidate C remains unregistered/pending are historical to the Saturn implementation.

## How agents should use this plan

Read this entire file before making changes. Start each session with `git status --short --branch` and inspect the current versions of all files relevant to the next task. Find the next `[ ]` TODO and work on it; if new relevant work is discovered, add TODOs under the current phase before continuing. Keep working until the current TODO, or a coherent group of TODOs that forms a testable chunk, is complete. Mark completed items by changing `[ ]` to `[x]`, and document assumptions, deviations, hardware results, and design decisions in this file.

Commit completed plan updates in the same commit as the corresponding code/tests so this handoff remains aligned with implementation. Stage only files belonging to this work. Do not delete, rewrite, format, stage, or commit unrelated untracked files.

Important working-tree note at plan creation: the only unrelated untracked paths were:

```text
debug/EncodeRideBlock/
debug/RideBlockGuessPrototype/.idea/
debug/write-variant-profile.sh
```

Do not touch those paths unless the user explicitly changes scope.

---

## What this work is

Register hardware-validated Candidate B as the production ride sequence **Saturn**.

Candidate B uses the already-implemented generalized ride counter codec:

```text
friendlyName = saturn
zeroBlock    = 8B1249F0
rotation     = 0
range        = 0..500
```

This is not a codec refactor. Jupiter already proved production/elevator support for rotation 0 and reset flow. This plan is sequence-specific registration, tests, documentation, and any gated reset-profile work for Candidate B/Saturn.

---

## End goal of this plan

- `EncodingSequences.Saturn` is registered for `0..500` using `zeroBlock=8B1249F0`, `rotation=0`.
- Saturn read/set/add flows work through `RidesCli`, preserving Saturn after writes.
- Saturn blocks decode structurally through the registered-sequence registry.
- ~~Candidate C remains unregistered and unknown to production.~~ Superseded: Candidate C is registered as Uranus.
- Saturn identity recognition is added using the canonical Candidate-B identity:

  ```text
  23FE007B-D88CBD8A-5D04593D-5D04593D
  ```

- Saturn reset support is implemented using the now-known zero/reset image, then hardware-smoke-tested before final handoff.
- Documentation and exploration/oracle scripts reflect Saturn as production. Historical note: Candidate C was still pending during this plan, but is now Uranus.
- All targeted and non-integration tests pass.

---

## Key working assumptions and non-goals

- Do not change the generalized codec unless a failing test proves the existing implementation is wrong.
- Do not infer arbitrary rotation-0 blocks. Production decode must continue to require a registered exact structural match.
- Do not register Candidate C in this plan. Historical note: Candidate C was registered later as Uranus.
- Do not enable Candidate C identity or reset support in this plan. Historical note: enabled later in the Uranus plan.
- Saturn `1 -> 0` is confirmed, so the reset image is known; still smoke-test `reset --profile saturn` on a sacrificial token before declaring reset support complete.
- Normal set/add writes only blocks 5/6. Reset writes only blocks 1..6 through the existing safe verified path. Never write blocks 0 or 7.
- Identity blocks 1..4 are metadata/reset profile identity, not ride-encoding inputs. Saturn ride blocks were validated on EBFE/Jupiter identity tokens, which is acceptable evidence for the ride sequence.
- The app-supported range remains `0..500` even though the counter representation supports `0..511`.
- If any newly read hardware result differs from expected, stop and update this plan/model before registering or enabling reset.

---

## Hardware evidence for Saturn / Candidate B

### Pre-existing evidence

```text
47  781266DF -> 46  781267DE
130 8B12CB72  independent unlabeled dump; decodes with the same zeroBlock=8B1249F0 and rotation=0
```

### Newly validated transition tests, 2026-07-22

These were tested by writing only page-0 blocks 5/6 on sacrificial card/fob tokens whose identity blocks were EBFE/Jupiter. Blocks 5 and 6 matched in each confirmed post-ride read.

```text
128 8B12C970 -> 127 7812368F  confirmed on card
256 8B1349F1 -> 255 7812B60F  confirmed on fob
384 8B13C971 -> 383 7813368E  confirmed on card, one ride
8   781241F8 -> 7   8B124EF7  confirmed on fob
```

### Final zero/reset-image confirmation

The final zero transition is confirmed:

```text
1   8B1248F1 -> 0   8B1249F0  confirmed on card
```

Blocks 5 and 6 matched in the readback. This confirms the Saturn zero block and makes the Saturn reset image known:

```text
block0 00148040
block1 23FE007B
block2 D88CBD8A
block3 5D04593D
block4 5D04593D
block5 8B1249F0
block6 8B1249F0
block7 00000000
```

The implementing agent may create/register `RidesCli/Data/saturn-0-rides.bin`, but must still run the normal reset safety tests and perform a hardware reset smoke test on a user-confirmed sacrificial token before declaring reset support complete.

---

# Phase 0 — Baseline inspection and evidence preservation

## Todos

- [x] Run `git status --short --branch` and record any changed/untracked paths in Agent notes before editing.
- [x] Read the current versions of these files before editing:
  - `Tokens/EncodingSequence.cs`
  - `Tokens/TokenBlockUtils.cs`
  - `Tokens/TokenIdentityProfile.cs`
  - `Tokens.Tests/TokenBlockUtilsTest.cs`
  - `Tokens.Tests/TokenIdentityProfilesTests.cs`
  - `RidesCli/RideBlockResolver.cs`
  - `RidesCli/RidesCommandHandler.cs`
  - `RidesCli.Tests/RideBlockResolverTests.cs`
  - `RidesCli.Tests/RidesCommandHandlerTests.cs`
  - `RideCaptureCli/CaptureSequenceService.cs`
  - `RideCaptureCli.Tests/CaptureSequenceServiceTests.cs`
  - `.docs/ride-encoding-algorithm-hypothesis-2026-07-21.md`
  - `.docs/ride-encoding-exploration-2026-07-21.md`
  - `debug/ride-encoding-hypothesis.py`
- [x] Preserve the confirmed Saturn hardware observations above in tests and documentation before changing any old Candidate-B unknown assertions.
- [x] Read back the final Saturn zero test:

  ```text
  1 8B1248F1 -> 0 8B1249F0
  blocks 5 and 6 matched
  ```

- [x] If the `1 -> 0` result differs from expected, stop and update the plan/model before registering Saturn.
- [x] Record the final `1 -> 0` result in this plan's Agent notes.

## Agent notes / assumptions

- Notes: Session start `git status`: `## master...origin/master [ahead 2]` with unrelated untracked paths `debug/EncodeRideBlock/`, `debug/RideBlockGuessPrototype/.idea/`, `debug/write-variant-profile.sh`. Plan file was untracked at session start.
- Notes: User reported both staged tests worked; readback confirmed card blocks 5/6 as `8B1249F0`, completing Saturn `1 -> 0` validation and confirming the reset image content listed above.
- Assumptions: None beyond plan defaults.

---

# Phase 1 — Test-first production expectations

## Todos

- [x] Add or update `Tokens.Tests/TokenBlockUtilsTest.cs` fixtures so Saturn expected values are explicit and independent of production registration.
  - Include at least:

    ```text
    0   8B1249F0
    1   8B1248F1
    7   8B124EF7
    8   781241F8
    127 7812368F
    128 8B12C970
    255 7812B60F
    256 8B1349F1
    383 7813368E
    384 8B13C971
    500 8B13BD05
    ```

  - Include the historical/independent points:

    ```text
    46  781267DE
    47  781266DF
    130 8B12CB72
    ```

- [x] Update collision tests: Saturn should be part of the registered sequence set over `0..500`; Candidate C should remain an unregistered non-colliding hypothesis.
- [x] Replace any tests asserting Candidate B anchors are unknown with tests asserting Saturn decode succeeds. Keep Candidate C unknown tests.
- [x] Add registered sequence range/constructor tests as needed so Saturn is covered wherever Jupiter is covered.

## Agent notes / assumptions

- Notes: Added `Saturn_hardware_observations_are_encoded_and_decoded` TestCase matrix; renamed collision test to `Candidate_c_remains_collision_free_but_unregistered`; added Saturn to `Registered` fixture array.
- Assumptions: None.

---

# Phase 2 — Register Saturn ride sequence

## Todos

- [x] Add `EncodingSequences.Saturn` in `Tokens/EncodingSequence.cs`:

  ```csharp
  public static readonly EncodingSequence Saturn = new("saturn", new T55Block(0x8B1249F0), 0, 0, 500);
  ```

- [x] Add Saturn to `EncodingSequences.All` in an order that keeps friendly-name listings stable and intentional.
- [x] Confirm `EncodingSequences.BuildRegistry` collision checks still pass after Saturn is registered.
- [x] Ensure `TokenBlockUtils.Decode`, `TryDecode`, `Encode`, and `EncodePreservingSequence` work for Saturn through existing registry paths without special cases.
- [x] Run:

  ```bash
  dotnet test Tokens.Tests --no-restore
  ```

## Agent notes / assumptions

- Notes: Saturn appended after Jupiter in `All`. Registry collision check passes at build time. Tokens.Tests: 77 passed.
- Assumptions: None.

---

# Phase 3 — Identity recognition and reset gating

## Todos

- [x] Add `TokenIdentityProfiles.Saturn` with canonical Candidate-B identity:

  ```text
  friendlyName = saturn
  sequence     = EncodingSequences.Saturn
  tokenId      = 23FE007B-D88CBD8A-5D04593D-5D04593D
  resetImage   = saturn-0-rides.bin
  ```

- [x] Add Saturn to `TokenIdentityProfiles.All`.
- [x] Add/update `Tokens.Tests/TokenIdentityProfilesTests.cs` coverage:
  - friendly-name lookup is case-insensitive;
  - Saturn maps to `EncodingSequences.Saturn`;
  - Saturn's token id is exact;
  - Saturn is resettable once `saturn-0-rides.bin` is added and embedded.
- [x] Ensure `RideCaptureCli` known-token detection recognizes the canonical Saturn identity and does not mark it `UNKNOWN_TOKEN`.
- [x] Add `saturn-0-rides.bin` and make Saturn resettable as part of the reset-image phase; do not defer Saturn as recognition-only unless the user explicitly changes scope.

## Agent notes / assumptions

- Notes: `Resettable` count updated to 7. Capture tests confirm canonical Saturn identity is known.
- Assumptions: None.

---

# Phase 4 — RidesCli read/set/add coverage

## Todos

- [x] Add `RidesCli.Tests/RideBlockResolverTests.cs` cases for matching Saturn mirrors:

  ```text
  8B1249F0 -> 0
  8B1248F1 -> 1
  781241F8 -> 8
  8B12C970 -> 128
  8B1349F1 -> 256
  8B13C971 -> 384
  8B13BD05 -> 500
  ```

- [x] Add `RidesCli.Tests/RidesCommandHandlerTests.cs` coverage showing read output reports `sequence: saturn` for Saturn blocks.
- [x] Add set/add tests showing a Saturn source block preserves Saturn encoding after writes.
  - Include boundary crossings such as `127 -> 128`, `255 -> 256`, and `383 -> 384` if practical.
- [x] Ensure unknown-Candidate-C behavior remains unknown in `RidesCli`.
- [x] Run:

  ```bash
  dotnet test RidesCli.Tests --no-restore
  ```

## Agent notes / assumptions

- Notes: RidesCli.Tests: 113 passed. Candidate C anchor `7A1222BB` still resolves unknown.
- Assumptions: None.

---

# Phase 5 — RideCaptureCli behavior

## Todos

- [x] Add or update `RideCaptureCli.Tests/CaptureSequenceServiceTests.cs` so first mirrored Saturn scans decode exact structural ride counts, even when the identity is not canonical Saturn.
  - Include at least the independent `130 -> 8B12CB72` point.
  - Include a boundary point such as `128 -> 8B12C970` or `256 -> 8B1349F1`.
- [x] Add a test showing canonical Saturn identity is known and does not produce `UNKNOWN_TOKEN`.
- [x] Verify that existing stale-history hardening still uses decoded Saturn counts rather than blindly decrementing old labels.
- [x] Run:

  ```bash
  dotnet test RideCaptureCli.Tests --no-restore
  ```

## Agent notes / assumptions

- Notes: RideCaptureCli.Tests: 32 passed. Added stale-history test mirroring Jupiter pattern for Saturn.
- Assumptions: None.

---

# Phase 6 — Documentation and oracle updates

## Todos

- [x] Update `.docs/ride-encoding-algorithm-hypothesis-2026-07-21.md`:
  - move Candidate B/Saturn from pending to production;
  - record `zeroBlock=8B1249F0`, `rotation=0`, range `0..500`;
  - record all confirmed Saturn transition tests;
  - keep Candidate C pending;
  - note that the old failed Candidate-B synthesized values used the obsolete family formula and are superseded.
- [x] Update `.docs/ride-encoding-exploration-2026-07-21.md` so its current-production status is not stale.
- [x] Update `debug/ride-encoding-hypothesis.py`:
  - treat Saturn as a registered production sequence;
  - keep Candidate C as an unregistered hypothesis;
  - preserve collision checks over `0..500` and diagnostic `0..511`.
- [x] Run:

  ```bash
  python3 debug/ride-encoding-hypothesis.py
  ```

## Agent notes / assumptions

- Notes: Oracle PASS with 8 sequences, 39 observations.
- Assumptions: None.

---

# Phase 7 — Reset image implementation and smoke test

The `1 -> 0` zero transition is confirmed, so Saturn reset support should be implemented in this plan. The reset image content is known, but a normal hardware reset smoke test is still required before final handoff.

## Todos

- [x] Confirm hardware result:

  ```text
  written 1: 8B1248F1
  after one elevator ride: 8B1249F0
  blocks 5 and 6 matched
  ```

- [x] If the result differs, stop and update model/docs/tests; do not create or register a reset image.
- [x] Create `RidesCli/Data/saturn-0-rides.bin` as big-endian page-0 words:

  ```text
  block0 00148040
  block1 23FE007B
  block2 D88CBD8A
  block3 5D04593D
  block4 5D04593D
  block5 8B1249F0
  block6 8B1249F0
  block7 00000000
  ```

- [x] Embed the reset image in `RidesCli/RidesCli.csproj`.
- [x] Change `TokenIdentityProfiles.Saturn` to use `saturn-0-rides.bin`.
- [x] Add reset image parser/existence tests and `reset --profile saturn` / compatibility alias tests.
- [x] Before any hardware reset smoke test, run a read-only preflight and verify the intended sacrificial token with the user.
- [x] Hardware-smoke-test Saturn reset through the normal safe reset path:

  ```text
  reset --profile saturn
  verify blocks 1..6
  read -> sequence saturn, rides 0
  ```

- [x] Verify reset never writes blocks 0 or 7 and rollback behavior remains covered by automated tests.
- [x] Record final reset smoke-test evidence in this plan and docs.

## Agent notes / assumptions

- Notes: Saturn zero is confirmed. Created and registered `saturn-0-rides.bin` using canonical Candidate-B identity blocks and mirrored zero `8B1249F0`. Automated tests cover `reset --profile saturn`, `reset --sequence saturn`, embedded image validation via `ResetPage0BlocksLoaderTests`, and blocks 0/7 never written.
- Notes: **Hardware reset smoke test passed 2026-07-22.** Sacrificial card on PM3 pre-reset: identity `EBFE002A-F100CC5B-A5045936-A5045936` (Jupiter), blocks 5/6 `89124ED7` (unregistered Candidate-C structural decode at 7 rides). `reset --profile saturn` succeeded. Post-reset blocks: 0=`00148040`, 1=`23FE007B`, 2=`D88CBD8A`, 3=`5D04593D`, 4=`5D04593D`, 5=`8B1249F0`, 6=`8B1249F0`, 7=`00000000`. `RidesCli read` reported `sequence: saturn`, `rides remaining: 0`.
- Assumptions: Automated reset path reuses existing Jupiter/Mars rollback coverage; no Saturn-specific rollback test added.

---

# Phase 8 — Final validation, cleanup, and handoff

## Todos

- [x] Run targeted tests:

  ```bash
  dotnet test Tokens.Tests --no-restore
  dotnet test RidesCli.Tests --no-restore
  dotnet test RideCaptureCli.Tests --no-restore
  python3 debug/ride-encoding-hypothesis.py
  ```

- [x] Run complete non-integration suite:

  ```bash
  dotnet test ElevatorTokens.sln --no-restore --filter 'Category!=Integration&Category!=IntegrationParity'
  ```

- [x] Run `git diff --check`.
- [x] Inspect `git status --short --branch` and confirm unrelated untracked files remain untouched.
- [x] Update this plan's Agent notes with:
  - final Saturn parameters;
  - hardware result summary;
  - reset enabled vs recognition-only status;
  - test counts/results;
  - any remaining risks or follow-up work.
- [x] Commit the code/test/docs/plan updates together.
- [x] Hand off to the reviewer/user and explicitly state that Candidate C remained unregistered at that time. Superseded: Candidate C is now Uranus.

## Agent notes / assumptions

- Notes:
  - **Saturn parameters:** `friendlyName=saturn`, `zeroBlock=8B1249F0`, `rotation=0`, `range=0..500`, identity `23FE007B-D88CBD8A-5D04593D-5D04593D`.
  - **Hardware summary:** All listed boundary transitions and `1 -> 0` confirmed with matching blocks 5/6.
  - **Reset status:** Implemented, automated-test verified, and **hardware smoke test passed** (2026-07-22).
  - **Test results:** Tokens.Tests 77, RidesCli.Tests 113, RideCaptureCli.Tests 32, Pm3UsbApi.Tests 120 (1 skipped), oracle PASS. Full non-integration filter: all passed.
  - **Remaining risks at Saturn handoff:** Candidate C still unregistered. Superseded: Candidate C is now Uranus.
- Assumptions: None.

---

# Follow-up work explicitly outside this plan

- [x] Hardware-test and separately register Candidate C (`zeroBlock=891249D0`, rotation 0) if validated. Completed later in `.plans/candidate-c-uranus-plan.md`.
- [ ] Investigate whether rotations 1,2,3,5,6,7 occur in real tokens.
- [ ] Consider a future tool for inferring `(zeroBlock, rotation)` from multiple trusted anchors while refusing single-block ambiguity.
- [x] Complete Saturn hardware reset smoke test on user-confirmed sacrificial token. **Done 2026-07-22** — see Phase 7 Agent notes.
