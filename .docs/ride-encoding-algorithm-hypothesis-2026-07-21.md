# Generalized ride-encoding algorithm hypothesis (2026-07-21)

This note follows `.docs/ride-encoding-exploration-2026-07-21.md`. It separates hardware-confirmed facts from mathematical extrapolation.

Reproduction tool:

```bash
python3 debug/ride-encoding-hypothesis.py
```

## Production status (post-generalization)

Production represents each registered sequence as `(zeroBlock, rotation, minRides, maxRides)` and performs registered-sequence structural decode rather than high16 family lookup. Mercury, Venus, Earth, Pluto, and Mars use rotation 4; **Jupiter** is registered for `0..500` with `zeroBlock=8C124980`, `rotation=0`, and canonical EBFE identity `EBFE002A-F100CC5B-A5045936-A5045936`. **Saturn** (formerly Candidate B) is registered for `0..500` with `zeroBlock=8B1249F0`, `rotation=0`, and canonical identity `23FE007B-D88CBD8A-5D04593D-5D04593D`. **Uranus** (formerly Candidate C) is registered for `0..500` with `zeroBlock=891249D0`, `rotation=0`, and canonical identity `FBFE002A-F1003C92-F5D1D766-F5D1D766`.

Jupiter, Saturn, and Uranus reset/profile support are enabled after hardware confirmed their `1 -> 0` transitions. Blocks 1..4 are identity/reset metadata, not ride-encoding inputs.

## Main finding

The production “base low16 + 128-value families” implementation is a local description of a much simpler 32-bit operation.

For every currently registered sequence, the complete block can be generated from that sequence's **zero block**, with no family table:

```text
block(rides) = zeroBlock XOR counterDelta(rides, rotation=4)
```

Candidates B/C/D use the same construction with a different byte-layout rotation:

```text
block(rides) = zeroBlock XOR counterDelta(rides, rotation=0)
```

For a 9-bit ride count `n` (0..511), rotation `s` is:

```text
r = n & 0xFF
h = (n >> 8) & 1
p = ROL8(r, s) XOR (h << s)

counterDelta bytes:
  byte 0 = F3 if bit 3 of p is set, otherwise 00
  byte 1 = h
  byte 2 = r
  byte 3 = p
```

Then XOR those four bytes with the sequence's zero block.

The high16 “families” are therefore not independent identifiers. They are the visible result of XORing duplicated counter bits into bytes 0/1. The low16 is also two representations of the same counter byte: direct in byte 2 and rotated/folded in byte 3.

## Why the production formula looked more complicated

For `rotation=4`:

```text
p = swapNibbles(r) XOR (h << 4)
```

The production `EncodeBaseLow16Only` is exactly the Mercury zero low16 (`49CC`) XOR:

```text
(r << 8) | p
```

Its four family constants encode the effects of counter bits 7 and 8 in separate 128-value windows. This works, but hides that one constant zero block and one 9-bit counter transform generate the entire sequence.

Equivalent per-counter-bit XOR masks for rotation 4 are:

```text
bit 0: 00000110
bit 1: 00000220
bit 2: 00000440
bit 3: 00000880
bit 4: 00001001
bit 5: 00002002
bit 6: 00004004
bit 7: F3008008
bit 8: 00010010
```

For `rotation=0` (B/C/D), they are:

```text
bit 0: 00000101
bit 1: 00000202
bit 2: 00000404
bit 3: F3000808
bit 4: 00001010
bit 5: 00002020
bit 6: 00004040
bit 7: 00008080
bit 8: 00010001
```

This immediately explains the observed B/C/D decrement. All three anchors have odd ride counts, so decrementing toggles counter bit 0. The rotation-0 bit-0 mask is `00000101`, producing the observed `low16 += 0xFF` in those particular states. `+0xFF` is an observation for an odd-to-even decrement, not the general encoder.

## Sequence parameters inferred from data

### Rotation 4 — registered sequences

| Sequence | Zero block | Confirmed range |
|---|---:|---:|
| Mercury | `CCC749CC` | 0..500 |
| Venus | `48C74948` | 0..500 |
| Mars | `4EC7494E` | 0..500 |
| Earth | `18121218` | 0..500 (corrected 256/384 boundaries validated 2026-07-21) |
| Pluto | `1F12121F` | 0..500 (corrected 256/384 boundaries validated 2026-07-21) |

The formula reproduces all currently implemented values in their confirmed ranges and all Mercury/Venus/Mars values through 500.

### Rotation 0 — Jupiter, Saturn, and Uranus

