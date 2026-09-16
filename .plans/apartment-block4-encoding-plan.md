# Apartment Block 4 Encoding Plan

## How agents should use this plan

Read this entire file before making changes. Start each session with `git status --short --branch` and inspect the current versions of all files relevant to the next task. Find the next `[ ]` TODO and work on it; if new relevant work is discovered, add TODOs under the current phase before continuing. Keep working until the current TODO, or a coherent group of TODOs that forms a testable chunk, is complete. Mark completed items by changing `[ ]` to `[x]`, and document assumptions, deviations, and design decisions in this file.

Commit completed plan updates in the same commit as the corresponding code/tests so this handoff remains aligned with implementation. Stage only files belonging to this work. Do not delete, rewrite, format, stage, or commit unrelated untracked or modified files.

**Hard non-touch rule:** leave in-progress parallel work alone, especially `RidesBridge*`, `RidesTablet*`, and any other already-dirty paths unrelated to this plan. If unsure whether a file is in scope, stop and ask.

Important working-tree note at plan creation: unrelated dirty/untracked paths visible in this checkout included:

```text
RidesBridge/
RidesBridge.Tests/
RidesTablet/
RidesTabletTests/
debug/EncodeRideBlock/
debug/RideBlockGuessPrototype/.idea/
debug/__pycache__/
debug/red-token-investigation/
debug/write-variant-profile.sh
```

Do not touch those paths unless the user explicitly changes scope.

---

## What this work is

Implement a v1 apartment encoding for T55xx **page-0 block 4**, plus `RidesCli` commands to read/write it and reset behavior that preserves a sealed apartment value by default.

Hardware already established (2026-09-11) that block 4 can differ from block 3, accepts arbitrary bytes, and is preserved across rides on the tested elevator. Canonical profiles still ship `block4 == block3`; current reset can overwrite block 4. This plan turns block 4 into application storage without requiring more device experiments.

---

## End goal of this plan

- Pure encode/decode for apartment block 4 lives in `Tokens`, covered by unit tests.
- Wire format is:

  ```text
  block4 = seal:16 | ((building:8 || apt:8) XOR mask:16)
  ```

  with `building` fixed to `0` for all encodes in this slice.
- `seal` and `mask` are derived from a PRF keyed by an in-memory **secret**, mixing in block 3 and version label `"apt-v1"`.
- `RidesCli` gains:
  - `apt` — read apartment when no argument; write apartment when given an integer `0..255`
  - `aptsecret` — set/replace the in-memory secret (blind console input)
  - shared secret ensure-path used by `apt` when the secret is missing
- `reset` preserves block 4 when it decodes as a sealed apartment, unless `--resetapt` is passed.
- If the secret is missing during reset, block 4 is preserved by default (fail-safe) unless `--resetapt`.
- “Same identity” for reset optimization uses **blocks 1..3** (not 1..4); block 4 is handled separately.
- Software/TDD only in this plan: no hardware smoke checklist required for completion.
- Help text / usage strings updated for the new commands and `--resetapt`.
- Targeted automated tests pass.

---

## Key working assumptions and non-goals

### Assumptions

- Block 4 remains safe application storage on the tested system; reuse existing proven page-0 read/write/verify paths.
- Apartment capacity is one byte (`0..255`). Building is also one byte on the wire, but **always encoded as `0`** in this slice; no CLI input for building yet.
- Region/geo grouping stays off-card for now.
- Version label is the code constant `"apt-v1"` (not user-entered, not stored as its own on-card field).
- Naming: the 16-bit integrity field is called **seal** (not “tag”), to avoid collision with physical token/tag language.
- Single shared **secret** keys both mask and seal via distinct PRF labels (`"mask"` / `"seal"`).
- Preferred PRF: `HMAC-SHA256` truncated as needed (16 bits for seal and mask).
- Secret handling for this slice is **console + in-memory cache only**. No `.env` loader and no process-environment secret lookup in this plan.
- Tablet/iPad UI secret submenu and bridge integration are **out of scope**.
- Default building `0` is acceptable until a later plan adds building input.
- Executing agents may refine type/API names if a better fit appears, but must update this plan when they do.

### Non-goals

- No `.env` / environment-variable secret loading.
- No UI/menu secret entry (iPad/tablet).
- No `--building` / building CLI flags.
- No region id on-card.
- No hardware smoke tests as a completion gate.
- No changes to ride encode/decode, blocks 5/6 authority, or block 0/7 write policy.
- No edits to unrelated parallel worktrees listed above.
- No identity-profile TokenId redesign beyond what reset/apt flows require (full TokenId still includes block 4 today; do not broaden into a recognition refactor unless a failing in-scope test forces a minimal fix — if so, document it here).

---

## Agreed encoding design (v1)

```text
plaintext = building:8 || apt:8                 # building always 0 in this slice
mask      = trunc16(PRF(secret, "mask" || "apt-v1" || block3))
seal      = trunc16(PRF(secret, "seal" || "apt-v1" || block3 || plaintext))
block4    = seal:16 || (plaintext XOR mask)
```

