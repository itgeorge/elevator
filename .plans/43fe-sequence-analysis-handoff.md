# Handoff: Analyze 43FE0062 ride-state sequence for TokenBlockUtils

You are taking over analysis of elevator token ride-count encodings in `/Users/itgeorge/elevator`.

## Status (2026-07-12)

**Analysis complete. Venus sequence algorithm confirmed through elevator-reader testing.**

| Range | Family | Status |
|-------|--------|--------|
| 0..127 | `48C7` / xor `0084` | Captured (0..180 capture) + elevator confirmed |
| 128..255 | `BBC7` / xor `808C` | Captured (128..180) + predicted 181..255 elevator-confirmed |
| 256..383 | `48C6` / xor `0094` | Predicted from Mercury; elevator-confirmed (256→255 transition) |
| 384..500 | `BBC6` / xor `809C` | Predicted from Mercury; elevator-confirmed (500→499, 384→383 transitions) |

**Next step:** wire the full Venus sequence (all four families, 0..500) into `RidesCli`. See [RidesCli integration plan](#ridescli-integration-plan-pending) below.

**Follow-up:** investigate unreliable page-0 block writes (especially block 2) on the native USB PM3 executor during `reset --sequence venus`. See [Known issues](#known-issues-follow-up).

---

## Goal

Analyze the newly completed capture for token:

```text
43FE0062-5BA494A3-D6D1C733-D6D1C733
```

and prepare/implement a robust `TokenBlockUtils` algorithm for this sequence/family.

The Venus sequence is now understood for the full `0..500` range. Implementation in `Tokens/` covers families `48C7` and `BBC7` only; `48C6` and `BBC6` are algorithmically confirmed but not yet registered in code.

## Key data files

Primary scan data:

```text
ride-capture-data/captures.csv
ride-capture-data/dumps/**
```

Note: `ride-capture-data/` is gitignored but exists locally and contains the latest live captures.

Useful code/tests/docs:

```text
Tokens/TokenBlockUtils.cs
Tokens/EncodingSequence.cs
Tokens.Tests/TokenBlockUtilsTest.cs
RidesCli/RideBlockResolver.cs
RideCaptureCli/README.md
ride-capture-tool-plan.md
```

---

## Venus sequence algorithm (confirmed)

Venus uses the same `baseLow` + XOR-family model as Mercury. Each 128-ride window (except the last, which ends at 500) maps to one family defined by `(high16, xorConst, baseOffset)`.

### Confirmed families

```text
Rides 0..127:    high16 = 0x48C7,  xor = 0x0084,  base = 0
Rides 128..255:  high16 = 0xBBC7,  xor = 0x808C,  base = 128
Rides 256..383:  high16 = 0x48C6,  xor = 0x0094,  base = 256   [predicted, elevator-confirmed]
Rides 384..500:  high16 = 0xBBC6,  xor = 0x809C,  base = 384   [predicted, elevator-confirmed]
```

Encoding:

```text
block = (high16 << 16) | (baseLow(rides - baseOffset) XOR xorConst)
```

Decoding inverts the XOR, then runs the existing `DecodeFromBaseBlock` base algorithm, then adds `baseOffset`.

### Relationship to Mercury

Venus families are a fixed offset from Mercury for every segment:

```text
venus.high16 = mercury.high16 XOR 0x8400
venus.xor    = mercury.xor    + 0x0084   (16-bit wrap)
```

Mercury reference:

```text
0..127:   CCC7 / 0000 / 0
128..255: 3FC7 / 8008 / 128
256..383: CCC6 / 0010 / 256
384..500: 3FC6 / 8018 / 384
```

Mercury internal step pattern (high16 toggles `F300`/`F301`, xor toggles `8008`/`8018`) applies equally to Venus.

### Border ride reference blocks

```text
127 -> 48C736BF   (48C7, last of low segment)
128 -> BBC7C940   (BBC7, first of mid segment)
255 -> BBC7B6B7   (BBC7, last before 48C6)
256 -> 48C64958   (48C6, first of 256..383)
383 -> 48C636AF   (48C6, last before BBC6)
384 -> BBC6C950   (BBC6, first of 384..500)
500 -> BBC6BD17   (BBC6, top of range)
```

---

## Elevator-reader validation (2026-07-11 / 2026-07-12)

All tests on token `43FE0062-5BA494A3-D6D1C733-D6D1C733` unless noted.

| Test | Written | After elevator | Result |
|------|---------|----------------|--------|
| Predicted 255 | `BBC7B6B7` | accepted | Pass |
| Predicted 500 | `BBC6BD17` | → `BBC6BA67` (499) | Pass |
| 384→383 transition | `BBC6C950` | → `48C636AF` (383) | Pass |
| 256→255 transition | `48C64958` | → `BBC7B6B7` (255) | Pass |

The 127/128 transition was validated during the original capture descent from ~180 to 0.

Writes for hardware tests used `Pm3Cli write <block> <hex>` on ride mirror blocks 5 and 6 only. `RidesCli set` was used where Venus sequence already covered the ride count (0..255).

---

## Capture data status (0..180)

As of 2026-07-11 after a full elevator ride capture:

- token: `43FE0062-5BA494A3-D6D1C733-D6D1C733`
- rows in `captures.csv`: `187`
- real ride counts covered: every value from `180` down to `0`
- statuses: `181 Ok`, `6 NoChange`
- mirror mismatches: `0`
- dump-linked rows: `185`
- final state at real `0`: `48C74948 / 48C74948`

Quick verification command:

```bash
python3 - <<'PY'
import csv, collections
rows=list(csv.DictReader(open('ride-capture-data/captures.csv')))
t='43FE0062-5BA494A3-D6D1C733-D6D1C733'
rs=[r for r in rows if r['token_id']==t]
print('rows', len(rs))
print('real minmax', min(int(r['real_ride_count']) for r in rs if r['real_ride_count']), max(int(r['real_ride_count']) for r in rs if r['real_ride_count']))
print('families', collections.Counter(r['block5'][:4] for r in rs if r['real_ride_count']))
PY
```

### Observed families in capture (0..180)

```text
real 180..128: BBC7....   (53 observed values)
real 127..0:   48C7....   (128 observed values)
```

Transition samples:

```text
real 180 -> BBC7FD03
real 128 -> BBC7C940
real 127 -> 48C736BF
real 0   -> 48C74948
```

---

## Code implementation status

### Done in `Tokens/`

- `Family48C7_0To127` and `FamilyBBC7_128To255` registered in `TokenBlockUtils.Families`
- `EncodingSequences.Venus` with segments 0..127 and 128..255
- Tests for captured 0..180, predicted 181..255, round-trip 0..255
- `EncodeByFamily` validates ride value is within family range
- `EncodingSequence` segment/list model (supports partial sequences)

### Not yet done

- Register `Family48C6_256To383` and `FamilyBBC6_384To500` in `TokenBlockUtils`
- Extend `EncodingSequences.Venus` to 256..383 and 384..500 segments
- Add unit tests for predicted high-range encodings (mirror `Table43FE_181To255_Predicted` style)
- Full `RidesCli` integration (see below)

---

## Known issues (follow-up)

### Unreliable page-0 writes on native USB executor

After migration to native USB integration, `RidesCli reset --sequence venus` (`WriteAndVerifyPage0BlocksAsync` for blocks 1..6) frequently fails at **block 2** with `Failed to write block 2`. Symptoms observed 2026-07-11/12:

- Block 1 (`43FE0062`) often writes; block 2 (`5BA494A3`) fails
- Partial reset leaves token in inconsistent state (mixed UID blocks)
- Ride mirror blocks 5/6 can usually still be written via `Pm3Cli write`
- Same physical tokens worked reliably before USB migration; process-based PM3 integration suspected to work fine
- Intermittent mirror mismatch on read (blocks 5 ≠ 6) after flaky writes

**Action:** compare native vs process executor write paths for T55 page-0 writes; add integration test or repro script; consider retry/verify policy for reset.

---

## RidesCli integration plan (pending)

Wire the confirmed full-range Venus sequence into `RidesCli` so `read`, `set`, `add`, and `reset` work without manual `Pm3Cli` hex writes.

1. **Add Venus families to `TokenBlockUtils.Families`**
   - `Family48C6_256To383` (`0x48C6`, `0x0094`, 256)
   - `FamilyBBC6_384To500` (`0xBBC6`, `0x809C`, 384)
   - Register both in `AllFamilies` / `FamilyByHigh16`

2. **Extend `EncodingSequences.Venus`**
   - Add segments 256..383 and 384..500
   - Venus becomes full 0..500 mirror of Mercury segment structure

3. **Tests (`Tokens.Tests`)**
   - Encode/decode round-trip 256..500
   - Border values: 255, 256, 383, 384, 500
   - Predicted block table tests for `48C6` and `BBC6` ranges
   - `TryGetSequenceFromBlock` recognizes `48C6`/`BBC6` as Venus

4. **Tests (`RidesCli.Tests`)**
   - `RideBlockResolver` decodes Venus high-range blocks
   - `set 500` / `set 256` on Venus token uses correct family (not Mercury)
   - `reset --sequence venus` still loads reset image and writes 0 correctly
   - Preserve existing `EncodePreservingSequence` behavior for Venus tokens

5. **`RideBlockResolver`**
   - Should work unchanged once families decode (max rides still 500)
   - Verify no conflict when both Mercury and Venus share similar ride counts but different high16

6. **Optional: reset image**
   - Confirm `venus-0-rides.bin` UID blocks match target token; no change expected unless we want a 500-rides Venus default image

7. **Run test suite**

   ```bash
   dotnet test Tokens.Tests
   dotnet test RidesCli.Tests
   ```

---

## Context: other recorded known sequences

### D3 sequence

Token: `D3FE005D-522BC69D-650432F5-650432F5` — partial capture 23..0, family `1812` (future work).

### EBFE sequence

Token: `EBFE002A-F100CC5B-A5045936-A5045936` — unsolved, future work.

---

## Safety notes

- Do not write block 0 or block 7.
- App tooling forbids these already.
- For hardware testing, write only mirrored ride blocks 5 and 6 and verify reads.
- Prefer `RidesCli`/`Pm3Cli` over raw pm3 commands.

## Git notes

Relevant commits on branch:

```text
786e485 Refactor EncodingSequence to use ride-range segments
84cffe0 Validate EncodeByFamily rejects out-of-family ride values
1dd6086 Add EncodingSequence model with mercury/venus families and explicit writes
```

Venus high-range families and full RidesCli wiring are not yet committed.
