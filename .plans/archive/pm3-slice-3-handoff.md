# PM3 Native Integration — Slice 3/4 Handoff

This handoff continues from `pm3-implementation-plan.md` and `pm3-implementation-plan-stage-b.md` on branch `pm3-integration`.

> Note: an earlier handoff file was referenced from the Stage B plan, but it was never successfully created. This file is the current handoff source of truth for the next agent.

## Current repo state

- Branch: `pm3-integration`
- Relevant recent commits:
  - `00a9ac8` — Slice 1: native USB CDC executor for connect and LF tune
  - `c5bf941` — WIP native T55 detect/read, transport OK but detect hung
  - `828ae8d` — Slice 2 complete: fixed native T55 detect/read hang
  - `8f2ef38` — refactored integration tests to parameterized executor fixtures
- Stage A/process executor is complete and remains the default executor.
- Stage B/native executor supports read-only elevator token workflows.

At handoff time there were local uncommitted cleanup/doc changes:

- `Pm3UsbApi/Native/Demod/Pm3LfDemod.cs`
  - removes optional `trace` parameter/debug scaffolding from `AskDemodExt`
- `NativeT55Probe/Program.cs`
  - updates call site after trace removal
- `pm3-implementation-plan-stage-b.md`
  - updates Stage B status/checklist

Non-integration tests were run with:

```bash
dotnet test --filter "Category!=Integration&Category!=IntegrationParity"
```

Result: all matching tests passed; two Unix-specific port discovery tests skipped on Windows.

## Architecture summary

The PM3 API now uses typed commands instead of raw strings:

- `HwVersionCommand`
- `LfTuneCommand`
- `T55DetectCommand`
- `T55ReadBlockCommand`
- `T55WriteBlockCommand`
- `T55DumpCommand`
- `CliPassthroughCommand`

Executor seam:

```csharp
Task<CommandResult> ExecuteAsync(
    IReadOnlyList<IPm3DeviceCommand> commands,
    TimeSpan? timeout = null,
    CancellationToken ct = default,
    string? portOverride = null);
```

Main executors:

- `Pm3UsbApi/Execution/Pm3ProcessExecutor.cs`
  - wraps `proxmark3 -c "..."`
  - supports all Stage A operations, including write/dump/raw CLI
- `Pm3UsbApi/Native/Pm3NativeExecutor.cs`
  - speaks Proxmark3 NG/MIX/OLD frames over USB CDC serial
  - currently supports hw version, LF tune, T55 detect, T55 read

`Pm3Session.ExecuteT55Async(command)` automatically chains:

```csharp
[new T55DetectCommand(), command]
```

Therefore native Slice 3 support must handle both single-command and batched detect+write/dump paths.

## Stage A/process status

Process mode is complete and is still the compatibility baseline. It supports:

- connect / hw version
- LF tune
- T55 detect
- T55 read blocks 0–7
- T55 write
- T55 dump
- raw CLI passthrough

`RidesCli` selects executor from `PM3_EXECUTOR` via `Pm3Options.ReadExecutorKindFromEnvironment()`:

- unset or anything except `native` => process
- `PM3_EXECUTOR=native` => native

## Stage B/native Slice 1 + 2 status

Implemented native components:

- `Pm3UsbApi/Native/Protocol/Pm3NgPacketCodec.cs`
  - NG response frames
  - MIX commands
  - OLD 544-byte frames for BigBuf download
- `Pm3UsbApi/Native/Transport/Pm3SerialTransport.cs`
  - USB CDC serial transport
  - port open/close
  - ping
  - send/wait response
  - BigBuf download
- `Pm3UsbApi/Native/Pm3NativeExecutor.cs`
  - implements `IPm3CommandExecutor`
  - maps typed commands to native protocol operations
- `Pm3UsbApi/Native/T55/Pm3T55NativeService.cs`
  - token-scoped T55 detect/read service
- `Pm3UsbApi/Native/Demod/Pm3LfDemod.cs`
  - ASK/Manchester demod support for elevator token profile
- `Pm3UsbApi/Native/Pm3NativeOutputBuilder.cs`
  - creates synthetic CLI-style output lines consumed by existing parsers

Native verified capabilities from prior session:

| Capability | Native | Process |
|---|---:|---:|
| Connect / ping / hw version | ✅ | ✅ |
| LF tune | ✅ | ✅ |
| T55 detect | ✅ | ✅ |
| T55 read blocks 0–7 | ✅ | ✅ |
| Token baseline, block 5 = 50 rides | ✅ | ✅ |
| Integration tests, read path | 10 pass / 7 skip | 17 pass |

Native read path scope is intentionally narrow:

- Token profile: elevator T55x7 tokens
- Known config block: `0x00148040`
- Modulation: ASK / Manchester
- Bit rate: RF/64
- Sample conversion: `raw[i] - 127`, matching proxmark3 `getSamplesFromBufEx`

## Native limitations before Slice 3

`Pm3NativeExecutor` currently rejects:

- `T55WriteBlockCommand`
- `T55DumpCommand`
- `CliPassthroughCommand`

Current behavior:

```csharp
T55WriteBlockCommand or T55DumpCommand =>
    throw new Pm3CommandException($"{commands[0].GetType().Name} is not supported by the native executor yet.");

CliPassthroughCommand =>
    throw new Pm3CommandException("Raw CLI commands are not supported by the native executor.");
```

Raw CLI passthrough should remain unsupported in native mode by design.

## Integration tests

Main file:

- `Pm3UsbApi.Tests/Integration/Pm3IntegrationTests.cs`

It is parameterized:

```csharp
[TestFixture(Pm3ExecutorKind.Process)]
[TestFixture(Pm3ExecutorKind.Native)]
```

Native currently skips write/dump/raw CLI/sequential write tests through feature flags:

```csharp
private bool SupportsWrite => _executorKind == Pm3ExecutorKind.Process;
private bool SupportsDump => _executorKind == Pm3ExecutorKind.Process;
private bool SupportsCliPassthrough => _executorKind == Pm3ExecutorKind.Process;
```

After Slice 3 native write/dump is implemented and verified:

- enable write for native
- enable dump for native
- keep raw CLI passthrough process-only
- enable or adjust sequential session test for native

Integration test options are built in:

- `Pm3UsbApi.Tests/Integration/IntegrationTestOptions.cs`

Relevant env var:

- `PM3_DEVICE_PORT`
  - set to a port such as `COM4`, or leave unset/`auto` for auto-discovery

Typical integration test command:

```bash
dotnet test --filter "Category=Integration" -- NUnit.RunExplicitTests=true
```

## Safety requirements for Slice 3 write

Blocks 5 and 6 are confirmed safe write targets for integration tests.

The native implementation must preserve the same public safeguards already enforced by `Pm3.WritePage0BlockAsync`:

- block 0 is forbidden
- block 7 is forbidden
- blocks greater than 7 are invalid
- only page 0 blocks 1–6 are writable through the public high-level API

Current public guard in `Pm3.cs`:

```csharp
if (block == 0)
    throw new ArgumentException("Block 0 (configuration) is forbidden for this tool, it is too dangerous to write to. NEVER WRITE TO BLOCK 0.", nameof(block));
if (block == 7)
    throw new ArgumentException("Block 7 (password) is forbidden for this tool, it is too dangerous to write to. NEVER WRITE TO BLOCK 7.", nameof(block));
if (block > 7)
    throw new ArgumentOutOfRangeException(nameof(block), "Block must be between 1 and 6.");
```

Do not bypass these safeguards. If adding lower-level native service methods that technically can write any block, keep them internal and ensure the high-level API remains guarded.

## Slice 3 goal: native T55 write + dump

### 1. Implement `CMD_LF_T55XX_WRITEBL` (`0x0215`)

Command code already exists:

```csharp
public const ushort CmdLfT55XxWriteBl = 0x0215;
```

Payload from Proxmark3 `include/pm3_cmd.h`:

```c
typedef struct {
    uint32_t data;
    uint32_t pwd;
    uint8_t blockno;
    uint8_t flags;
} PACKED t55xx_write_block_t;
```

Client-side flag construction from `cmdlft55xx.c`:

```c
flags  = (usepwd)   ? 0x1 : 0;
flags |= (page1)    ? 0x2 : 0;
flags |= (testMode) ? 0x4 : 0;
flags |= (downlink_mode << 3);
```

For current page0/no-password/non-test writes:

```csharp
flags = (byte)(config.DownlinkMode << 3);
```

Expected firmware response:

- command: `CMD_LF_T55XX_WRITEBL`
- status: `PM3_SUCCESS`
- no data payload

Recommended implementation location:

- Add `WriteBlock(...)` to `Pm3UsbApi/Native/T55/Pm3T55NativeService.cs`
- Wire `T55WriteBlockCommand` in `Pm3UsbApi/Native/Pm3NativeExecutor.cs`

Recommended behavior:

1. Require an active detected config, same as read.
2. Build 10-byte payload little-endian:
   - data: 4 bytes little-endian
   - password: 4 bytes little-endian
   - block number: 1 byte
   - flags: 1 byte
3. Send `CMD_LF_T55XX_WRITEBL` and wait for same command response.
4. Treat non-success status as failure.
5. Verify by reading the block back through the already-working native read path.
6. Return synthetic CLI-style success lines, or throw `Pm3CommandException` with a useful `CommandResult`.

### 2. Implement native dump

