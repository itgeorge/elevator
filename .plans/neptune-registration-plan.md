# Neptune Registration Plan

## How agents should use this plan

Read this entire file before making changes. Start each session with `git status --short --branch` and inspect the current versions of all files relevant to the next task. Find the next `[ ]` TODO and work on it; if new relevant work is discovered, add TODOs under the current phase before continuing. Keep working until the current TODO, or a coherent group of TODOs that forms a testable chunk, is complete. Mark completed items by changing `[ ]` to `[x]`, and document assumptions, deviations, hardware results, and design decisions in this file.

Commit completed plan updates in the same commit as the corresponding code/tests so this handoff remains aligned with implementation. Stage only files belonging to this work. Do not delete, rewrite, format, stage, or commit unrelated untracked files.

Important working-tree note at plan creation: the unrelated untracked paths visible in this checkout were:

```text
debug/EncodeRideBlock/
debug/RideBlockGuessPrototype/.idea/
debug/write-variant-profile.sh
```

Do not touch those paths unless the user explicitly changes scope.

---

## What this work is

Register the newly hardware-validated rotation-0 ride sequence as production **Neptune** and add its resettable identity profile.

Neptune uses the already-implemented generalized ride counter codec:

```text
friendlyName = neptune
zeroBlock    = 8F1249B0
rotation     = 0
range        = 0..500
```

This is not a codec refactor. Jupiter, Saturn, and Uranus already prove production/elevator support for rotation 0. This work should be a TDD sequence/profile registration with tests and documentation updates.

---

## End goal of this plan

- `EncodingSequences.Neptune` is registered for `0..500` using `zeroBlock=8F1249B0`, `rotation=0`.
- Neptune read/set/add flows work through `RidesCli`, preserving Neptune after writes.
- Neptune blocks decode structurally through the registered-sequence registry.
- Neptune has no self-collisions and no collisions with all other known registered sequences over `0..500`, and preferably also over the full diagnostic `0..511` counter range.
- Neptune identity recognition is added using the canonical zero-capture identity:

  ```text
  8BFE002A-F100C6A2-95D15917-95D15917
  ```

- Neptune reset support is implemented using `RidesCli/Data/neptune-0-rides.bin` and `TokenIdentityProfiles.Neptune`.
- Automated tests assert the Neptune reset image is a zero-ride image: blocks 5 and 6 equal `8F1249B0` and blocks 1..4 match the canonical Neptune identity.
- No hardware reset smoke test is required for this plan; the existing reset path is considered proven. Still preserve normal reset safety tests: reset writes only page-0 blocks 1..6 and never writes blocks 0 or 7.
- Documentation and exploration/oracle scripts reflect Neptune as production.
- All targeted and non-integration tests pass.

---

## Key working assumptions and non-goals

- Do not change the generalized codec unless a failing test proves the existing implementation is wrong.
- Do not infer arbitrary rotation-0 blocks. Production decode must continue to require a registered exact structural match.
- Do not rename or alter Jupiter, Saturn, or Uranus semantics while adding Neptune.
- Identity blocks 1..4 are metadata/reset profile identity, not ride-encoding inputs. Neptune ride blocks were validated on Saturn/Uranus identity tokens, which is acceptable evidence for the ride sequence.
- Normal set/add writes only blocks 5/6. Reset writes only blocks 1..6 through the existing safe verified path. Never write blocks 0 or 7.
- The app-supported range remains `0..500` even though the counter representation supports `0..511`.
- If any newly discovered hardware result differs from expected, stop and update this plan/model before registering or enabling reset.
- Non-goal: adding production support for any other unknown sequence from unlabeled dumps.

---

## Hardware evidence for Neptune

### Canonical zero capture

Original `RidesCli` dump reported as suspected zero:

```text
file: elevator-t55xx-00148040-8BFE002A-F100C6A2-95D15917-95D15917-8F1249B0-8F1249B0-57F674C3--rides-0.bin
block0 00148040
block1 8BFE002A
block2 F100C6A2
block3 95D15917
block4 95D15917
block5 8F1249B0
block6 8F1249B0
block7 57F674C3
```

Use blocks 1..4 as the canonical Neptune identity. Use blocks 5/6 as the Neptune zero block/reset ride encoding. Reset code must not write blocks 0 or 7; if the reset image includes the observed block7 value, document that it is loaded but not written.

### Boundary and high-count validation

All write tests wrote only page-0 blocks 5/6. Blocks 1..4 belonged to sacrificial Saturn/Uranus identity tokens, confirming again that identity blocks are not ride-encoding inputs.

```text
128 8F12C930 -> 127 7C1236CF  confirmed on token with Saturn identity
256 8F1349B1 -> 255 7C12B64F  confirmed on card with Uranus identity
384 8F13C931 -> 383 7C1336CE  confirmed on card with Uranus identity
500 8F13BD45 -> 497 8F13B840  confirmed after three rides on token with Saturn identity
```

Saved post-ride evidence dumps:

```text
elevator-t55xx-00148040-23FE007B-D88CBD8A-5D04593D-5D04593D-7C1236CF-7C1236CF-00000000--rides-UNKNOWN.bin
elevator-t55xx-00148040-FBFE002A-F1003C92-F5D1D766-F5D1D766-7C12B64F-7C12B64F-00000000--rides-UNKNOWN.bin
elevator-t55xx-00148040-FBFE002A-F1003C92-F5D1D766-F5D1D766-7C1336CE-7C1336CE-00000000--rides-UNKNOWN.bin
elevator-t55xx-00148040-23FE007B-D88CBD8A-5D04593D-5D04593D-8F13B840-8F13B840-00000000--rides-497.bin
```

Useful computed Neptune values for tests:

```text
  0 8F1249B0
  1 8F1248B1
127 7C1236CF
128 8F12C930
255 7C12B64F
256 8F1349B1
383 7C1336CE
384 8F13C931
497 8F13B840
499 8F13BA42
500 8F13BD45
```

---

# Phase 0 — Baseline inspection and evidence preservation

## Todos

- [x] Run `git status --short --branch` and record any changed/untracked paths in Agent notes before editing.
- [x] Confirm the current branch already includes Jupiter, Saturn, and Uranus production support; preserve them unchanged.
- [x] Read the current versions of these files before editing:
  - `Tokens/EncodingSequence.cs`
  - `Tokens/RideCounterCodec.cs`
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
- [x] Preserve the Neptune hardware observations above in tests and documentation before deleting or changing any unknown-sequence assertions.
- [x] Record baseline test status before editing, or note why baseline is intentionally skipped:

  ```bash
  dotnet test Tokens.Tests --no-restore
  dotnet test RidesCli.Tests --no-restore
  dotnet test RideCaptureCli.Tests --no-restore
  python3 debug/ride-encoding-hypothesis.py
  ```

## Agent notes / assumptions

- Notes: Baseline passed before edits: Tokens 90, RidesCli 126, RideCaptureCli 36; oracle passed with 8 sequences and 49 observations. Jupiter, Saturn, and Uranus support was present and left unchanged.
- Assumptions: The pre-existing unrelated untracked paths listed above remain out of scope.

---

# Phase 1 — TDD: sequence-level tests fail for Neptune

## Todos

- [x] Add `Tokens.Tests` fixtures for Neptune expected encodings independent of production registration:

  ```text
  0   -> 8F1249B0
  1   -> 8F1248B1
  127 -> 7C1236CF
  128 -> 8F12C930
  255 -> 7C12B64F
  256 -> 8F1349B1
  383 -> 7C1336CE
  384 -> 8F13C931
  497 -> 8F13B840
  499 -> 8F13BA42
  500 -> 8F13BD45
  ```

