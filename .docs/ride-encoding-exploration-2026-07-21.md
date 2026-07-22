# Ride encoding exploration handoff (2026-07-21)

This document summarizes hardware exploration of unknown ride-encoding candidates from recent `RidesCli` dumps, what was registered in production code, and open questions for follow-up investigation.

**Audience:** a new agent investigating the general encoding algorithm from known families and dumps.

> **Update 2026-07-22:** Candidate B is registered as **Saturn** and Candidate C as **Uranus** (both rotation 0, range 0..500). Boundary transitions and `1 -> 0` were hardware-validated; reset images `saturn-0-rides.bin` and `uranus-0-rides.bin` are implemented.

> **Update later on 2026-07-21:** the generalized constant-zero-block XOR algorithm in
> `.docs/ride-encoding-algorithm-hypothesis-2026-07-21.md` identified corrected Earth high values.
> Hardware validated Earth `256 18131208 -> 255 EB12EDE7` and `384 EB139200 -> 383 18136DFF`,
> then Pluto `256 1F13120F -> 255 EC12EDE0` and `384 EC139207 -> 383 1F136DF8`.
> Earth and Pluto are now registered through 500. Earlier rejection notes below concern incorrect addition-derived values.

---

## Historical family model

The family/segment formula below is retained only to explain pre-generalization captures. Production now uses the constant-zero-block generalized model documented in `.docs/ride-encoding-algorithm-hypothesis-2026-07-21.md`; `EncodingSequenceSegment`, family registries, and high16 lookup have been removed.

```text
m      = rides - family.baseOffset
low16  = baseLow(m) XOR family.xor
block  = family.high16 << 16 | low16
```

A single trusted `(ride count, block5/6)` point can still generate an exploration hypothesis, but production never infers a sequence from one arbitrary block. It requires an exact structural match against a registered `(zeroBlock, rotation, range)` sequence.

---

## Registered sequences (production)

| Name | Range | Families (high16/xor) | Identity example | Notes |
|------|-------|------------------------|------------------|-------|
| Mercury | 0..500 | CCC7/0000, 3FC7/8008, CCC6/0010, 3FC6/8018 | 9BFE0062-… | XOR-step 256+ validated |
| Venus | 0..500 | 48C7/0084, BBC7/808C, 48C6/0094, BBC6/809C | 43FE0062-… | XOR-step 256+ validated |
| Mars | 0..500 | 4EC7/0082, BDC7/808A, 4EC6/0092, BDC6/809A | C3FE0031-… | XOR-step 256+ validated |
| Earth | 0..500 | 1812/5BD4, EB12/DBDC, 1813/5BC4, EB13/DBCC | D3FE005D-… | Corrected XOR-derived 256/384 boundaries validated |
| **Pluto** | **0..500** | **1F12/5BD3, EC12/DBDB, 1F13/5BC3, EC13/DBCB** | **83FE002A-F100C064-A3045930-A3045930** | Corrected 256/384 boundaries validated |
| **Jupiter** | **0..500** | **rotation 0, zeroBlock 8C124980** | **EBFE002A-F100CC5B-A5045936-A5045936** | Candidate D registered; reset enabled |
| **Saturn** | **0..500** | **rotation 0, zeroBlock 8B1249F0** | **23FE007B-D88CBD8A-5D04593D-5D04593D** | Candidate B registered; reset enabled |
| **Uranus** | **0..500** | **rotation 0, zeroBlock 891249D0** | **FBFE002A-F1003C92-F5D1D766-F5D1D766** | Candidate C registered; reset enabled |

Pluto reset image: `RidesCli/Data/pluto-0-rides.bin`. Profile name: `pluto`.

---

## Candidate A → Pluto (registered)

**Source dump:** `elevator-t55xx-…-83FE002A-…-1F12203C-…--rides-50.bin`

| Item | Value |
|------|-------|
| Observed anchor | rides **50** → `1F12203C` |
| Inferred low family | `1F12/5BD3` |
| Inferred 128..255 (Earth-style) | `EC12/DBDB` |

### Hardware validation (2026-07-21)

| Test | Write | Post-ride | Result |
|------|-------|-----------|--------|
| Low decrement | 50 `1F12203C` | `1F12230C` (49) | ✅ Matches model |
| Second family decrement | 128 `EC129217` | `1F126DE8` (127) | ✅ Matches model |
| Old incorrect 256 candidate | `1F13922F` | unchanged | ❌ Rejected; superseded by corrected `1F13120F` |
| Minus-one 256 | `1F11122F` | `1F12121F` (0), double-beep | ⚠️ Accepted as zero; not the valid high-range encoding |

**Updated conclusion:** Pluto is Earth-class / rotation-4. The original failed 256 tests used incorrect addition-derived or minus-one values. Corrected XOR-derived values validated through the 256 and 384 boundaries, so Pluto is now committed as a 0..500 production sequence.

