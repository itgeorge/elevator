# D3 Earth Sequence Candidate Plan

## How agents should use this plan

Read this entire file before making changes. Find the next `[ ]` TODO and work on it; if new relevant work is discovered, add TODOs under the current phase before continuing. Keep each chunk coherent and testable, mark completed items by changing `[ ]` to `[x]`, and document assumptions/deviations in the notes. Commit plan updates in the same commit as corresponding code/test changes so implementation and handoff state stays aligned.

---

## What this work is

Prepare and validate the D3 token sequence candidate, friendly name `earth`, for safe reset and incremental family registration.

Primary token/capture identity:

```text
D3FE005D-522BC69D-650432F5-650432F5
```

Recorded Earth zero image:

```text
block0 00148040
block1 D3FE005D
block2 522BC69D
block3 650432F5
block4 650432F5
block5 18121218
block6 18121218
block7 00000000
```

## Current status (updated 2026-07-21)

**Superseding result:** Earth is now implemented for `0..500`. The generalized constant-zero-block XOR model corrected the old addition-derived high constants to `1813/5BC4` and `EB13/DBCC`. Hardware validated `256 18131208 -> 255 EB12EDE7` and `384 EB139200 -> 383 18136DFF` on 2026-07-21.

The table below records the previous 2026-07-12 state and the failed incorrect candidates for historical context:

| Range | Family | Evidence | Code status |
|---|---|---|---|
| `0..127` | `1812` / xor `5BD4` / base `0` | Captured `0..23`; reset zero verified; `128 -> 127` elevator transition verified | Registered |
| `128..255` | `EB12` / xor `DBDC` / base `128` | `128 -> 127` transition verified; `255 -> 254` in-family decrement verified | Registered |
| `256..383` | old predicted `1813` / xor `5BE4` / base `256`; visual alternative `1811` / xor `5BE4` / base `256` | Old prediction was rejected/no-change; visual alternative was accepted as low/empty and reset to zero | Not registered; treated as unsupported |
| `384..500` | old predicted `EB13` / xor `DBEC` / base `384`; visual alternative `EB11` / xor `DBEC` / base `384` | Visual alternative `384` was accepted as low/empty and reset to zero | Not registered; treated as unsupported |

Confirmed hardware reads after elevator use:

```text
start 128 -> wrote EB129210; after ride read 18126DEF (127)  PASS
start 255 -> wrote EB12EDE7; after ride read EB12ECF7 (254)  PASS
```

The original `256` boundary attempt wrote `18131228`, but the subsequent failed-token read did not show the expected Earth identity/state. Treat this as inconclusive, not as a reliable rejection of family 3.

## End goal of this plan

**Current outcome:** Earth is registered and resettable for `0..500` using the generalized zero-block/rotation codec. The older plan goals below are historical and were superseded by Phase 7.

Historical goals from the earlier partial-registration phase:

- Keep Earth reset safe via `RidesCli reset --sequence earth`.
- Register only families with enough supporting evidence.
- ~~Treat Earth as capped at `255` in application code until contradictory evidence appears.~~ Superseded: corrected XOR-derived high values validated and Earth is now `0..500`.
- ~~Keep `debug/write-earth-boundary.sh` limited to confirmed starts (`127`, `128`, `255`) so known-bad high candidates are not accidentally reused.~~ Superseded by production registration and generalized codec.
- ~~Do not register full Earth `0..500` unless future evidence identifies valid higher families.~~ Superseded by Phase 7 validation.

## Key working assumptions

- Boundary writes must never write block 0 or block 7.
- Boundary writes must only write blocks 5 and 6.
- Reset writes may use the existing safe per-block reset path for blocks 1..6.
- Process executor comparison remains blocked by local Homebrew `proxmark3` missing `/opt/homebrew/opt/lua/lib/liblua.dylib`.

---

# Phase 0 — Baseline safety and code inspection

## Todos

