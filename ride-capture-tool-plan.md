# Ride Capture Tool Implementation Plan

## Context

This repository already contains tooling for working with T55xx-based elevator tokens via Proxmark3:

- `RidesCli/` reads and writes ride counts for the currently understood encoding families.
- `Tokens/TokenBlockUtils.cs` contains the known ride encoding/decoding logic.
- `RidesCli` and its tests already support reading page 0 blocks `0..7`, writing page 0 blocks `1..6`, and handling unknown ride-encoding families by saving token dumps.
- `Pm3UsbApi/` already exposes the Proxmark3 operations needed for this work, especially page 0 block reads and raw dump output.

We are now building a **low-interaction capture tool** to investigate the unknown encoding families/sequences.

## Investigation assumptions / domain rules

These assumptions are based on the current analysis and must be preserved unless new data disproves them:

- T55xx page 0 **blocks 5 and 6** are mirrored ride-state blocks.
- After a ride, only **blocks 5 and 6** change.
- Token identity / family identity should be inferred automatically from the other static blocks, specifically **page 0 blocks 1..4**.
- The tool should require minimal interaction during capture: the operator places a token on the scanner and presses **Enter**.
- Each successful changed capture for a sequence implies the rides remaining went down by **1**.
- We want one cumulative CSV across all runs, plus copied `.bin` dumps stored locally under date-based subfolders.
- Unknown sequences should begin with a synthetic tracked value of **10000**. This is not a real ride count; it is a sentinel that allows relative tracking.
- If a scan for a token matches an already-known encoded state (`block5/block6`) that already has a known real ride count, the tool should automatically normalize the current sequence using that known point.
- There must also be a manual `zero` command that scans the current token, marks the current/duplicate entry as real zero, and backfills the current sequence accordingly.

## Seeded known starting points

Seed these from `familyerrors/README.md` using `blocks 1..4` as the token id:

- `D3FE005D-522BC69D-650432F5-650432F5` → starting real rides `24`
- `43FE0062-5BA494A3-D6D1C733-D6D1C733` → starting real rides `181`
- `EBFE002A-F100CC5B-A5045936-A5045936` → starting real rides `262`

For these seeded tokens, the first unseen capture in a sequence should use the seeded real ride count, then decrement by 1 on each changed capture.

## Capture behavior summary

### Enter / empty line

- Wait for the operator to place a token and press Enter.
- Only then attempt the scan.
- Read signal strength and page 0 blocks `0..7`.
- Identify token by `blocks 1..4`.
- Determine whether the scan is:
  - a duplicate / no-change scan,
  - the next step in an active sequence,
  - a new sequence for that token,
  - or a state that matches a previously known encoded value for that token.
- Copy the relevant proxmark-generated `.bin` dump into the local dataset folder.
- Append a CSV row with the scan details.
- Print highly visible ANSI-colored status to the console:
  - success / OK,
  - weak signal,
  - unknown token,
  - error conditions.

### `zero` command

- Performs the same scan/logging work as Enter.
- Then marks the current scan state (or matching duplicate row/state) as **real 0 rides**.
- Backfills the sequence so all rows in that sequence get a real ride count.
- This is the manual anchor for unknown sequences when the operator knows the token is truly at zero.

### Automatic sequence normalization

If a new sequence started at tracked `10000` later hits an encoded `block5/block6` state that already exists in the CSV for the same token id and that historical state has a known real ride count:

- compute `offset = tracked_count_at_match - known_real_ride_count_at_match`
- backfill the current sequence using `real_ride_count = tracked_count - offset`

This is the automatic equivalent of the `zero` backfill.

## Storage layout

Use one persistent root folder for the capture dataset, with:

- one cumulative CSV file for all runs
- copied dumps in date-based subfolders, e.g. `dumps/2026-04-20/...`
- relative copied dump paths stored in the CSV (quoted properly for CSV safety)

## Config requirements

Use a simple, easy-to-edit text-based config file (JSON is fine) with at least:

- maximum acceptable signal threshold, default **29000 mV**
  - for this reader/setup, **higher is worse**; a larger reported value means the reader is "trying harder"
  - values above this threshold should be treated as weak/bad positioning and highlighted clearly to the operator