---

## Candidate B → Saturn (registered)

> **Current production status:** Candidate B is registered as **Saturn** with `zeroBlock=8B1249F0`, rotation 0, range 0..500. Candidate C is now also registered as **Uranus**. Jupiter (formerly Candidate D) was registered earlier.

| Item | Value |
|------|-------|
| Profile | `23FE007B-D88CBD8A-5D04593D-5D04593D` |
| Source dump | `…--rides-47.bin` |
| Observed anchor | rides **47** → `781266DF` |
| Zero block | `8B1249F0` |

### Hardware validation

| Test | Write | Post-ride | Result |
|------|-------|-----------|--------|
| Historical decrement | 47 `781266DF` | `781267DE` | ✅ |
| Independent dump (130 rides) | `8B12CB72` | — | ✅ decodes structurally |
| 128 boundary (card) | `8B12C970` | `7812368F` | ✅ blocks 5/6 matched |
| 256 boundary (fob) | `8B1349F1` | `7812B60F` | ✅ blocks 5/6 matched |
| 384 boundary (card) | `8B13C971` | `7813368E` | ✅ one ride, blocks 5/6 matched |
| 8 boundary (fob) | `781241F8` | `8B124EF7` | ✅ blocks 5/6 matched |
| Zero transition (card) | 1 `8B1248F1` | 0 `8B1249F0` | ✅ blocks 5/6 matched |

Earlier synthesized values from the obsolete family formula (`7812483D` at rides=1, `8B12C925` at rides=128) are superseded by the generalized rotation-0 model.

Reset image: `RidesCli/Data/saturn-0-rides.bin`. Profile name: `saturn`. Hardware `reset --profile saturn` smoke test passed 2026-07-22 on sacrificial EBFE card; post-reset read reported `sequence: saturn`, rides 0, with blocks 1..6 matching the reset image.

## Candidate C → Uranus (registered)

| Item | Value |
|------|-------|
| Profile | `FBFE002A-F1003C92-F5D1D766-F5D1D766` |
| Source dump | `…--rides-107.bin` |
| Observed anchor | rides **107** → `7A1222BB` |
| Zero block | `891249D0` |

### Hardware validation

| Test | Write | Post-ride | Result |
|------|-------|-----------|--------|
| Historical decrement | 107 `7A1222BB` | `7A1223BA` | ✅ |
| 128 boundary (fob) | `8912C950` | `7A1236AF` | ✅ blocks 5/6 matched |
| 256 boundary (card) | `891349D1` | `7A12B62F` | ✅ blocks 5/6 matched |
| 384 boundary (fob) | `8913C951` | `7A1336AE` | ✅ one ride, blocks 5/6 matched |
| 8 boundary (card) | `7A1241D8` | `89124ED7` | ✅ one ride, blocks 5/6 matched |
| Zero transition (fob) | 1 `891248D1` | 0 `891249D0` | ✅ blocks 5/6 matched |

Earlier synthesized values from the obsolete family formula (`8912C905` at rides=128) are superseded by the generalized rotation-0 model.

Reset image: `RidesCli/Data/uranus-0-rides.bin`. Profile name: `uranus`. Hardware `reset --profile uranus` smoke test passed 2026-07-22 on sacrificial Saturn card (pre-reset 500 rides); post-reset read reported `sequence: uranus`, rides 0, with blocks 1..6 matching the reset image.

### Candidate D — EBFE (registered as Jupiter)

See Jupiter in the registered sequences table above. Historical exploration notes:

| Item | Value |
|------|-------|
| Profile | `EBFE002A-F100CC5B-A5045936-A5045936` |
| Source dump | `…--rides-57.bin` |
| Observed anchor | rides **57** → `7F1270B9` |
| Inferred family | `7F12/00E6` |
| Predicted 128 | `8C12/80EE` → `8C12C922` |

| Test | Write | Post-ride | Result |
|------|-------|-----------|--------|
| Captured rides=57 (fob) | `7F1270B9` | `7F1271B8` | ✅ |
| Synthesized rides=128 (card) | `8C12C922` | unchanged (confirmed read 2026-07-21) | ❌ |

Decrement: `0x70B9 → 0x71B8` (+`0xFF`); model predicted `7F1271A9`; pred ⊕ obs low16 = `0x0011`.

**Historical note:** older EBFE captures at rides 233..260 looked inconsistent under the old family model. The generalized rotation-0 model later explained those states and Jupiter is now registered/resettable for `0..500`.

---

## Cross-candidate comparison

