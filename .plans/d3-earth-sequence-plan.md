# D3 Earth Sequence Candidate Plan

## How agents should use this plan

Read this entire file before making changes. Find the next `[ ]` TODO and work on it; if new relevant work is discovered, add TODOs under the current phase before continuing. Keep each chunk coherent and testable, mark completed items by changing `[ ]` to `[x]`, and document assumptions/deviations in the notes. Commit plan updates in the same commit as corresponding code/test changes so implementation and handoff state stay aligned.

---

## What this work is

Prepare the D3 token sequence candidate, friendly name `earth`, for safe reset and limited boundary testing without registering unverified high-range families.

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

Captured low-family evidence:

```text
0..127 candidate: high16 1812, xor 5BD4, base 0
```

Captured rows cover real rides `23..0`, with block 5/6 values matching this family exactly.

## End goal of this plan

- Add safe, minimal Earth support using the confirmed low family and zero image.
- Allow `RidesCli reset --sequence earth` through the existing safe reset path.
- Provide a hardware boundary writer that writes only ride mirror blocks 5/6 for predicted candidate values.
- Record boundary-test outcomes before registering additional Earth families.
- Do not register full Earth `0..500` until boundary evidence supports it.

## Key working assumptions

- Boundary writes must never write block 0 or block 7.
- Boundary writes must only write blocks 5 and 6.
- Reset writes may use the existing safe per-block reset path for blocks 1..6.
- Process executor comparison remains blocked by local Homebrew `proxmark3` missing `/opt/homebrew/opt/lua/lib/liblua.dylib`.

---

# Phase 0 — Baseline safety and code inspection

## Todos

- [ ] Read relevant files before implementation:
  - `Tokens/EncodingSequence.cs`
  - `Tokens/TokenBlockUtils.cs`
  - `Tokens.Tests/TokenBlockUtilsTest.cs`
  - `RidesCli/ResetPage0BlocksLoader.cs`
  - `RidesCli/RidesCommandHandler.cs`
  - `RidesCli/RidesCli.csproj`
  - `RidesCli.Tests/RidesCommandHandlerTests.cs`
- [ ] Before any hardware write, run read-only preflight:

  ```bash
  printf 'connect\ntune\nread 0\nread 1\nread 2\nread 3\nread 4\nread 5\nread 6\nread 7\nexit\n' | dotnet run --project Pm3Cli
  ```

- [ ] Stop and ask the user to reposition the token if connect/detect/read fails or reads look inconsistent.

## Agent notes / assumptions

- Notes:
- Assumptions:

---

# Phase 1 — Add minimal Earth support

## Todos

- [ ] Add canonical low Earth family:

  ```csharp
  Earth0To127 = new TokenBlockUtils.Family(0x1812, 0x5BD4, 0)
  ```

- [ ] Add a public family alias consistent with existing style, e.g. `TokenBlockUtils.Families.Family1812_0To127`.
- [ ] Add `EncodingSequences.Earth`:

  ```csharp
  public static readonly EncodingSequence Earth = new(
      "earth",
      "earth-0-rides.bin",
      new EncodingSequenceSegment(0, 127, EncodingFamilyDefinitions.Earth0To127));
  ```

- [ ] Add Earth to `EncodingSequences.All`.
- [ ] Do not add predicted Earth high families yet.

## Agent notes / assumptions

- Notes:
- Assumptions:

---

# Phase 2 — Add Earth reset image

## Todos

- [ ] Create `RidesCli/Data/earth-0-rides.bin` as big-endian 32-bit words:

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

- [ ] Embed the resource in `RidesCli/RidesCli.csproj`.

## Agent notes / assumptions

- Notes:
- Assumptions:

---

# Phase 3 — Tests

## Todos

- [ ] Add/update tests for `EncodingSequences.TryGetByFriendlyName("earth", out ...)`.
- [ ] Add/update tests for `EncodingSequences.Earth.ResetImageFileName == "earth-0-rides.bin"`.
- [ ] Add/update tests for `TokenBlockUtils.Decode(new T55Block(0x18121218)) == 0`.
- [ ] Add/update tests for `TokenBlockUtils.Encode(0, EncodingSequences.Earth) == 0x18121218`.
- [ ] Add/update tests for captured D3 table `0..23` encode/decode.
- [ ] Add/update tests for `TryGetSequenceFromBlock(0x18121218)` returning Earth.
- [ ] Ensure reset-image existence tests include Earth via `EncodingSequences.All`.
- [ ] Add/update `RidesCli reset --sequence earth` test expecting identity blocks:

  ```text
  block1 D3FE005D
  block2 522BC69D
  block3 650432F5
  block4 650432F5
  block5 18121218
  block6 18121218
  ```

## Agent notes / assumptions

- Notes:
- Assumptions:

---

# Phase 4 — Boundary writer script

## Todos

- [ ] Add a simple `Pm3Cli`-based script, e.g. `debug/write-earth-boundary.sh`.
- [ ] Validate that the argument is one of the supported starts:

  ```text
  127 -> 18126DEF
  128 -> EB129210
  255 -> EB12EDE7
  256 -> 18131228
  383 -> 18136DDF
  384 -> EB139220
  500 -> EB13E667
  ```

- [ ] Print a warning that only blocks 5 and 6 are written.
- [ ] Pipe only these commands to `Pm3Cli`:

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

- [ ] Optionally support `--dry-run`.
- [ ] Confirm the script never writes blocks 0, 1, 2, 3, 4, or 7.

## Agent notes / assumptions

- Notes:
- Assumptions:

---

# Phase 5 — Validation and hardware reset

## Todos

- [ ] Run non-integration tests:

  ```bash
  dotnet test --filter "Category!=Integration&Category!=IntegrationParity"
  ```

- [ ] After tests pass and read-only preflight succeeds, reset a physical token to Earth zero:

  ```bash
  printf 'reset --sequence earth\ny\nread\nexit\n' | dotnet run --project RidesCli
  ```

- [ ] Verify with `Pm3Cli`:

  ```bash
  printf 'connect\nread 1\nread 2\nread 3\nread 4\nread 5\nread 6\nexit\n' | dotnet run --project Pm3Cli
  ```

- [ ] Expected reset verification:

  ```text
  Block 1: D3FE005D
  Block 2: 522BC69D
  Block 3: 650432F5
  Block 4: 650432F5
  Block 5: 18121218
  Block 6: 18121218
  ```

- [ ] If reset fails, report exact failed block and rollback output; do not continue to boundary writes.

## Agent notes / assumptions

- Notes:
- Assumptions:

---

# Phase 6 — Boundary evidence and future full registration

## Todos

- [ ] Record user-reported boundary results before registering additional families.
- [ ] If all boundary tests work, register the full Earth sequence:

  ```text
  0..127   1812 / 5BD4 / 0
  128..255 EB12 / DBDC / 128
  256..383 1813 / 5BE4 / 256
  384..500 EB13 / DBEC / 384
  ```

- [ ] Add full encode/decode/border tests similar to Venus only after boundary confirmation.

## Agent notes / assumptions

- Notes:
- Assumptions:
