# Generalized Ride Encoding and Jupiter Registration Plan

## How agents should use this plan

Read this entire file before making changes. Start each session with `git status --short --branch` and inspect the current versions of all files relevant to the next task. Find the next `[ ]` TODO and work on it; if new relevant work is discovered, add TODOs under the current phase before continuing. Keep working until the current TODO, or a coherent group of TODOs that forms a testable chunk, is complete. Mark completed items by changing `[ ]` to `[x]`, and document assumptions, deviations, hardware results, and design decisions in this file.

Commit completed plan updates in the same commit as the corresponding code/tests so this handoff remains aligned with implementation. Stage only files belonging to this work. Do not delete, rewrite, format, stage, or commit unrelated untracked files.

Important working-tree note: at plan creation, the repo contains uncommitted Earth/Pluto high-range production changes and generalized-algorithm exploration artifacts, plus unrelated pre-existing debug files. Treat the Earth/Pluto changes and these two exploration artifacts as required baseline inputs for this plan:

```text
.docs/ride-encoding-algorithm-hypothesis-2026-07-21.md
debug/ride-encoding-hypothesis.py
```

Known unrelated/pre-existing untracked paths include:

```text
debug/EncodeRideBlock/
debug/RideBlockGuessPrototype/.idea/
debug/write-variant-profile.sh
```

Inspect before touching; do not assume ownership merely because a path is untracked.

---

## What this work is

Replace the current 128-value `Family` / `EncodingSequenceSegment` implementation with the generalized zero-block + rotation algorithm discovered from all registered sequences, while preserving every currently supported encoded value exactly.

Then add the hardware-validated EBFE/Candidate-D sequence to production under the friendly name **Jupiter**.

The generalized model is:

```text
block(n) = zeroBlock XOR counterDelta(n, rotation)

r = n & 0xFF
h = (n >> 8) & 1
p = ROL8(r, rotation) XOR (h << rotation)

counterDelta byte 0 = F3 if bit 3 of p is set, otherwise 00
counterDelta byte 1 = h
counterDelta byte 2 = r
counterDelta byte 3 = p
```

Current registered sequences use rotation 4. Jupiter uses rotation 0.

---

## End goal of this plan

- Production encoding/decoding uses `(zeroBlock, rotation, minRides, maxRides)`, not 128-value family tables.
- `EncodingSequenceSegment`, `TokenBlockUtils.Family`, family registries, and high16-only sequence detection are removed if no longer needed.
- Mercury, Venus, Earth, Pluto, and Mars produce **exactly the same blocks for every ride count 0..500** as the current implementation.
- Jupiter is registered for 0..500 with:

  ```text
  friendlyName = jupiter
  zeroBlock    = 8C124980
  rotation     = 0
  canonical identity = EBFE002A-F100CC5B-A5045936-A5045936
  ```

- Jupiter read/set/add/price and sequence-preserving writes work through `RidesCli`.
- Jupiter is recognized by `RideCaptureCli` and no longer depends on the incorrect historical EBFE seed count.
- Registered sequence decode is based on full structural validation, not high16 lookup.
- All registered sequences are self-collision-free and mutually collision-free over 0..500 (also check 0..511 as a diagnostic).
- Candidates B/C remain unregistered and unresolved by production until separate hardware validation/registration work.
- Jupiter reset support is finalized only after hardware confirms `1 -> 0` and the reset image is tested.

---

## Key working assumptions and non-goals

