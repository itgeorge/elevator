# Ride encoding exploration handoff (2026-07-21)

This document summarizes hardware exploration of unknown ride-encoding candidates from recent `RidesCli` dumps, what was registered in production code, and open questions for follow-up investigation.

**Audience:** a new agent investigating the general encoding algorithm from known families and dumps, while the hardware team continues capture work on candidates B/C/D.

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
| XOR-step 256 | `1F13922F` | unchanged | ❌ Rejected |
| Minus-one 256 | `1F11122F` | `1F12121F` (0), double-beep | ⚠️ Accepted as zero (Earth-like) |

**Updated conclusion:** Pluto is Earth-class / rotation-4. The original failed 256 tests used incorrect addition-derived or minus-one values. Corrected XOR-derived values validated through the 256 and 384 boundaries, so Pluto is now committed as a 0..500 production sequence.

---

## Candidates B/C pending; Candidate D superseded by Jupiter

> **Current production status:** the later generalized codec registered Candidate D as **Jupiter** (`zeroBlock=8C124980`, rotation 0, range 0..500) and recognizes its canonical EBFE identity. Candidate B/C remain unregistered. Jupiter reset support remains gated on a pending hardware `1 -> 0` confirmation; this historical section records the evidence that led to the generalized model.

All three share a visual pattern distinct from Pluto:

- `high16` low byte is **`12`** (e.g. `7812`, `7A12`, `7F12`) — same *shape* as Earth/Pluto `xx12` families
- `xor` is **tiny** (`00E1`, `00C1`, `00E6`) — Mars/Venus-*like*, not Earth-like (`5BDx`)
- **Captured anchor blocks work** on the elevator
- **Model-synthesized** values at rides 1 and 128 are **rejected**
- **Second-family guesses** (Earth-style `high16 ^ F300`, `xor + 8008`) are **rejected**
- Post-ride blocks follow a **`low16 += 0xFF`** decrement rule, **not** the production `baseLow` step

### Candidate B — 23FE

| Item | Value |
|------|-------|
| Profile | `23FE007B-D88CBD8A-5D04593D-5D04593D` |
| Source dump | `…--rides-47.bin` |
| Observed anchor | rides **47** → `781266DF` |
| Inferred family | `7812/00E1` |
| Predicted 128 (untested family) | `8B12/80E9` → `8B12C925` |

| Test | Write | Post-ride | Result |
|------|-------|-----------|--------|
| Synthesized rides=1 | `7812483D` | unchanged | ❌ |
| Synthesized rides=128 | `8B12C925` | unchanged | ❌ |
| Captured rides=47 | `781266DF` | `781267DE` | ✅ Ride accepted (normal beep) |

Decrement analysis:

```text
Predicted (model): 781266DF → 781267CF  (rides 47 → 46)
Observed:          781266DF → 781267DE
low16 step:        0x66DF → 0x67DE = +0xFF
pred ⊕ obs (low16): 0x0011
```

### Candidate C — FBFE

| Item | Value |
|------|-------|
| Profile | `FBFE002A-F1003C92-F5D1D766-F5D1D766` |
| Source dump | `…--rides-107.bin` |
| Observed anchor | rides **107** → `7A1222BB` |
| Inferred family | `7A12/00C1` |
| Predicted 128 | `8912/80C9` → `8912C905` |

| Test | Write | Post-ride | Result |
|------|-------|-----------|--------|
| Captured rides=107 | `7A1222BB` | `7A1223BA` | ✅ |
| Synthesized rides=128 (card) | `8912C905` | unchanged | ❌ |

Decrement: `0x22BB → 0x23BA` (+`0xFF`); model predicted `7A1223AB`; pred ⊕ obs low16 = `0x0011`.

### Candidate D — EBFE

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

**Historical note:** older EBFE captures at rides 233..260 looked inconsistent. The 2026-07-21 low-range anchor at 57 behaved like B/C; high-range behavior remains untrusted.

---

## Cross-candidate comparison

| Class | Examples | xor | Decrement | 128 family | 256+ |
|-------|----------|-----|-----------|------------|------|
| C7/C6 (Mercury/Venus/Mars) | 48C7, BBC7, 4EC7 | 008x–809x | Matches `baseLow` model | Validated | XOR-step validated |
| Earth / Pluto | 1812/EB12, 1F12/EC12 | 5BDx/DBDx | Matches `baseLow` model | Validated | Resets to zero |
| **Unknown xx12 / tiny-xor** | **7812, 7A12, 7F12** | **00Cx–00Ex** | **`low16 += 0xFF`** | **Rejected** | **Not tested** |

### The `+0xFF` decrement hypothesis

For B, C, and D, every successful single-ride decrement observed so far satisfies:

```text
post.low16 = (pre.low16 + 0xFF) mod 2^16
```

while the production encoder predicts a different adjacent block (always `pred ⊕ obs` low16 = **`0x0011`** for the three cases above).

**Implication:** these tokens may use a different encoding path than `baseLow(m) XOR xor`, or the elevator decrements with a rule not yet captured in `TokenBlockUtils`. Single-point xor inference can still find the anchor block but fails to generalize.

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

### Hardware / capture (human + RideCaptureCli)

Priority: **full decrement captures** for B, C, D from validated anchors down toward 0.

| Candidate | Start block | Token on hand |
|-----------|-------------|---------------|
| B (23FE) | `781267DE` (post-ride from 47; treat as new baseline) | fob was last on EBFE profile |
| C (FBFE) | `7A1223BA` (post-ride from 107) | fob |
| D (EBFE) | `7F1271B8` (post-ride from 57) | fob |

Use `RideCaptureCli` Enter scans; avoid writing synthesized ride values except captured anchors. Record signal strength and mirror blocks 5/6.

### Algorithm investigation (new agent)

Hypotheses worth testing against **all** registered families and dumps:

1. **`+0xFF` decrement class** — Is there a closed-form mapping between `baseLow(m)` blocks and the observed `+0xFF` chain? Why is pred ⊕ obs always `0x0011` for B/C/D?
2. **Family taxonomy** — C7/C6 vs xx12/5BDx (Earth/Pluto) vs xx12/tiny-xor (B/C/D). Does high16 encode a class byte (`12`, `C7`, `C6`) independent of xor?
3. **Single-point inference limits** — When does `xor = low16 XOR baseLow(knownRides)` produce decoy families that only work at one ride?
4. **Second-family prediction** — Earth-style `^F300` / `+8008` works for Pluto but fails for B/C/D at 128. Is there an alternate second-family rule for tiny-xor class, or are they capped below 128?
5. **Cross-dump clustering** — Compare `high16` across all `elevator-t55xx-*.bin` files; flag shared high16 across unrelated profiles (e.g. `8B12` on 93FE dump vs B's prediction).

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