- [x] Read relevant files before implementation:
  - `Tokens/EncodingSequence.cs`
  - `Tokens/TokenBlockUtils.cs`
  - `Tokens.Tests/TokenBlockUtilsTest.cs`
  - `RidesCli/ResetPage0BlocksLoader.cs`
  - `RidesCli/RidesCommandHandler.cs`
  - `RidesCli/RidesCli.csproj`
  - `RidesCli.Tests/RidesCommandHandlerTests.cs`
- [x] Before hardware writes, run read-only preflight:

  ```bash
  printf 'connect\ntune\nread 0\nread 1\nread 2\nread 3\nread 4\nread 5\nread 6\nread 7\nexit\n' | dotnet run --project Pm3Cli
  ```

- [x] Stop and ask the user to reposition the token if connect/detect/read fails or reads look inconsistent.

## Agent notes / assumptions

- Notes: Several reads showed block 7 flipping between `00000000` and `FFFFFFFF`; because boundary/reset operations never write block 7 and blocks 1..6 were stable, work continued when target blocks were consistent.

---

# Phase 1 — Add Earth support

## Todos

- [x] Add confirmed low Earth family:

  ```csharp
  Earth0To127 = new TokenBlockUtils.Family(0x1812, 0x5BD4, 0)
  ```

- [x] Add a public family alias: `TokenBlockUtils.Families.Family1812_0To127`.
- [x] Add `EncodingSequences.Earth` with initial low range.
- [x] Add Earth to `EncodingSequences.All`.
- [x] After hardware confirmation, add confirmed second Earth family:

  ```csharp
  Earth128To255 = new TokenBlockUtils.Family(0xEB12, 0xDBDC, 128)
  ```

- [x] Add a public second-family alias: `TokenBlockUtils.Families.FamilyEB12_128To255`.
- [x] Extend `EncodingSequences.Earth` to `0..255` only. _(Historical; superseded by Phase 7 full `0..500` registration.)_
- [x] Do not add predicted Earth `256..500` families. _(Historical; superseded by corrected XOR-derived high values.)_
- [x] Expose sequence-supported ride ranges via `EncodingSequence.MinRides` / `MaxRides`.
- [x] Enforce Earth's confirmed cap (`0..255`) in `RidesCli set/add/price`. _(Historical; Earth is no longer capped.)_

## Agent notes / assumptions

- Historical note: `RidesCli set/add` initially preserved and wrote Earth values only within `0..255`. This was superseded after the generalized codec corrected and validated Earth `256+` values.

---

# Phase 2 — Add Earth reset image

## Todos

- [x] Create `RidesCli/Data/earth-0-rides.bin` as big-endian 32-bit words:

  ```text
  00148040
  D3FE005D
  522BC69D
  650432F5
  650432F5
  18121218
  18121218
  00000000
  ```

- [x] Embed the resource in `RidesCli/RidesCli.csproj`.

## Agent notes / assumptions

- Notes: Reset image existence is covered by the existing all-sequences reset image test via `EncodingSequences.All`.

---

# Phase 3 — Tests

## Todos

- [x] Add/update tests for `EncodingSequences.TryGetByFriendlyName("earth", out ...)`.
- [x] Add/update tests for `EncodingSequences.Earth.ResetImageFileName == "earth-0-rides.bin"`.
- [x] Add/update tests for `TokenBlockUtils.Decode(new T55Block(0x18121218)) == 0`.
- [x] Add/update tests for `TokenBlockUtils.Encode(0, EncodingSequences.Earth) == 0x18121218`.
- [x] Add/update tests for captured D3 table `0..23` encode/decode.
- [x] Add/update tests for `TryGetSequenceFromBlock(0x18121218)` returning Earth.
- [x] Add/update tests for `TryGetSequenceFromBlock(0xEB129210)` returning Earth.
- [x] Ensure reset-image existence tests include Earth via `EncodingSequences.All`.
- [x] Add/update `RidesCli reset --sequence earth` test expecting identity blocks:

  ```text
  block1 D3FE005D
  block2 522BC69D
  block3 650432F5
  block4 650432F5
  block5 18121218
  block6 18121218
  ```

