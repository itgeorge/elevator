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

## Current status (2026-07-12)

Earth is now implemented for confirmed rides `0..255`:

| Range | Family | Evidence | Code status |
|---|---|---|---|
| `0..127` | `1812` / xor `5BD4` / base `0` | Captured `0..23`; reset zero verified; `128 -> 127` elevator transition verified | Registered |
| `128..255` | `EB12` / xor `DBDC` / base `128` | `128 -> 127` transition verified; `255 -> 254` in-family decrement verified | Registered |
| `256..383` | predicted `1813` / xor `5BE4` / base `256` | One `256` write was attempted, but user reported elevator failure and later token identity/state was ambiguous; not confirmed | Not registered |
| `384..500` | predicted `EB13` / xor `DBEC` / base `384` | Not tested | Not registered |

Confirmed hardware reads after elevator use:

```text
start 128 -> wrote EB129210; after ride read 18126DEF (127)  PASS
start 255 -> wrote EB12EDE7; after ride read EB12ECF7 (254)  PASS
```

The original `256` boundary attempt wrote `18131228`, but the subsequent failed-token read did not show the expected Earth identity/state. Treat this as inconclusive, not as a reliable rejection of family 3.

## End goal of this plan

- Keep Earth reset safe via `RidesCli reset --sequence earth`.
- Register only families with enough supporting evidence.
- Continue using `debug/write-earth-boundary.sh` for unregistered predicted families.
- Investigate/extrapolate the third family (`256..383`) from Mercury/Venus/Earth patterns and then test it carefully.
- Do not register full Earth `0..500` until boundary evidence supports all remaining families.

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
- [x] Extend `EncodingSequences.Earth` to `0..255` only.
- [ ] Do not add predicted Earth `256..500` families until confirmed.

## Agent notes / assumptions

- Notes: `RidesCli set/add` may now preserve and write Earth values within `0..255`. Predicted higher Earth values still require the boundary writer script.

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
- [x] Validate that the argument is one of the supported starts:

  ```text
  127 -> 18126DEF
  128 -> EB129210
  255 -> EB12EDE7
  256 -> 18131228
  383 -> 18136DDF
  384 -> EB139220
  500 -> EB13E667
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

- Notes: Keep this script for predicted/unregistered high-family tests; do not use `RidesCli set` for unregistered `256..500` Earth values.

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
- [ ] Investigate/extrapolate the third Earth family from Mercury/Venus/Earth patterns before another hardware attempt.
- [ ] Re-test predicted `256 -> 255` with careful token identity/readback tracking:

  ```text
  start 256 -> write 18131228; after ride expect 255 -> EB12EDE7
  ```

- [ ] If `256 -> 255` succeeds, consider registering only Earth `256..383`:

  ```text
  256..383 1813 / 5BE4 / 256
  ```

- [ ] Test `383 -> 382` before fully trusting family 3:

  ```text
  start 383 -> write 18136DDF; after ride expect 382 -> 18136CCF
  ```

- [ ] Test family 4 separately before registering `384..500`:

  ```text
  384 -> EB139220; after ride expect 383 -> 18136DDF
  500 -> EB13E667; after ride expect 499 -> EB13E117
  ```

- [ ] Only after remaining boundary tests work, register full Earth sequence:

  ```text
  0..127   1812 / 5BD4 / 0
  128..255 EB12 / DBDC / 128
  256..383 1813 / 5BE4 / 256
  384..500 EB13 / DBEC / 384
  ```

- [ ] Add full encode/decode/border tests similar to Venus only after boundary confirmation.

## Agent notes / assumptions

- Notes: The successful `128 -> 127` transition confirms both second-family starting value and transition down into the low family. The successful `255 -> 254` confirms second-family in-range decrement near its top.
- Notes: The earlier `256` failure should be treated as inconclusive because the later read showed an unexpected token/identity state; do not use it alone to reject predicted family 3.

## Extrapolation notes for family 3

Known registered/confirmed sequence patterns:

```text
Mercury: 0..127 CCC7/0000, 128..255 3FC7/8008, 256..383 CCC6/0010, 384..500 3FC6/8018
Venus:   0..127 48C7/0084, 128..255 BBC7/808C, 256..383 48C6/0094, 384..500 BBC6/809C
Earth:   0..127 1812/5BD4, 128..255 EB12/DBDC
```

Across Mercury and Venus, the family-to-family step pattern is stable:

```text
high16 step 0->1: XOR F300
high16 step 1->2: XOR F301
high16 step 2->3: XOR F300
xor step each segment: +8008 modulo 10000
base step each segment: +128
```

Applying that to confirmed Earth families gives:

```text
Earth 256..383: high16 1813, xor 5BE4, base 256
Earth 384..500: high16 EB13, xor DBEC, base 384
```

The third-family prediction is especially strong because both independent derivations agree:

```text
EB12 XOR F301 = 1813
DBDC + 8008 = 5BE4 (mod 10000)
```

Recommended next hardware check remains:

```text
start 256 -> write 18131228; after ride expect 255 -> EB12EDE7
```

If this succeeds, test the top of family 3:

```text
start 383 -> write 18136DDF; after ride expect 382 -> 18136CCF
```
