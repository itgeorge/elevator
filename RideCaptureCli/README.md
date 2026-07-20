# RideCaptureCli

Low-interaction CLI for collecting T55xx token state transitions while investigating unknown ride-encoding sequences.

## Interactive behavior

- Press **Enter** to scan the current token.
- Type **`other`** to scan the current token into `other-captures.csv` without updating tracked sequences.
- Type **`zero`** to scan and mark the current sequence state as real zero rides.
- Type **`exact <n>`** to scan and mark the current sequence state as exact real ride count `n`.
- Type **`exact <n> <sequenceId>`** to update the latest row in an existing sequence without scanning a token.
- Type **`list`** to list known sequences and their latest values.
- Type **`help`** for commands.
- Type **`exit`** to quit.

## Non-interactive behavior

You can also run a single command directly from the command line.

Examples:

```bash
dotnet run --project RideCaptureCli -- exact 238 EBFE002A-20260420-183831-s01
dotnet run --project RideCaptureCli -- exact 116 43FE0062-20260420-183756-s01
dotnet run --project RideCaptureCli -- other
dotnet run --project RideCaptureCli -- list
```

If you omit the sequence id, the command requires a live scan:

```bash
dotnet run --project RideCaptureCli -- exact 137
```

Normal Enter/`zero`/`exact <n>` scans update `captures.csv` and sequence state. `other` scans write only to `other-captures.csv`, so they are safe for opportunistic scans of unrelated tokens while preserving the active sequence you are investigating.

Each successful scan:

- reads signal strength
- runs a proxmark dump
- reads page 0 blocks `0..7`
- identifies the token from blocks `1..4`
- tracks the current state from blocks `5..6`
- appends/updates `ride-capture-data/captures.csv` for normal sequence commands, or `ride-capture-data/other-captures.csv` for `other`
- copies the proxmark-created `.bin` dump into `ride-capture-data/dumps/yyyy-MM-dd/`, or writes a native page-0 `.bin` fallback when running through direct USB

## Generated files

On first run the tool creates:

- `ride-capture-config.json`
- `ride-capture-data/captures.csv`
- `ride-capture-data/other-captures.csv` after the first `other` scan

## Config

Default config values:

```json
{
  "MaxAcceptableSignalMv": 29000,
  "OutputRootDirectory": "ride-capture-data",
  "ProxmarkDumpSearchDirectory": "proxmark-runs"
}
```

For this reader/setup, **higher signal values are worse**. Values above `29000 mV` are treated as weak/bad positioning and highlighted in the console.

## CSV notes

Important columns:

- `token_id` = blocks `1..4`
- `sequence_id` = logical tracked sequence
- `tracked_count` = monotonic tracking value (`10000`, `9999`, ... for unknown starts)
- `real_ride_count` = blank until known / normalized
- `block5`, `block6` = encoded ride-state blocks
- `copied_dump_relative_path` = relative path to copied local proxmark dump

`other-captures.csv` stores timestamp, token id, warnings, signal strength, blocks `0..7`, and copied dump path, but no sequence id/tracked count/real ride count.

## Seeded starting tokens

These are defined in `SeededTokenCatalog.cs`:

- `D3FE005D-522BC69D-650432F5-650432F5` → `24`
- `43FE0062-5BA494A3-D6D1C733-D6D1C733` → `181`
- `EBFE002A-F100CC5B-A5045936-A5045936` → `262`
- `C3FE0031-20C60722-B6D14924-B6D14924` → `14`
