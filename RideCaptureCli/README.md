# RideCaptureCli

Low-interaction CLI for collecting T55xx token state transitions while investigating unknown ride-encoding sequences.

## Current behavior

- Press **Enter** to scan the current token.
- Type **`zero`** to scan and mark the current sequence state as real zero rides.
- Type **`help`** for commands.
- Type **`exit`** to quit.

Each successful scan:

- reads signal strength
- runs a proxmark dump
- reads page 0 blocks `0..7`
- identifies the token from blocks `1..4`
- tracks the current state from blocks `5..6`
- appends/updates `ride-capture-data/captures.csv`
- tries to copy the proxmark-created `.bin` dump into `ride-capture-data/dumps/yyyy-MM-dd/`

## Generated files

On first run the tool creates:

- `ride-capture-config.json`
- `ride-capture-data/captures.csv`

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

## Seeded starting tokens

These are seeded from `familyerrors/README.md`:

- `D3FE005D-522BC69D-650432F5-650432F5` → `24`
- `43FE0062-5BA494A3-D6D1C733-D6D1C733` → `181`
- `EBFE002A-F100CC5B-A5045936-A5045936` → `262`