- [x] Add or extend collision tests so all registered sequences, including Neptune, have no self-collisions and no cross-sequence collisions over `0..500`.
- [x] Add the diagnostic full-counter collision check over `0..511` if an equivalent test already exists; otherwise add a clearly named test for it.
- [x] Add tests asserting known Neptune observation blocks decode as sequence `neptune` with the expected ride counts.
- [x] Verify the new tests fail before production registration, or document if the project test structure makes that impractical.

## Agent notes / assumptions

- Notes: Completed in the coherent Neptune registration/test implementation batch. Registration-driven behavior required no codec or command-handler special cases.
- Assumptions: Existing shared registry/profile paths remain the source of truth.

---

# Phase 2 — Register Neptune ride sequence

## Todos

- [x] Add `EncodingSequences.Neptune` in `Tokens/EncodingSequence.cs`:

  ```csharp
  public static readonly EncodingSequence Neptune = new("neptune", new T55Block(0x8F1249B0), 0, 0, 500);
  ```

- [x] Add Neptune to `EncodingSequences.All` in an intentional order after Uranus unless a nearby naming/listing convention suggests otherwise.
- [x] Confirm `EncodingSequences.BuildRegistry` collision checks pass after Neptune is registered.
- [x] Ensure `TokenBlockUtils.Decode`, `TryDecode`, `Encode`, and `EncodePreservingSequence` work for Neptune through existing registry paths without special cases.
- [x] Run `dotnet test Tokens.Tests --no-restore` and record the result.

## Agent notes / assumptions

- Notes: Completed in the coherent Neptune registration/test implementation batch. Registration-driven behavior required no codec or command-handler special cases.
- Assumptions: Existing shared registry/profile paths remain the source of truth.

---

# Phase 3 — TDD: identity and reset image expectations

## Todos

- [x] Add tests for `TokenIdentityProfiles.Neptune` before implementing it:
  - friendly name `neptune` resolves case-insensitively;
  - token id is `8BFE002A-F100C6A2-95D15917-95D15917`;
  - ride sequence is `EncodingSequences.Neptune`;
  - reset image filename is `neptune-0-rides.bin`;
  - Neptune appears in `TokenIdentityProfiles.All` and `TokenIdentityProfiles.Resettable`.
- [x] Add reset image loader tests asserting Neptune reset image blocks:

  ```text
  block1 8BFE002A
  block2 F100C6A2
  block3 95D15917
  block4 95D15917
  block5 8F1249B0
  block6 8F1249B0
  ```

- [x] If asserting the full eight-block image, use the canonical observed zero dump unless there is a documented project convention requiring `block7=00000000`:

  ```text
  block0 00148040
  block7 57F674C3  # observed; reset path must not write it
  ```

- [x] Verify these tests fail before adding the profile/image, or document why not.

## Agent notes / assumptions

- Notes: Completed in the coherent Neptune registration/test implementation batch. Registration-driven behavior required no codec or command-handler special cases.
- Assumptions: Existing shared registry/profile paths remain the source of truth.

---

# Phase 4 — Implement Neptune profile and reset image

## Todos

- [x] Add `TokenIdentityProfiles.Neptune` in `Tokens/TokenIdentityProfile.cs` using canonical identity blocks and `neptune-0-rides.bin`.
- [x] Add Neptune to `TokenIdentityProfiles.All`; because it is resettable, it should flow into `Resettable` automatically.
- [x] Create `RidesCli/Data/neptune-0-rides.bin` as big-endian page-0 words. Preferred canonical image from observed zero:

  ```text
  block0 00148040
  block1 8BFE002A
  block2 F100C6A2
  block3 95D15917
  block4 95D15917
  block5 8F1249B0
  block6 8F1249B0
  block7 57F674C3
  ```

  If choosing `block7=00000000` for consistency with other reset images, document that choice in this plan and docs. In either case, reset must still never write block 7.

- [x] Embed `RidesCli/Data/neptune-0-rides.bin` in `RidesCli/RidesCli.csproj`.
- [x] Run `dotnet test Tokens.Tests --no-restore` and relevant reset loader tests; record the result.