Decode:

1. Require secret.
2. Recompute mask from secret + `"apt-v1"` + block 3; recover plaintext.
3. Recompute expected seal; compare in constant-time-ish manner if easy, otherwise exact equality is fine at this size.
4. Success ⇒ `{ Building, Apt }`; failure ⇒ not a sealed apartment encoding.

Recognition implications:

- Do **not** use `block4 == block3` as the primary “is apartment?” discriminator.
- Factory mirrors and junk fail the seal check (false-accept ~1/65536).
- Copying block 4 onto a token with a different block 3 fails the seal check.

---

## Proposed code structure (starting point; may evolve)

These names are a strong starting point, not a rigid contract. Update this section if implementation chooses clearer names.

### `Tokens` (pure)

- `ApartmentBlockCodec` (static)
  - `Encode(ReadOnlySpan<byte> secret, T55Block block3, byte building, byte apt) -> T55Block`
  - `TryDecode(ReadOnlySpan<byte> secret, T55Block block3, T55Block block4, out ApartmentPayload payload) -> bool`
- `ApartmentPayload` record/struct: `byte Building`, `byte Apt`
- Private helpers for HMAC-SHA256 truncation / byte packing
- Tests in `Tokens.Tests` (new fixture file, e.g. `ApartmentBlockCodecTests.cs`)

### `RidesCli`

- `ApartmentSecretStore` (or similar): in-memory secret cache for the process
  - `HasSecret`, `SetSecret`, `TryGetSecret`
- Extend `IRidesInput` with blind secret read, e.g. `ReadSecretLine()`
  - `ConsoleRidesInput`: no-echo read
  - `ScriptedRidesInput`: dequeue scripted secret lines for tests
- `RidesCommandHandler` commands:
  - `apt` / `aptsecret`
  - shared `EnsureApartmentSecret()` used by `apt` (and reset classification when needed)
- Reset changes in `ExecuteResetCore` / target-block selection:
  - same-identity check on blocks **1..3**
  - block 4 include/exclude based on sealed-apt detection + `--resetapt`
- Usage/help strings updated

### Tests

- `Tokens.Tests/ApartmentBlockCodecTests.cs`
- `RidesCli.Tests` extensions for secret ensure, `apt`, `aptsecret`, reset preservation
- Prefer existing fakes: `FakeRidesPm3Api`, `ScriptedRidesInput`, `StringBuilderRidesOutput`

---

# Phase 0 — Pure apartment codec in `Tokens`

## Goal

Land the encode/decode algorithm with no PM3/CLI dependencies.

## Todos

- [x] Add failing unit tests for `ApartmentBlockCodec` covering:
  - round-trip encode/decode with fixed test secret, `building=0`, various apt values
  - wrong block3 ⇒ decode failure
  - wrong secret ⇒ decode failure
  - `block4 == block3` factory mirror ⇒ decode failure (for normal secrets/payloads)
  - random/junk block4 ⇒ decode failure
  - same payload + different block3 ⇒ different block4
  - different apt ⇒ different block4
  - building is present in the payload (decode returns `Building == 0` for v1 encodes)
- [x] Implement `ApartmentPayload` + `ApartmentBlockCodec` using HMAC-SHA256 PRF, version `"apt-v1"`, layout `seal:16 | obfuscated_payload:16`.
- [x] Export only what CLI needs; keep PRF helpers private unless tests require internals.
- [x] Run `dotnet test Tokens.Tests` and fix until green.

## Agent notes / assumptions

- Notes: Implemented `ApartmentPayload` (readonly record struct) and public static `ApartmentBlockCodec` with `Encode` / `TryDecode` as proposed. PRF helpers (`ComputeTrunc16`, block3 byte packing, HMAC truncation) are private to `ApartmentBlockCodec`. Block3 is serialized big-endian (4 bytes) for HMAC input; seal/mask/plaintext use big-endian 16-bit packing; `block4` layout is `seal` in the high 16 bits and obfuscated payload in the low 16 bits.
- Assumptions: `trunc16` is the first two bytes of HMAC-SHA256 interpreted as big-endian uint16. Factory-mirror rejection is implicit via seal mismatch (no special-case check).

---

# Phase 1 — Secret input + in-memory cache in `RidesCli`

## Goal

Support blind secret entry and process-lifetime caching, without env/.env loading.

## Todos

- [ ] Extend `IRidesInput` with `ReadSecretLine()` (name may vary; update plan if renamed).
- [ ] Implement no-echo console secret read in `ConsoleRidesInput`.
- [ ] Extend `ScriptedRidesInput` so tests can script secret prompts.
- [ ] Add an in-memory secret store used by `RidesCommandHandler` (new type or private field + helpers).
- [ ] Add failing tests for:
  - missing secret ⇒ ensure prompts once, caches value
  - second ensure ⇒ no second prompt
  - empty/cancelled secret input ⇒ clear error, secret remains unset
  - secret value never appears in `IRidesOutput` lines