- output root directory
- directory to search for proxmark-created `lf-t55xx-*.bin` files

## Suggested implementation location

Create a new CLI project for this capture workflow rather than overloading `RidesCli`.

Suggested name:

- `RideCaptureCli/`

This keeps the operator capture workflow separate from the normal ride management CLI.

---

## Sequential implementation checklist

### Phase 1 - Scaffold the new tool

- [ ] Create a new CLI project for the capture workflow, likely `RideCaptureCli/`.
- [ ] Reference existing shared projects needed for PM3 and token block handling.
- [ ] Add the new project to `ElevatorTokens.sln`.
- [ ] Add a minimal runnable entry point with a simple command loop.
- [ ] Decide and document the runtime working directory assumptions.
- [ ] **Commit checkpoint:** scaffolded project builds and runs as an empty shell.

### Phase 2 - Define config and persistent data layout

- [ ] Add a config model for capture settings.
- [ ] Implement loading config from an easy-to-edit file.
- [ ] Create defaults including maximum acceptable signal threshold `29000 mV`.
- [ ] Define dataset root layout:
  - [ ] cumulative CSV path
  - [ ] copied dump root
  - [ ] date-based dump subfolders
- [ ] Ensure directories are created automatically.
- [ ] **Commit checkpoint:** config and output layout are implemented and documented.

### Phase 3 - Define data model for CSV/state logic

- [ ] Define the token identifier format using page 0 blocks `1..4`.
- [ ] Define a sequence identifier format.
  - Recommended: `<block1>-<yyyyMMdd-HHmmss>-sNN`
- [ ] Define row fields for the cumulative CSV.
- [ ] Include enough fields to support later normalization and analysis, at minimum:
  - [ ] timestamp
  - [ ] token id
  - [ ] sequence id
  - [ ] status
  - [ ] signal mV
  - [ ] weak signal flag
  - [ ] tracked count
  - [ ] real ride count
  - [ ] zero-anchor flag
  - [ ] block0..block7
  - [ ] block5
  - [ ] block6
  - [ ] copied dump relative path
  - [ ] notes / warning text if useful
- [ ] Implement CSV reading and appending.
- [ ] Implement proper CSV escaping/quoting, especially for file paths with spaces.
- [ ] **Commit checkpoint:** CSV schema is implemented with round-trip-safe read/write.

### Phase 4 - Reuse PM3 read capabilities

- [ ] Reuse existing PM3 APIs to read page 0 blocks `0..7`.
- [ ] Reuse existing PM3 APIs to measure signal strength.
- [ ] Decide whether to use `Pm3` directly or a small adapter interface similar to `RidesCli`.
- [ ] Implement a capture-time scan routine that returns:
  - [ ] signal mV
  - [ ] blocks 0..7
  - [ ] token id from blocks 1..4
  - [ ] current encoded state from blocks 5/6
- [ ] Verify mirror behavior and record warnings if block 5 != block 6.
- [ ] **Commit checkpoint:** the tool can scan a token and print the parsed capture data.

### Phase 5 - Seed known starting tokens

- [ ] Encode the seeded mapping from `familyerrors/README.md` into the tool.
- [ ] Match seeded entries by token id (`blocks 1..4`).
- [ ] Ensure first unseen sequence for a seeded token starts with the seeded real ride count.
- [ ] Ensure subsequent changed captures decrement by 1.
- [ ] **Commit checkpoint:** seeded tokens initialize correctly.

### Phase 6 - Sequence detection rules

- [ ] Implement lookup of historical rows by token id.
- [ ] Implement encoded-state matching using `block5/block6` for the same token id.
- [ ] Implement duplicate / no-change detection.
- [ ] Implement active-sequence continuation when the encoded state changes by one step in capture order.
- [ ] Implement new-sequence creation when the token id matches history but no historical encoded state matches the current one.
- [ ] For new unknown sequences, start tracked count at `10000`.
- [ ] Ensure duplicate scans keep the same tracked count and are marked `NO_CHANGE`.
- [ ] **Commit checkpoint:** duplicate/new-sequence/continuation logic works from CSV history.

### Phase 7 - Automatic normalization against known historical states