- [x] Add tests for confirmed Earth second-family boundary values:

  ```text
  128 -> EB129210
  254 -> EB12ECF7
  255 -> EB12EDE7
  ```

- [x] Add Earth round-trip test for `0..255`.
- [x] Add `RidesCli` test ensuring Earth profile is preserved/written within confirmed second family.

## Agent notes / assumptions

- Notes: Avoid adding round-trip tests for unconfirmed Earth `256..500` until those families are registered.

---

# Phase 4 — Boundary writer script

## Todos

- [x] Add `debug/write-earth-boundary.sh` using `dotnet run --project Pm3Cli`.
- [x] Validate that the argument is one of the supported confirmed starts:

  ```text
  127 -> 18126DEF
  128 -> EB129210
  255 -> EB12EDE7
  ```

- [x] Print a warning that only blocks 5 and 6 are written.
- [x] Pipe only these commands to `Pm3Cli`:

  ```text
  connect
  read 1
  read 2
  read 3
  read 4
  read 5
  read 6
  write 5 <HEX>
  write 6 <HEX>
  read 5
  read 6
  exit
  ```

- [x] Support `--dry-run`.
- [x] Confirm the script never writes blocks 0, 1, 2, 3, 4, or 7.

## Agent notes / assumptions

- Notes: The script no longer includes known-bad high-family candidates. Any future experimental high-family writes should be one-off/manual or added under a clearly named experimental script.

---

# Phase 5 — Validation and hardware reset

## Todos

- [x] Run non-integration tests:

  ```bash
  dotnet test --filter "Category!=Integration&Category!=IntegrationParity"
  ```

- [x] After tests pass and read-only preflight succeeds, reset a physical token to Earth zero:

  ```bash
  printf 'reset --sequence earth\ny\nread\nexit\n' | dotnet run --project RidesCli
  ```

- [x] Verify with `Pm3Cli`:

  ```bash
  printf 'connect\nread 1\nread 2\nread 3\nread 4\nread 5\nread 6\nexit\n' | dotnet run --project Pm3Cli
  ```

- [x] Expected reset verification observed:

  ```text
  Block 1: D3FE005D
  Block 2: 522BC69D
  Block 3: 650432F5
  Block 4: 650432F5
  Block 5: 18121218
  Block 6: 18121218
  ```

- [x] If reset fails, report exact failed block and rollback output; do not continue to boundary writes.

## Agent notes / assumptions

- Notes: Earth resets succeeded through the safe per-block reset path. No rollback failures were observed.

---

# Phase 6 — Boundary evidence and future registration

## Todos

- [x] Record user-reported/observed first boundary results:

  ```text
  start 128 -> write EB129210; after ride expect/read 127 -> 18126DEF; PASS
  start 255 -> write EB12EDE7; after ride expect/read 254 -> EB12ECF7; PASS
  ```

- [x] Register confirmed Earth families `0..127` and `128..255`.
- [x] Investigate/extrapolate the third Earth family from Mercury/Venus/Earth patterns before another hardware attempt.
- [x] Re-test old predicted `256 -> 255` with careful token identity/readback tracking:

  ```text
  start 256 -> write 18131228; expected 255 -> EB12EDE7; observed unchanged/rejected
  ```

- [x] Test old predicted `383 -> 382`:

  ```text
  start 383 -> write 18136DDF; expected 382 -> 18136CCF; observed unchanged/rejected
  ```

- [x] Test visual alternative `256 -> 255`:

  ```text
  start 256 -> write 18111228; expected 255 -> EB12EDE7; observed elevator double-beep/low indication and readback Earth zero
  ```

- [x] Test visual alternative `384 -> 383`:

  ```text
  start 384 -> write EB119220; expected 383 -> 18116DDF; observed elevator double-beep/low indication and readback Earth zero
  ```

- [x] Formalize the then-current conclusion that Earth appeared capped at `255` (superseded by Phase 7).
- [x] Add sequence-specific range checks/tests so Earth `set/add` above `255` errors without writing.
- [x] Future evidence identified corrected high Earth families; see Phase 7.

## Agent notes / assumptions

- Notes: The successful `128 -> 127` transition confirms both second-family starting value and transition down into the low family. The successful `255 -> 254` confirms second-family in-range decrement near its top.
- Historical note: The earlier single `256` failure was initially inconclusive, and subsequent controlled tests of old addition-derived `256`/`383` stayed unchanged while visual alternative `256`/`384` reset to zero/low. This temporarily supported a `0..255` cap, but Phase 7 showed those were the wrong high values; corrected XOR-derived values validated.

## Extrapolation notes for family 3

Known registered/confirmed sequence patterns:

```text
Mercury: 0..127 CCC7/0000, 128..255 3FC7/8008, 256..383 CCC6/0010, 384..500 3FC6/8018
Venus:   0..127 48C7/0084, 128..255 BBC7/808C, 256..383 48C6/0094, 384..500 BBC6/809C
Earth:   0..127 1812/5BD4, 128..255 EB12/DBDC
```

The first extrapolation used the Mercury/Venus XOR-step pattern:

```text
high16 step 0->1: XOR F300
high16 step 1->2: XOR F301
high16 step 2->3: XOR F300
xor step each segment: +8008 modulo 10000
base step each segment: +128
```

That predicted:

```text
Earth 256..383: high16 1813, xor 5BE4, base 256
Earth 384..500: high16 EB13, xor DBEC, base 384
```

Hardware rejected/no-changed those old predicted `256` and `383` starts.

A second visual extrapolation noticed Mercury/Venus can also be described as alternating prefixes with the last high16 nibble decrementing for the high ranges. Unlike the first extrapolation, this visual alternative is not applying the XOR-step high16 rule; it treats the third/fourth Earth high16 values as the visible prior prefixes minus one (`1812 -> 1811`, `EB12 -> EB11`):

```text
Mercury: CCC7, 3FC7, CCC6, 3FC6
Venus:   48C7, BBC7, 48C6, BBC6
Earth?:  1812, EB12, 1811, EB11
```

That predicted:

```text
Earth 256..383: high16 1811, xor 5BE4, base 256
Earth 384..500: high16 EB11, xor DBEC, base 384
```

Hardware accepted/processed visual alternative `256` (`18111228`) and `384` (`EB119220`), but not as true high ride counts: instead of decrementing by one, the elevator treated them as low/empty, gave a low-rides double beep, and the token read back as Earth zero (`18121218`).

Historical conclusion (superseded 2026-07-21): no then-tested high-family extrapolation behaved as true `256+`. The tests had used addition-derived constants; corrected XOR-derived values subsequently validated.

---

# Phase 7 — Generalized XOR correction and full Earth registration

## Todos

- [x] Derive the constant-zero-block rotation-4 algorithm from all registered sequences.
- [x] Identify that old Earth high XOR constants used addition (`5BE4`/`DBEC`) instead of XOR composition.
- [x] Predict corrected high families:

  ```text
  256..383: 1813 / 5BC4 / base 256
  384..500: EB13 / DBCC / base 384
  ```

- [x] Hardware validate corrected `256 -> 255` transition:

  ```text
  write 18131208; post-ride EB12EDE7
  ```

- [x] Hardware validate corrected `384 -> 383` transition:

  ```text
  write EB139200; post-ride 18136DFF
  ```

- [x] Register both corrected families and extend Earth to `0..500`.
- [x] Update unit and CLI tests, help text, and exploration documentation.

## Agent notes

- The 256 test used a `9BFE...` card and the 384 test used an `EBFE...` fob. Both worked with only blocks 5/6 changed, further confirming blocks 1..4 are not inputs to the Earth ride encoding.