## Agent notes / assumptions

- Notes: Completed in the coherent Neptune registration/test implementation batch. Registration-driven behavior required no codec or command-handler special cases.
- Assumptions: Existing shared registry/profile paths remain the source of truth.

---

# Phase 5 — TDD: CLI read/set/add/reset behavior

## Todos

- [x] Add `RidesCli.Tests/RideBlockResolverTests.cs` coverage for Neptune blocks:
  - matching mirrored blocks return expected rides;
  - mismatch handling remains unchanged;
  - unknown/non-Neptune blocks do not decode by loose inference.
- [x] Add `RidesCli.Tests/RidesCommandHandlerTests.cs` coverage showing `read` reports `sequence: neptune` for Neptune blocks, including blocks on non-canonical Saturn/Uranus identities.
- [x] Add `set` and `add` preservation tests: after reading a Neptune token, `set`/`add` writes Neptune-encoded blocks and does not switch to another sequence.
- [x] Add `reset --profile neptune` and compatibility alias `reset --sequence neptune` tests.
- [x] Assert reset writes only blocks 1..6 and never writes blocks 0 or 7. Reuse existing reset safety helpers/patterns rather than duplicating PM3 logic.
- [x] Verify new tests fail before implementing missing CLI support, or document if registration makes them pass at the same time.

## Agent notes / assumptions

- Notes: Completed in the coherent Neptune registration/test implementation batch. Registration-driven behavior required no codec or command-handler special cases.
- Assumptions: Existing shared registry/profile paths remain the source of truth.

---

# Phase 6 — Implement CLI behavior and resource wiring

## Todos

- [x] Make any required `RidesCli` updates so Neptune appears in sequence/profile listings and reset commands.
- [x] Ensure help/error text remains accurate after adding Neptune. If range wording lists exceptions, verify it still states the correct global range and exceptions, if any.
- [x] Ensure `reset --sequence neptune` remains a compatibility alias for the resettable Neptune profile, matching existing behavior for other resettable profiles.
- [x] Run targeted CLI tests:

  ```bash
  dotnet test RidesCli.Tests --no-restore
  ```

- [x] Record the result in Agent notes.

## Agent notes / assumptions

- Notes: Completed in the coherent Neptune registration/test implementation batch. Registration-driven behavior required no codec or command-handler special cases.
- Assumptions: Existing shared registry/profile paths remain the source of truth.

---

# Phase 7 — TDD: RideCaptureCli first-scan/decode behavior

## Todos

- [x] Add `RideCaptureCli.Tests/CaptureSequenceServiceTests.cs` coverage that a first scan with Neptune ride blocks decodes structurally to `neptune` and the expected count, without relying on seeded fallback.
- [x] Include at least one canonical Neptune identity scan and one non-canonical identity scan using the validated Saturn/Uranus evidence identities.
- [x] Preserve existing behavior: if identity blocks are unknown but ride blocks decode, ride count/sequence can decode while identity warnings remain identity-based where applicable.
- [x] Verify the new tests fail before implementation if applicable.

## Agent notes / assumptions

- Notes: Completed in the coherent Neptune registration/test implementation batch. Registration-driven behavior required no codec or command-handler special cases.
- Assumptions: Existing shared registry/profile paths remain the source of truth.

---

# Phase 8 — Implement RideCaptureCli support

## Todos

- [x] Make any required `RideCaptureCli` updates so Neptune is recognized through the shared token registry/profile APIs.
- [x] Ensure seeded fallback cannot override Neptune structural decode.
- [x] Run targeted capture tests:

  ```bash
  dotnet test RideCaptureCli.Tests --no-restore
  ```

- [x] Record the result in Agent notes.

## Agent notes / assumptions

- Notes: Completed in the coherent Neptune registration/test implementation batch. Registration-driven behavior required no codec or command-handler special cases.
- Assumptions: Existing shared registry/profile paths remain the source of truth.