Lowest-risk approach: build native dump from repeated reads, not `RESET_READ`.

Rationale:

- Native per-block read is already verified.
- Existing `DumpParser` only needs table-like lines containing block number and 8-hex value.
- This avoids adding reset-read demod complexity in Slice 3.

Recommended behavior:

1. Ensure detect/config is active.
2. Read page 0 blocks 0 through 7 with `Pm3T55NativeService.ReadBlock`.
3. Build synthetic dump output via `Pm3NativeOutputBuilder` that matches `DumpParser` regex:

```text
[+] Page 0
[+] blk | hex data | binary                           | ascii
[+] ----+----------+----------------------------------+-------
[+] 0   | 00148040 | ...
[+] 1   | XXXXXXXX | ...
...
```

The parser regex accepts lines shaped like:

```regex
^\s*(?:\[\+\]\s*)?(\d+)\s+\|\s+([0-9A-Fa-f]{8})\b
```

So binary/ascii columns are optional from the parser’s perspective, but table lines should remain human-readable.

Alternative later path:

- `CMD_LF_T55XX_RESET_READ` (`0x0216`) can reset-read all page 0 data into BigBuf, then demod once. This is listed as Stage B item 8c.4, but repeated read is safer for Slice 3.

### 3. Wire executor batch support

Currently native batch support only accepts detect/read:

```csharp
Native executor batch supports T55 detect/read only.
```

Update `ExecuteBatchAsync` to support:

- detect + write
- detect + dump
- existing detect + read

Remember that `Pm3Session.ExecuteT55Async(new T55WriteBlockCommand(...))` sends two commands in one batch:

```csharp
T55DetectCommand, T55WriteBlockCommand
```

Same for dump.

### 4. Update output builder

Likely additions to `Pm3NativeOutputBuilder`:

- `BuildWriteBlockLines(uint block, uint data)`
- `BuildWriteFailedLines(uint block)`
- `BuildDumpLines(IReadOnlyList<uint> blockValues)`

Existing parsers should continue to work without changes.

### 5. Update tests

Suggested unit tests:

- payload construction for native write, if helper is exposed internally
- output builder dump lines parse with `DumpParser`
- output builder write lines have no error markers

Integration test changes after hardware verification:

- `SupportsWrite` should become true for native
- `SupportsDump` should become true for native
- `SupportsCliPassthrough` should remain process-only
- Native should run these previously skipped tests:
  - `WriteBlock5_ThenRead_MatchesWrittenValue`
  - `WriteBlock6_ThenRead_MatchesWrittenValue`
  - `Dump_ReturnsExpectedBlockCount`
  - `Dump_Block5MatchesIndividualRead`
  - `SequentialSession_ExecutesTenOperationsWithoutFailure` if write/dump are both stable

Keep fixture snapshot/restore behavior intact. It snapshots blocks 5/6 before write tests and restores them afterward.

## Useful Proxmark3 source references

Local source paths found on this machine:

- `C:/Users/itgeorge/proxmark3/include/pm3_cmd.h`
- `C:/Users/itgeorge/proxmark3/client/src/cmdlft55xx.c`
- `C:/Users/itgeorge/proxmark3/armsrc/lfops.c`
- same source also exists under `C:/Users/itgeorge/ProxSpace/pm3/proxmark3/...`

Relevant references:

- `pm3_cmd.h`
  - `t55xx_write_block_t`
  - command code defines
- `cmdlft55xx.c`
  - `t55xxWrite(...)`
  - `CmdT55xxDump(...)`
  - `CmdT55xxResetRead(...)`
- `lfops.c`
  - firmware handling for `CMD_LF_T55XX_WRITEBL`
  - firmware handling for `CMD_LF_T55XX_RESET_READ`

## Slice 4 goal: production enablement / polish

After Slice 3, planned Slice 4 work:

- macOS USB CDC validation (`/dev/cu.usbmodem*`)
- Linux USB CDC validation (`/dev/ttyACM0`)
- possibly improve native port discovery without relying on pm3 script on Unix
- decide whether/when to default `RidesCli` to native executor
- keep or move debug tooling:
  - `NativeT55Probe/`
  - `scripts/check-com4.ps1`
- optional `CMD_CAPABILITIES` (`0x0112`)
- optional detect optimization:
  - cache known config/downlink mode/inversion
  - avoid trying all 4 downlink modes × 2 inversions when config is already known
- optional broader demod support beyond elevator-token ASK/Manchester profile

## Recommended next step

Before implementing Slice 3, make a small cleanup/status commit containing:

- `AskDemodExt` trace removal
- `NativeT55Probe` call-site update
- `pm3-implementation-plan-stage-b.md` status update
- this handoff file

Then implement native write first, verify with block 5 or 6, then implement dump using repeated reads.