- [ ] Implement `aptsecret` command wiring (can be thin in this phase, fully exercised in Phase 2) or a shared ensure helper first if that yields a smaller TDD step.
- [ ] Run targeted `RidesCli.Tests` for the new secret behavior.

## Agent notes / assumptions

- Notes:
- Assumptions:

---

# Phase 2 — `apt` and `aptsecret` commands

## Goal

Operators can set the secret and read/write apartment number via `RidesCli`.

## Behavior contract

```text
aptsecret             Prompt for secret (blind), store in memory, confirm without echoing secret
apt                   Ensure secret; read blocks 3 and 4; print apartment or “not encoded” / errors
apt <0-255>           Ensure secret; read block 3; encode building=0 + apt; write/verify block 4 only; print result
```

## Todos

- [ ] Add failing handler tests for `aptsecret` happy path and replace-existing-secret path.
- [ ] Add failing handler tests for `apt` read:
  - sealed value decodes and prints apt (and building 0)
  - mirror/junk ⇒ not encoded message
  - missing secret ⇒ prompts/ensure path then decodes (scripted)
  - if secret entry fails ⇒ warning/error, no crash, no write
- [ ] Add failing handler tests for `apt <n>` write:
  - writes only block 4
  - uses current block 3 in the codec
  - readback round-trips
  - rejects non-integer / out-of-range values with usage error and no write
- [ ] Implement command parsing + handler methods; update help/usage strings.
- [ ] Keep building hard-coded to `0` on write; do not accept building CLI args.
- [ ] Run targeted `RidesCli.Tests` until green.

## Agent notes / assumptions

- Notes:
- Assumptions:

---

# Phase 3 — Reset preserves sealed apartment unless `--resetapt`

## Goal

Profile reset no longer casually wipes apartment data.

## Behavior contract

- Parse optional `--resetapt`.
- Same-identity optimization compares blocks **1..3** (plus existing ride-sequence checks on 5/6 as today), **not** block 4.
- When selecting target blocks:
  - if `--resetapt` ⇒ block 4 may be written like other identity blocks
  - else if secret available and block 4 decodes as sealed apt ⇒ **exclude block 4**
  - else if secret missing and block 4 differs from profile block 4 (or cannot be classified) ⇒ **exclude block 4** and warn that apartment classification/preservation used fail-safe behavior
  - else (no sealed apt / still profile mirror) ⇒ block 4 may be written as today
- `-f` still skips confirmation, but must not skip the reads needed to classify/preserve block 4.
- Never write blocks 0 or 7.

## Todos

- [ ] Add failing reset tests:
  - sealed apt present, no `--resetapt` ⇒ block 4 unchanged after reset; rides/identity otherwise reset
  - sealed apt present + `--resetapt` ⇒ block 4 becomes profile image value
  - no apt / profile-mirror block 4 ⇒ existing reset behavior remains
  - blocks 1..3 match profile but custom sealed block 4 ⇒ do not treat as reason to wipe apt; prefer rides-only or non-4 targets as appropriate
  - secret missing + divergent block 4 ⇒ preserve block 4 + warning
  - `-f` without `--resetapt` still preserves sealed apt
- [ ] Update reset arg parsing for `--resetapt`; update usage/help.
- [ ] Update `IsSameResetProfile` (or replacement) to ignore block 4 for identity sameness.
- [ ] Implement target-block selection changes + operator messages (“preserving apartment in block 4”, etc.).
- [ ] Ensure reset still only writes page-0 blocks in `1..6`.
- [ ] Run targeted reset-related `RidesCli.Tests` until green.

## Agent notes / assumptions

- Notes:
- Assumptions:

---

# Phase 4 — Cleanup, docs touchpoints, handoff

## Goal

Leave the repo coherent for the next agent/human smoke test without doing device work now.

## Todos

- [ ] Update `RidesCli` help output / any command list so `apt`, `aptsecret`, and `reset ... [--resetapt]` are discoverable.
- [ ] Add a short design note pointer in `.docs/ride-encoding-exploration-2026-07-21.md` (or a tiny sibling doc if cleaner) that block 4 apartment encoding is implemented per this plan — keep it brief; no need to paste the whole algorithm twice.
- [ ] Run broader targeted test sets: `dotnet test Tokens.Tests` and `dotnet test RidesCli.Tests`.
- [ ] Final plan pass: all in-scope TODOs `[x]`, notes filled, deviations recorded.
- [ ] Stop for human hardware smoke later (not part of this plan’s completion gate): write apt → ride → confirm block 4 survives → reset without `--resetapt` → apt remains → reset with `--resetapt` clears it.

## Agent notes / assumptions

- Notes:
- Assumptions:

---

## Validation summary (software)

```bash
dotnet test Tokens.Tests
dotnet test RidesCli.Tests
```

Do not run hardware writes as part of finishing this plan unless the user explicitly asks.