---

# Phase 9 — Exploration oracle and documentation

## Todos

- [x] Update `debug/ride-encoding-hypothesis.py`:
  - add `SequenceHypothesis("neptune", 0x8F1249B0, 0)`;
  - add Neptune observations from this plan;
  - include Neptune in 0..500 and 0..511 collision checks;
  - update expected sequence/observation counts in output assertions, if present.
- [x] Run the oracle and record the result:

  ```bash
  python3 debug/ride-encoding-hypothesis.py
  ```

- [x] Update `.docs/ride-encoding-algorithm-hypothesis-2026-07-21.md`:
  - list Neptune in production status;
  - add Neptune to the rotation-0 table;
  - record canonical identity, zero block, reset image status, and hardware evidence;
  - update confidence/open questions and collision counts.
- [x] Update `.docs/ride-encoding-exploration-2026-07-21.md`:
  - list Neptune in registered sequences;
  - add evidence summary and reset image status;
  - update any remaining “unknown rotation-0 candidate” text if the new evidence changes it.
- [x] Update any README/help docs that enumerate known sequence/profile names.

## Agent notes / assumptions

- Notes: Completed in the coherent Neptune registration/test implementation batch. Registration-driven behavior required no codec or command-handler special cases.
- Assumptions: Existing shared registry/profile paths remain the source of truth.

---

# Phase 10 — Final validation and handoff

## Todos

- [x] Run targeted suites:

  ```bash
  dotnet test Tokens.Tests --no-restore
  dotnet test RidesCli.Tests --no-restore
  dotnet test RideCaptureCli.Tests --no-restore
  python3 debug/ride-encoding-hypothesis.py
  ```

- [x] Run the complete non-integration suite:

  ```bash
  dotnet test ElevatorTokens.sln --no-restore --filter 'Category!=Integration&Category!=IntegrationParity'
  ```

- [x] Run `git diff --check`.
- [x] Inspect `git status --short --branch` and confirm unrelated untracked files remain untouched.
- [x] Update this plan's Agent notes with:
  - final Neptune parameters;
  - exact reset image choice, especially block7 if not using observed `57F674C3`;
  - hardware evidence summary;
  - test counts/results;
  - collision results;
  - any remaining risks or follow-up work.
- [x] Commit the code/test/docs/plan updates together.
- [x] Hand off to the reviewer/user with the final status and any remaining manual validation suggestions.

## Agent notes / assumptions

- Notes: Final parameters are `zeroBlock=8F1249B0`, rotation 0, range 0..500, identity `8BFE002A-F100C6A2-95D15917-95D15917`. The reset image uses observed block 7 `57F674C3`; reset tests confirm only blocks 1..6 are written. Hardware evidence covers 128/127, 256/255, 384/383, and 500→497. The plan's computed 499 vector was corrected from `8F13BA44` to codec/oracle result `8F13BA42`. Final targeted results: Tokens 101/101, RidesCli 140/140, RideCaptureCli 38/38, and oracle PASS. The complete filtered solution run passed 399 tests with 1 platform skip; TokenDumpsCli.Tests reported no test matching the category filter. Commit and handoff are recorded in repository history and the user response. Oracle checked 9 sequences with 58 observations and no collisions: 4,509 blocks over 0..500 and 4,608 over 0..511.
- Assumptions: No hardware reset smoke test was required; existing safe reset path plus automated block-range coverage is sufficient.

---

# Follow-up work explicitly outside this plan

- [ ] Optional: hardware-test `1 -> 0` for Neptune if the user later wants direct boundary evidence, even though this plan does not require a hardware reset smoke test.
- [ ] Investigate whether rotations 1,2,3,5,6,7 occur in real tokens.
- [ ] Consider a future tool for inferring `(zeroBlock, rotation)` from multiple trusted anchors while refusing single-block ambiguity.