- Breaking compatibility for `EncodingSequenceSegment` and `TokenBlockUtils.Family` is allowed. Migrate all solution callers/tests cleanly rather than retaining a misleading compatibility layer.
- Preserve public behavior that matters to users: friendly sequence names, read/set/add/reset flows, block safety, and all encoded values.
- Normal set/add writes only blocks 5/6. Reset writes only blocks 1..6 through the existing verified/rollback path. Never write blocks 0 or 7.
- Jupiter is the only rotation-0 sequence to register in this plan.
- Candidate B (`zero=8B1249F0`, rotation 0) and Candidate C (`zero=891249D0`, rotation 0) remain exploration fixtures only. Production decode should return unknown for their blocks.
- Do not infer a ride count for an arbitrary unregistered block by inventing a zero block. Production decoding must match a registered sequence and validate exact round-trip structure.
- The app-supported range remains 0..500 even though the counter representation supports 0..511.
- Jupiter canonical identity/reset image uses the EBFE fob profile, not the unrelated 9BFE card used for some boundary tests.
- Jupiter zero `8C124980` is currently inferred with strong model support but awaits the final `1 -> 0` hardware read requested below.

---

## Confirmed hardware evidence available to the implementing agent

### Rotation-4 baseline

Earth corrected high boundaries:

```text
256 18131208 -> 255 EB12EDE7
384 EB139200 -> 383 18136DFF
```

Pluto corrected high boundaries:

```text
256 1F13120F -> 255 EC12EDE0
384 EC139207 -> 383 1F136DF8
```

### Jupiter / EBFE / Candidate D

Parameters inferred from all points:

```text
zeroBlock = 8C124980
rotation  = 0
range     = 0..500
```

Elevator-confirmed transitions:

```text
57  7F1270B9 -> 56  7F1271B8
128 8C12C900 -> 127 7F1236FF
256 8C134981 -> 255 7F12B67F
8   7F124188 -> 7   8C124E87
384 8C13C901 -> 381 7F1334FC  (three rides)
```

Additional historical blocks explained by the same model include:

```text
261 -> 8C134C84
256 -> 8C134981
255 -> 7F12B67F
247 -> 8C12BE77
240 -> 8C12B970
238 -> 7F12A76E
```

The old `ride-capture-data/captures.csv` EBFE real-count labels are offset/interrupted and must not be used as authoritative expected counts.

Pending hardware result:

```text
Card currently written at Jupiter 1: 8C124881
Expected after one elevator ride: Jupiter 0: 8C124980
```

---

# Phase 0 — Baseline, coordination, and evidence preservation

## Todos

- [x] Run `git status --short --branch`; record all modified/untracked paths in Agent notes before editing.
- [x] Read these files completely:
  - `.docs/ride-encoding-exploration-2026-07-21.md`
  - `.docs/ride-encoding-algorithm-hypothesis-2026-07-21.md`
  - `.plans/d3-earth-sequence-plan.md`
  - `Tokens/EncodingSequence.cs`
  - `Tokens/TokenBlockUtils.cs`
  - `Tokens/TokenIdentityProfile.cs`
  - `Tokens.Tests/TokenBlockUtilsTest.cs`
  - `RidesCli/RideBlockResolver.cs`
  - `RidesCli/RidesCommandHandler.cs`
  - `RideCaptureCli/SeededTokenCatalog.cs`
  - `debug/ride-encoding-hypothesis.py`
- [x] Confirm the uncommitted Earth/Pluto high-range changes are present and tests currently pass. Do not accidentally start from the old 0..255 model.
- [x] Run and record baseline results:

  ```bash
  python3 debug/ride-encoding-hypothesis.py
  dotnet test Tokens.Tests --no-restore
  dotnet test RidesCli.Tests --no-restore
  dotnet test ElevatorTokens.sln --no-restore --filter 'Category!=Integration&Category!=IntegrationParity'
  ```

- [x] Preserve the exact hardware observations above in test fixtures or documentation before deleting family-based code.

## Agent notes / assumptions

- Notes: Baseline status on `master` (ahead 3) has only these untracked, unrelated paths: `debug/EncodeRideBlock/`, `debug/RideBlockGuessPrototype/.idea/`, and `debug/write-variant-profile.sh`. No modified tracked files were present. The committed baseline contains the corrected Earth/Pluto 0..500 family ranges; this work starts from that high-range model rather than the obsolete 0..255 version.
- Notes: Baseline commands passed: hypothesis oracle (29 observations, 8-sequence collision checks at 0..500 and 0..511), `Tokens.Tests` (94), `RidesCli.Tests` (90), and full non-integration solution suite (Tokens 94; RideCapture 26; RidesCli 90; Pm3Usb 120 passed/1 skipped; TokenDumps no filter matches).
- Assumptions:

---

# Phase 1 — Add independent compatibility and collision tests before refactoring

## Todos

- [x] Add a test-side reference implementation of the **current rotation-4 family algorithm** or retain immutable golden tables sufficient to compare old vs new output independently.
  - Do not make the compatibility test call the same generalized implementation on both sides.
  - Cover every value 0..500 for Mercury, Venus, Earth, Pluto, and Mars.
  - Expected zero blocks:

    ```text
    Mercury CCC749CC
    Venus   48C74948
    Earth   18121218
    Pluto   1F12121F
    Mars    4EC7494E
    ```

- [x] Add explicit golden assertions for known hardware boundaries, including:

  ```text
  Mercury: existing 0/127/128/255/256/383/384/500 references
  Venus:   127/128/255/256/383/384/500
  Earth:   255/256/383/384/500
  Pluto:   255/256/383/384/500
  Mars:    127/128/255/256/383/384/500
  Jupiter: 0/1/7/8/56/57/127/128/238/240/247/255/256/381/384/500
  ```

- [x] Add exhaustive collision tests using the proposed parameters for all six production sequences:
  - no duplicate blocks within each sequence over 0..500;
  - no duplicate blocks across registered sequences over 0..500;
  - repeat over 0..511 as a diagnostic of the full 9-bit representation.
- [x] Assert Candidates B/C do not collide with any registered sequence over 0..500, but do **not** register them.
- [x] Add fixtures showing Candidate B/C anchors remain unknown to production:

  ```text
  B 47  -> 781266DF
  C 107 -> 7A1222BB
  ```

- [x] Commit this characterization test layer separately if practical, before replacing production internals.

## Agent notes / assumptions

- Notes:
- Assumptions:

---

# Phase 2 — Implement the generalized counter codec

## Todos

- [x] Introduce a small, deterministic codec in `Tokens` (name may evolve; update this plan if it does). Suggested shape:

  ```csharp
  internal static class RideCounterCodec
  {
      internal static uint BuildDelta(uint rides, byte rotation);
      internal static T55Block Encode(T55Block zeroBlock, byte rotation, uint rides);
      internal static bool TryDecode(
          T55Block zeroBlock,
          byte rotation,
          T55Block block,
          out uint rides);
  }
  ```

- [x] Validate constructor/input constraints:
  - rotation must be 0..7;
  - codec counter must be 0..511;
  - sequence/application range remains independently limited to 0..500.
- [x] Implement `ROL8` without relying on undefined shifts for rotation 0.
- [x] Implement exact structural decode, not partial byte extraction:
  1. `delta = block XOR zeroBlock`;
  2. byte 1 must be only `0` or `1`;
  3. derive `n = (byte1 << 8) | byte2`;
  4. recompute `BuildDelta(n, rotation)`;
  5. require exact 32-bit equality with `delta`;
  6. require the sequence range to contain `n`;
  7. round-trip encode must equal the input block.
- [x] Add focused unit tests for rotations 0 and 4, all counter bits 0..8, boundaries 0/255/256/511, malformed deltas, and invalid rotations/ranges.
- [x] Keep this codec independent of sequence registration so it can be exhaustively tested.

## Agent notes / assumptions

- Notes:
- Assumptions:

---

# Phase 3 — Replace family segments with generalized EncodingSequence

## Todos

- [x] Replace the current segment/family model with a direct sequence definition. Suggested starting shape:

  ```csharp
  public sealed class EncodingSequence
  {
      public string FriendlyName { get; }
      public T55Block ZeroBlock { get; }
      public byte Rotation { get; }
      public uint MinRides { get; }
      public uint MaxRides { get; }

      public T55Block Encode(uint rides);
      public bool TryDecode(T55Block block, out uint rides);
  }
  ```

- [x] Register the five existing sequences using rotation 4 and range 0..500.
- [x] Register Jupiter using zero `8C124980`, rotation 0, range 0..500.
- [x] Remove `EncodingSequenceSegment` and `EncodingFamilyDefinitions` after all callers migrate.
- [x] Remove `TokenBlockUtils.Family`, `TokenBlockUtils.Families`, `EncodeByFamily`, and high16 family maps if no production caller still needs them.
  - Do not retain them solely to avoid updating tests.
  - If a narrow compatibility helper is temporarily necessary within an intermediate commit, mark it clearly and remove it before completing the plan.
- [x] Make sequence registration fail fast on duplicate friendly names and exact encoded collisions.
- [x] Ensure sequence definitions remain the single source of truth for zero block, rotation, and supported range.

## Agent notes / assumptions

- Notes:
- Assumptions:

---

# Phase 4 — Replace high16 lookup with registered-sequence decode

## Todos

- [x] Replace `EncodingSequences.SequenceByHigh16` and `TryGetSequenceFromBlock` high16 lookup with full registered-sequence matching.
- [x] Provide one authoritative decode operation that can return both sequence and rides. Suggested shape:

  ```csharp
  public static bool TryDecode(
      T55Block block,
      out EncodingSequence? sequence,
      out uint rides);
  ```

- [x] Define ambiguity behavior explicitly:
  - zero matches -> unknown sequence;
  - one exact structural/range match -> success;
  - more than one match -> fail loudly as ambiguous (registration collision tests should prevent this for generated blocks).
- [x] Route `TokenBlockUtils.Encode`, `TryDecode`, `Decode`, and `EncodePreservingSequence` through the generalized registry/model.
- [x] Preserve clear exception/error distinctions where practical:
  - unknown/unregistered sequence;
  - structurally invalid block for all registered sequences;
  - ambiguous registration/match.
- [x] Update `RidesCli/RideBlockResolver` so it does not call `Families.TryGetFamilyFromBlock`.
  - Matching mirrors: decode via registered sequences.
  - Mismatched mirrors: preserve confirmed preference for valid block 6.
  - Unknown candidate B/C blocks must still produce `UnknownEncodingFamily` (or a renamed equivalent documented in tests).
- [x] Add tests proving blocks with the same or changing high16 are decoded by full structure, especially Jupiter’s 8→7 and 128→127 transitions.

## Agent notes / assumptions

- Notes:
- Assumptions:

---

# Phase 5 — Register Jupiter in application workflows

## Todos

- [x] Add `EncodingSequences.Jupiter`:

  ```text
  name      jupiter
  zero      8C124980
  rotation  0
  range     0..500
  ```

- [x] Add a Jupiter identity profile for canonical EBFE blocks:

  ```text
  EBFE002A-F100CC5B-A5045936-A5045936
  ```

  Initially it may be recognition-only until Phase 8 confirms reset image safety.

- [x] Ensure Jupiter is included in friendly-name formatting and sequence listings.
- [x] Update `RidesCli` read/set/add/price flows:
  - reading any valid Jupiter block identifies `jupiter` and its ride count;
  - `set 0`, `set 8`, `set 128`, `set 256`, `set 384`, and `set 500` use rotation 0;
  - add operations correctly cross 7/8, 127/128, 255/256, and 383/384;
  - sequence preservation uses the decoded Jupiter sequence, not blocks 1..4.
- [x] Update `FakeRidesPm3Api` and test builders so they construct arbitrary generalized sequences without depending on family APIs.
- [x] Add CLI tests for all Jupiter hardware points and range boundaries.
- [x] Confirm identity independence in tests: a token with non-EBFE identity blocks but valid Jupiter blocks 5/6 must still read/set/add as Jupiter.

## Agent notes / assumptions

- Notes:
- Assumptions:

---

# Phase 6 — Migrate RideCaptureCli and remove the stale EBFE seed