| Class | Examples | xor | Decrement | 128 family | 256+ |
|-------|----------|-----|-----------|------------|------|
| C7/C6 (Mercury/Venus/Mars) | 48C7, BBC7, 4EC7 | 008x–809x | Matches `baseLow` model | Validated | XOR-step validated |
| Earth / Pluto | 1812/EB12, 1F12/EC12 | 5BDx/DBDx | Matches `baseLow` locally; generalized as rotation 4 | Validated | Corrected XOR-derived values validated through 500; earlier reset-to-zero tests used wrong addition/minus-one candidates |
| Jupiter / Saturn / Uranus | 7F12/8C12/8C13, 7812/8B12/8B13, 7A12/8912/8913 | rotation 0 zero-block codec | `+0xFF` was the local odd-to-even decrement symptom | Validated | Registered/resettable through 500 |

### The `+0xFF` decrement hypothesis

For B, C, and D, every successful single-ride decrement observed so far satisfies:

```text
post.low16 = (pre.low16 + 0xFF) mod 2^16
```

while the production encoder predicts a different adjacent block (always `pred ⊕ obs` low16 = **`0x0011`** for the three cases above).

**Superseding implication:** these observations were the clue for the rotation-0 codec. Single-point `baseLow` xor inference can find an anchor but fails to generalize for Jupiter/Saturn/Uranus; production now uses registered `(zeroBlock, rotation, range)` structural decoding instead.

---

## Unlabeled dumps (no trusted ride count)

Do not infer xor without CLI I/O or a known ride label:

| Profile (blocks 1–4) | b5/b6 | Dump file pattern |
|----------------------|-------|-------------------|
| 6BFE0031-20C6006C-86044936-86044936 | `4DC6548C` | rides-UNKNOWN |
| 1BFE002A-F1003D86-D5D1D713-D5D1D713 | `A4124B77` | rides-UNKNOWN |
| 6BFE002A-F100D40F-32D159A1-32D159A1 | `06C73709` | rides-UNKNOWN |
| 93FE002A-EFEEC008-D6D1C733-D6D1C733 | `8B12CB72` | rides-UNKNOWN |

**Note:** `93FE` block `8B12CB72` uses high16 `8B12` — the same second-family high16 predicted for candidate B (`23FE`). Worth investigating once B is better understood.

---

## Repo artifacts

| Artifact | Location |
|----------|----------|
| Page-0 dumps | `elevator-t55xx-…--rides-*.bin` (repo root) |
| Pluto registration | `Tokens/EncodingSequence.cs`, `TokenIdentityProfile.cs` |
| Pluto tests | `Tokens.Tests/TokenBlockUtilsTest.cs`, `RidesCli.Tests/`, `RideCaptureCli.Tests/` |
| Earth high-range postmortem | `.plans/d3-earth-sequence-plan.md` |
| Block-only guesser (C7/C6 only) | `debug/RideBlockGuessPrototype/` |

---

## Recommended next work

### Current status

The old recommended full decrement captures for B/C/D are no longer necessary for production registration: B is Saturn, C is Uranus, and D is Jupiter, all registered/resettable through `0..500` with rotation `0`.

### Remaining useful follow-up

1. **Unlabeled dumps** — Revisit the `rides-UNKNOWN` dumps below using the generalized codec; some may now decode as registered sequences or indicate another zeroBlock/rotation candidate.
2. **Rotation search** — Investigate whether rotations other than `0` and `4` occur in real tokens.
3. **Inference tooling** — Build a tool that requires multiple trusted anchors to infer `(zeroBlock, rotation)` and refuses ambiguous single-block guesses.
4. **Cross-dump clustering** — Compare `elevator-t55xx-*.bin` blocks against all registered sequences, then cluster remaining unknowns by possible rotation/zeroBlock hypotheses.

Useful tooling:

```bash
# Encode a registered sequence value
dotnet run --project debug/EncodeRideBlock -- pluto 128

# Read-only PM3 preflight
printf 'connect\ntune\nread 0\nread 1\nread 2\nread 3\nread 4\nread 5\nread 6\nread 7\nexit\n' | dotnet run --project Pm3Cli

# Write ride mirror only (blocks 5/6)
printf 'connect\nwrite 5 <hex>\nwrite 6 <hex>\nread 5\nread 6\nexit\n' | dotnet run --project Pm3Cli
```

**Safety:** never write block 0 or block 7 via `Pm3Cli`. For profile resets of registered sequences use `RidesCli reset --sequence <name>`.

---

## Session changelog

| Date | Action |
|------|--------|
| 2026-07-21 | Validated and registered **Pluto** (83FE, initially 0..255) |
| 2026-07-21 | Tested candidates B/C/D: anchors work; 128 family fails; `+0xFF` decrement observed |
| 2026-07-21 | Confirmed EBFE card unchanged at `8C12C922` after failed 128 test |
| 2026-07-21 | Derived generalized rotation/XOR algorithm; corrected Earth high values |
| 2026-07-21 | Validated Earth `256 -> 255` and `384 -> 383`; extended production Earth to 500 |
| 2026-07-21 | Validated Pluto `256 -> 255` and `384 -> 383`; extended production Pluto to 500 |