| Sequence/candidate | Inferred zero block | Evidence / production state |
|---|---:|---|
| Saturn (formerly Candidate B) | `8B1249F0` | 47 anchor, 46 post-ride, independent 130 dump; boundary tests and `1 -> 0` validated 2026-07-22; registered/resettable 0..500 |
| Uranus (formerly Candidate C) | `891249D0` | 107 anchor, 106 post-ride; boundary tests and `1 -> 0` validated 2026-07-22; registered/resettable 0..500 |
| Jupiter (formerly Candidate D) | `8C124980` | 57 anchor, 56 post-ride, historical 238..261 states, confirmed 1 -> 0 transition; registered/resettable 0..500 |

For rotation 0, candidate middle bytes make the count directly visible:

```text
h = block.byte1 XOR 0x12
r = block.byte2 XOR 0x49
rides = (h << 8) | r
```

This decodes the trusted anchors as 47, 107, and 57.

### Saturn hardware validation (2026-07-22)

Boundary transitions were validated on sacrificial tokens with EBFE/Jupiter identity blocks (blocks 1..4 are not ride-encoding inputs). Blocks 5 and 6 matched in each confirmed post-ride read:

```text
128 8B12C970 -> 127 7812368F  (card)
256 8B1349F1 -> 255 7812B60F  (fob)
384 8B13C971 -> 383 7813368E  (card, one ride)
  8 781241F8 ->   7 8B124EF7  (fob)
  1 8B1248F1 ->   0 8B1249F0  (card)
```

The `1 -> 0` transition confirms the Saturn zero block and reset image. Earlier synthesized Candidate-B values from the obsolete family formula are superseded by the generalized rotation-0 predictions above.

### Uranus hardware validation (2026-07-22)

Boundary transitions were validated on sacrificial tokens with EBFE/Jupiter identity blocks. Blocks 5 and 6 matched in each confirmed post-ride read:

```text
128 8912C950 -> 127 7A1236AF  (fob)
256 891349D1 -> 255 7A12B62F  (card)
384 8913C951 -> 383 7A1336AE  (fob, one ride)
  8 7A1241D8 ->   7 89124ED7  (card, one ride)
  1 891248D1 ->   0 891249D0  (fob)
```

The `1 -> 0` transition confirms the Uranus zero block and reset image. Earlier synthesized Candidate-C values from the obsolete family formula are superseded.

### Independent confirmation for Saturn (formerly Candidate B)

The unlabeled dump:

```text
93FE002A-EFEEC008-D6D1C733-D6D1C733 / 8B12CB72
```

has different blocks 1..4 from the 23FE Candidate-B anchor, but the generalized formula decodes it as:

```text
rotation = 0
rides    = 130
zero     = 8B1249F0
```

That is exactly the same zero block inferred from Saturn's trusted 47 anchor. Testing all rotations 0..7 against both points selects **rotation 0 uniquely**. Independently, each observed anchor/post-ride pair for Saturn, C, and Jupiter also selects only rotation 0. This is strong evidence that blocks 1..4 are not inputs to this ride sequence.

### Historical Candidate-D capture is structured, not random

The old EBFE CSV ride labels are offset/interrupted, but their blocks match rotation 0 exactly when decoded from bytes 1/2:

```text
8C134C84 -> 261
8C134981 -> 256
7F12B67F -> 255
8C12BE77 -> 247
8C12B970 -> 240
7F12A76E -> 238
```

All infer the same zero block `8C124980`. The apparent high16 changes at unusual boundaries are explained by `p.bit3`, not by 128-value families.

The repeated historical states (for example `8C134C84`) show that the old CSV's assigned `real_ride_count` values must not be treated as ground truth. The block itself remains consistent with the generalized counter.

## Corrected Earth/Pluto high ranges

The earlier Earth/Pluto 256+ candidates were generated by **adding** family XOR steps. The generalized algorithm shows that counter effects must be composed by **XOR** against the sequence zero block. Addition happened to give the same results for Mercury/Venus/Mars because their constants did not trigger the relevant carry/overlap; it does not for Earth/Pluto.

Correct mathematical rotation-4 predictions are:

| Rides | Earth | Pluto |
|---:|---:|---:|
| 256 | `18131208` | `1F13120F` |
| 383 | `18136DFF` | `1F136DF8` |
| 384 | `EB139200` | `EC139207` |
| 500 | `EB13E647` | `EC13E640` |

These differ from the previously rejected/tested values such as Earth `18131228` and Pluto `1F13922F`. Consequently, those hardware failures did **not** reject the generalized 256+ hypothesis.

### Earth hardware validation (2026-07-21)