## Todos

- [x] Verify `RideCaptureCli` decode-first behavior uses the generalized production decoder.
- [x] Register the EBFE identity as known Jupiter identity so captures do not emit `UNKNOWN_TOKEN` for the canonical fob.
- [x] Remove or explicitly supersede this incorrect historical seed:

  ```text
  EBFE002A-F100CC5B-A5045936-A5045936 / 8C134C84 / starting 262
  ```

  The generalized model decodes `8C134C84` as 261. Do not preserve 262 merely for backward compatibility.
- [x] Add capture tests showing:
  - `8C134C84` decodes as Jupiter 261;
  - trusted current points decode correctly;
  - existing CSV’s incorrect `real_ride_count` does not override generalized decoding;
  - candidate B/C remain unknown/unregistered;
  - canonical Jupiter identity is known.
- [x] Ensure sequence continuation/normalization logic still works when Jupiter high16 changes every eight-count band.
- [x] Do not add Candidates B/C to `TokenIdentityProfiles`, `EncodingSequences.All`, or reset profiles in this plan.

## Agent notes / assumptions

- Notes:
- Assumptions:

---

# Phase 7 — Exhaustive regression, malformed input, and cleanup

## Todos

- [x] Prove exact encode equivalence for Mercury/Venus/Earth/Pluto/Mars over every value 0..500 against the independent pre-refactor oracle from Phase 1.
- [x] Prove encode/decode round-trip for all six registered sequences over every value 0..500.
- [x] Prove no self/cross collisions over 0..500 and diagnostic 0..511.
- [x] Add malformed-block tests for each rotation:
  - incorrect duplicated high counter bit;
  - wrong F3 toggle;
  - wrong rotated payload;
  - out-of-range 501..511 at sequence/application layer;
  - random unknown block;
  - mirror mismatch with one/both valid.
- [x] Add tests showing B/C anchors are not decoded merely because they use rotation 0.
- [x] Remove all obsolete family/segment tests and replace them with behavior-oriented generalized tests.
- [x] Run `rg` for stale production assumptions and remove/update them:

  ```text
  EncodingSequenceSegment
  EncodingFamilyDefinitions
  TokenBlockUtils.Family
  TokenBlockUtils.Families
  EncodeByFamily
  GetFamilyForRides
  SequenceByHigh16
  baseLow
  C7/C6 only
  Earth/Pluto 0..255
  EBFE seed 262
  ```

- [x] Update `debug/ride-encoding-hypothesis.py` to mirror final registered parameters and keep B/C as non-production hypotheses.
- [x] Update `debug/RideBlockGuessPrototype` only if it is an owned/tracked artifact in the executing agent’s checkout; otherwise document that it is superseded and leave unrelated untracked work untouched.
- [x] Update documentation:
  - generalized architecture and decode ambiguity rules;
  - Jupiter hardware evidence and production status;
  - B/C explicitly pending;
  - blocks 1..4 are identity/reset metadata, not ride-encoding input.

## Agent notes / assumptions

- Notes: `RideCounterCodec` is the independent nine-bit counter codec; `EncodingSequence` owns `(zeroBlock, rotation, range)`, and `EncodingSequences.TryDecode` performs the sole registered full-structure match. Any non-match is intentionally reported as `UnknownEncodingFamily`: separating malformed from unknown would require reinstating a misleading high16 heuristic and would misclassify B/C anchors.
- Notes: `debug/RideBlockGuessPrototype/.idea/` remains untouched because it is untracked and unrelated; the tracked Python hypothesis tool is the superseding artifact.
- Assumptions:

---

# Phase 8 — Jupiter zero and reset-image confirmation gate

## Todos

- [ ] **Block reset registration until the user reports the pending hardware test.** Confirm:

  ```text
  written 1: 8C124881
  after one elevator ride expected 0: 8C124980
  blocks 5 and 6 must match
  ```