- [ ] When a scan in a new sequence hits an encoded state already known for the same token id with a real ride count, compute the normalization offset.
- [ ] Backfill all rows in the current sequence with real ride counts.
- [ ] Ensure this works for both seeded real counts and previously normalized unknown sequences.
- [ ] Ensure repeated matching states for the same token imply the same real ride count.
- [ ] Add tests around this normalization logic.
- [ ] **Commit checkpoint:** automatic backfill against a known historical anchor works.

### Phase 8 - Implement the `zero` command

- [ ] Add the `zero` command to the command loop.
- [ ] Make `zero` perform the same scan/logging behavior as Enter.
- [ ] After the scan, find the current state / matching duplicate row for that token and mark it as real zero.
- [ ] Compute the offset and backfill all rows in the current sequence.
- [ ] Ensure duplicates at the zero point get the correct real ride count.
- [ ] Add tests for zero-backfill behavior.
- [ ] **Commit checkpoint:** manual `zero` anchoring works end-to-end.

### Phase 9 - Proxmark `.bin` dump discovery and copy

- [ ] Implement lookup of the relevant proxmark-generated `lf-t55xx-*.bin` file.
- [ ] Decide the matching heuristic, likely newest matching dump created around scan time.
- [ ] Copy the selected dump into the dataset under a date-based subfolder.
- [ ] Store only the **relative local copied path** in the CSV.
- [ ] Handle missing dump files gracefully with a warning/status.
- [ ] Add tests for file-copy path logic and CSV quoting.
- [ ] **Commit checkpoint:** copied local dump files are attached to captures.

### Phase 10 - Operator-facing console UX

- [ ] Implement the low-interaction loop where Enter triggers a scan.
- [ ] Add visible ANSI-colored status lines with large separators, e.g.:
  - [ ] `******** OK ********`
  - [ ] `----- WEAK SIGNAL -----`
  - [ ] `?????? UNKNOWN TOKEN ??????`
- [ ] Print strong success output for completed captures.
- [ ] Print obvious warnings for:
  - [ ] weak signal
  - [ ] unknown token
  - [ ] mirror mismatch
  - [ ] duplicate / no-change
  - [ ] missing proxmark dump file
- [ ] Keep the output readable from a short distance.
- [ ] **Commit checkpoint:** operator UX is practical for real scanning workflow.

### Phase 11 - Tests

- [ ] Add unit tests for token identification by blocks 1..4.
- [ ] Add tests for CSV read/write and quoting.
- [ ] Add tests for sequence id generation.
- [ ] Add tests for seeded token start values.
- [ ] Add tests for unknown token start value `10000`.
- [ ] Add tests for duplicate / no-change handling.
- [ ] Add tests for automatic normalization against a known historical state.
- [ ] Add tests for the `zero` command backfill.
- [ ] Add tests for weak-signal classification.
- [ ] Add tests for copied-dump path handling.
- [ ] **Commit checkpoint:** core behavior is covered by tests.

### Phase 12 - Documentation and polish

- [ ] Add a README or usage section for the new CLI.
- [ ] Document config file format and defaults.
- [ ] Document the CSV schema.
- [ ] Document the meaning of tracked count vs real ride count.
- [ ] Document the sequence-matching and auto-normalization rules.
- [ ] Document operational workflow for the human operator.
- [ ] **Commit checkpoint:** user-facing docs are present and accurate.

---

## Notes for future agents

When continuing this work, preserve these key design choices unless explicitly told otherwise:

- Use **blocks 1..4** as the token identifier.
- Use **block5/block6** as the encoded ride-state identity.
- Keep one cumulative CSV across all runs.
- Store copied dump paths in CSV as **relative local paths**.
- Unknown sequences begin at **10000** tracked count.
- Duplicate scans are recorded as **NO_CHANGE** and should not decrement tracked count.
- `zero` is both a scan and a manual real-zero anchor.
- Automatic normalization should occur when a new sequence hits any previously known encoded state for the same token id.
- Prefer **reasonable checkpoint commits** after each completed phase instead of one large final commit.

## Recommended commit cadence

Do **not** implement this as one giant commit. Commit at sensible checkpoints such as:

- project scaffold
- config/data model
- scan integration
- sequence logic
- automatic normalization
- `zero` command
- dump copying
- console UX
- tests/docs

Each checkpoint commit should leave the repo in a buildable state where possible.