Both corrected starts were accepted by the elevator and decremented exactly as predicted, despite using token identities unrelated to the original D3 Earth profile:

```text
Card, identity 9BFE...: 256 18131208 -> 255 EB12EDE7
Fob,  identity EBFE...: 384 EB139200 -> 383 18136DFF
```

Blocks 5 and 6 matched in both post-ride reads. Earth is therefore registered through 500 with corrected high families `1813/5BC4` and `EB13/DBCC`.

### Pluto hardware validation (2026-07-21)

Both corrected starts were accepted by the elevator and decremented exactly as predicted:

```text
Card, identity 9BFE...: 256 1F13120F -> 255 EC12EDE0
Fob,  identity EBFE...: 384 EC139207 -> 383 1F136DF8
```

Blocks 5 and 6 matched in both post-ride reads. Pluto is therefore registered through 500 with corrected high families `1F13/5BC3` and `EC13/DBCB`.

## Corrected candidate predictions

The current `baseLow XOR inferred-xor` synthesized values use rotation 4 and are expected to fail for rotation-0 candidates. Correct predictions include:

| Candidate / sequence | 0 | 1 | 128 | 255 | 256 | 500 |
|---|---:|---:|---:|---:|---:|---:|
| Saturn (registered) | `8B1249F0` | `8B1248F1` | `8B12C970` | `7812B60F` | `8B1349F1` | `8B13BD05` |
| Uranus (registered) | `891249D0` | `891248D1` | `8912C950` | `7A12B62F` | `891349D1` | `8913BD25` |
| Jupiter (registered) | `8C124980` | `8C124881` | `8C12C900` | `7F12B67F` | `8C134981` | `8C13BD75` |

Notably, Saturn's predicted 130 block is `8B12CB72`, exactly matching the independent unlabeled dump. Jupiter's predicted 256/255 blocks exactly match the historical EBFE states.

Saturn and Uranus boundary tests listed above are hardware-validated.

## Collision results

`debug/ride-encoding-hypothesis.py` checks:

- all eight registered sequences (five rotation-4 plus Jupiter, Saturn, and Uranus),
- every value 0..500,
- and the complete 9-bit range 0..511.

Results:

```text
8 sequences × 501 values = 4008 distinct blocks (0..500)
8 sequences × 512 values = 4096 distinct blocks (0..511)
```

There are no self-collisions and no cross-sequence collisions among these hypotheses.

For any fixed zero block and rotation, self-collision freedom also follows directly from bytes 1 and 2 of `counterDelta`: together they contain the complete 9-bit counter.

This does **not** prove that every arbitrary zero block/rotation pair is globally collision-free against every possible unknown sequence. Unknown sequence registration should still run the enumerated collision check.

## Detection implications

A production sequence can be represented by:

```text
(zeroBlock, rotation, minRides, maxRides)
```

rather than four `(high16, xor, baseOffset)` families.

Given a trusted `(rides, block)` anchor and candidate rotation:

```text
zeroBlock = block XOR counterDelta(rides, rotation)
```

Two anchors from the same sequence can identify rotation: choose the rotation for which both infer the same zero block. This is how B's 47 and 130 points uniquely select rotation 0.

A single arbitrary block cannot, in general, uniquely determine both rotation and zero block without constraints or a registry: every rotation can infer some zero block. Practical detection should therefore:

1. match registered `(zeroBlock, rotation)` sequences by attempting decode/round-trip;
2. use multiple trusted points to infer a new rotation/base;
3. reject ambiguous matches;
4. run 0..500/511 self- and cross-collision checks before registration.

Blocks 5/6 are sufficient once the sequence parameters are known. Current evidence does not require blocks 1..4 as an encoding input.

## Confidence and open questions

High confidence:

- rotation 4 exactly replaces all registered family arithmetic;
- rotation 0 exactly explains every trusted Saturn/C/Jupiter anchor and post-ride;
- Saturn's separate `8B12CB72` dump and Jupiter's historical data independently corroborate rotation 0;
- Saturn and Uranus boundary transitions and `1 -> 0` are hardware-validated; reset images are known; hardware `reset --profile uranus` smoke test passed 2026-07-22 on sacrificial Saturn card;
- previous Earth/Pluto 256+ tests did not test the constant-zero-block XOR predictions.

Still hypotheses:

- corrected Earth and Pluto high ranges have boundary hardware validation; interior 256+ values are extrapolated by the same verified family rule;
- rotations 1,2,3,5,6,7 are mathematically natural but currently have no identified captures;
- the rule by which an elevator decides that a `(zeroBlock, rotation)` is an allowed sequence is unknown.