- [ ] If the result differs, stop and update the model/plan before creating or registering a reset image.
- [ ] If confirmed, create `RidesCli/Data/jupiter-0-rides.bin` as big-endian page-0 words:

  ```text
  block0 00148040
  block1 EBFE002A
  block2 F100CC5B
  block3 A5045936
  block4 A5045936
  block5 8C124980
  block6 8C124980
  block7 00000000
  ```

- [ ] Embed the reset image and make the canonical Jupiter identity profile resettable.
- [ ] Add reset image parser/existence tests and `reset --profile jupiter` / compatibility alias tests.
- [ ] Before any hardware reset, run read-only preflight and verify the intended sacrificial token.
- [ ] Hardware-smoke-test Jupiter reset through the normal safe reset path:

  ```text
  reset --profile jupiter
  verify blocks 1..6
  read -> sequence jupiter, rides 0
  ```

- [ ] Verify reset never writes blocks 0 or 7 and rollback behavior remains covered by automated tests.
- [ ] Record the final zero/reset evidence in this plan and `.docs/ride-encoding-algorithm-hypothesis-2026-07-21.md`.

## Agent notes / assumptions

- Notes:
- Assumptions:

---

# Phase 9 — Final validation and handoff for review

## Todos

- [x] Run targeted suites:

  ```bash
  dotnet test Tokens.Tests --no-restore
  dotnet test RidesCli.Tests --no-restore
  dotnet test RideCaptureCli.Tests --no-restore
  ```

- [x] Run the complete non-integration suite:

  ```bash
  dotnet test ElevatorTokens.sln --no-restore --filter 'Category!=Integration&Category!=IntegrationParity'
  ```

- [x] Run the independent exploration/collision oracle:

  ```bash
  python3 debug/ride-encoding-hypothesis.py
  ```

- [x] Run `git diff --check` and inspect `git status --short --branch`.
- [x] Confirm no unrelated files were deleted, formatted, staged, or committed.
- [x] Summarize in Agent notes:
  - final type/API design;
  - removed family/segment APIs;
  - exact registered parameters;
  - test counts/results;
  - collision results;
  - Jupiter reset hardware result;
  - any remaining risks or follow-ups.
- [x] Commit the final plan update with the final code/test/documentation chunk.
- [x] Hand off to the reviewing agent/user; do not register Candidates B/C as part of cleanup.

## Agent notes / assumptions

- Notes: Final implementation uses `RideCounterCodec`, direct `EncodingSequence(zeroBlock, rotation, minRides, maxRides)`, and exhaustive registered structural matching. Removed family/segment APIs and high16 registry lookup. Registered parameters are Mercury `CCC749CC/4`, Venus `48C74948/4`, Earth `18121218/4`, Pluto `1F12121F/4`, Mars `4EC7494E/4`, and Jupiter `8C124980/0`, all `0..500`.
- Notes: Final targeted results: Tokens 63, RidesCli 99, RideCaptureCli 28 passed. Full non-integration suite also passed (Pm3Usb 120 passed/1 skipped; TokenDumps has no matching filtered tests). The oracle passed 29 observations and collision checks for eight hypotheses over `0..500` and `0..511`.
- Notes: Follow-up hardening decodes every mirrored registered capture state even with prior history; stale EBFE labels cannot override Jupiter’s structural count, and count jumps start a new capture sequence.
- Notes: Jupiter reset remains intentionally unavailable pending the hardware `8C124881 -> 8C124980` confirmation. Candidates B/C remain unregistered. No unrelated debug paths were touched.
- Assumptions:

---

# Follow-up work explicitly outside this plan

- [ ] Hardware-test and separately register Candidate B (`zero=8B1249F0`, rotation 0) if validated.
- [ ] Hardware-test and separately register Candidate C (`zero=891249D0`, rotation 0) if validated.
- [ ] Investigate whether rotations 1,2,3,5,6,7 occur in real tokens.
- [ ] Consider a future tool for inferring `(zeroBlock, rotation)` from multiple trusted anchors while refusing single-block ambiguity.
