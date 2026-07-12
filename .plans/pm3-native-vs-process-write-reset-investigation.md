# PM3 native vs process T55 write/reset investigation

Date: 2026-07-12

## Scope and safety constraints used

- Started read-only: connect/tune/read blocks 0..7 before any writes.
- Did **not** run `RidesCli reset --sequence ...`.
- Did **not** intentionally change identity data; only no-op writes of current values back to the same blocks were attempted.
- Did not write block 0 or block 7.

## Initial hardware/token state

Command:

```bash
printf 'connect\ntune\nread 0\nread 1\nread 2\nread 3\nread 4\nread 5\nread 6\nread 7\nexit\n' | dotnet run --project Pm3Cli
```

Result:

```text
Connected to Proxmark3.
Peak: 59812 mV
Block 0: 00148040
Block 1: 9BFE0062
Block 2: 5BA4A3DE
Block 3: D6D1C733
Block 4: D6D1C733
Block 5: 3FC6BD93
Block 6: 3FC6BD93
Block 7: 00000000
```

PM3 port from logs: `/dev/cu.usbmodem1201`.

Interpretation:

- Token is readable and signal looked strong (`59812 mV`).
- Blocks 5/6 mirror and decode as 500 rides via `RidesCli read` with native executor.
- Identity is hybrid/inconsistent: block 1/2 match Mercury reset values, block 3/4 match Venus values, ride blocks are Mercury 500.

Native `RidesCli` read command:

```bash
printf 'read\nexit\n' | dotnet run --project RidesCli
```

Result:

```text
rides remaining: 500
```

Process `RidesCli` read command:

```bash
printf 'read\nexit\n' | PM3_EXECUTOR=process dotnet run --project RidesCli
```

Result: process executor could not connect; see process section below.

## Harness used for no-op writes

Added a small debug harness at:

```text
debug/NoOpWriteCompare/
```

Build:

```bash
dotnet build debug/NoOpWriteCompare/NoOpWriteCompare.csproj -v:minimal
```

Single-block mode reads each target block, writes the same value back, then immediately reads it again:

```bash
dotnet run --project debug/NoOpWriteCompare -- --executor native --mode single --blocks 2,1,5,6
```

Batch mode reads blocks 0..7, then calls:

```csharp
await pm3.WriteAndVerifyPage0BlocksAsync(before, 1, 6);
```

where `before[1]..before[6]` are the current token values, making the batch a no-op data write:

```bash
dotnet run --project debug/NoOpWriteCompare -- --executor native --mode batch
```

## Native executor results

### Single no-op writes

Command:

```bash
dotnet run --project debug/NoOpWriteCompare -- --executor native --mode single --blocks 2,1,5,6
```

PM3 logs:

```text
/var/folders/ty/cs9d984d4s926vpqm0df5_sc0000gn/T/elevator/pm3-59335-20260712104017-session.log
/var/folders/ty/cs9d984d4s926vpqm0df5_sc0000gn/T/elevator/pm3-59335-20260712104017-errors.log
```

Result summary:

```text
block 2 before=5BA4A3DE write returned, after=5BA4A3DE, matches=True
block 1 before=9BFE0062 write returned, after=9BFE0062, matches=True
block 5 before=3FC6BD93 write returned, after=3FC6BD93, matches=True
block 6 before=3FC6BD93 write returned, after=3FC6BD93, matches=True
```

### Batch no-op write/verify, blocks 1..6

Command:

```bash
dotnet run --project debug/NoOpWriteCompare -- --executor native --mode batch
```

PM3 logs:

```text
/var/folders/ty/cs9d984d4s926vpqm0df5_sc0000gn/T/elevator/pm3-59350-20260712104027-session.log
/var/folders/ty/cs9d984d4s926vpqm0df5_sc0000gn/T/elevator/pm3-59350-20260712104027-errors.log
```

Result summary:

```text
BEFORE 0..7:
00148040 9BFE0062 5BA4A3DE D6D1C733 D6D1C733 3FC6BD93 3FC6BD93 00000000

BATCH_RESULT executor=Native first=1 last=6 ok=True

AFTER 0..7:
00148040 9BFE0062 5BA4A3DE D6D1C733 D6D1C733 3FC6BD93 3FC6BD93 00000000
```

The native batch command in the session log was:

```text
lf t55 write -b 1 -d 9BFE0062; lf t55 write -b 2 -d 5BA4A3DE; lf t55 write -b 3 -d D6D1C733; lf t55 write -b 4 -d D6D1C733; lf t55 write -b 5 -d 3FC6BD93; lf t55 write -b 6 -d 3FC6BD93; lf t55 read -b 1; lf t55 read -b 2; lf t55 read -b 3; lf t55 read -b 4; lf t55 read -b 5; lf t55 read -b 6
```

It completed OK in about 7.4 seconds.

## Process executor results

Process executor comparison was blocked before any token read/write, because the installed `proxmark3` process client aborts on startup.

Commands:

```bash
printf 'read\nexit\n' | PM3_EXECUTOR=process dotnet run --project RidesCli

dotnet run --project debug/NoOpWriteCompare -- --executor process --mode single --blocks 2,1,5,6

set -o pipefail; proxmark3 --list 2>&1 | head -80; echo exit:${PIPESTATUS[0]}
```

PM3 logs:

```text
/var/folders/ty/cs9d984d4s926vpqm0df5_sc0000gn/T/elevator/pm3-58724-20260712103808-session.log
/var/folders/ty/cs9d984d4s926vpqm0df5_sc0000gn/T/elevator/pm3-58724-20260712103808-errors.log
/var/folders/ty/cs9d984d4s926vpqm0df5_sc0000gn/T/elevator/pm3-59364-20260712104045-session.log
/var/folders/ty/cs9d984d4s926vpqm0df5_sc0000gn/T/elevator/pm3-59364-20260712104045-errors.log
/var/folders/ty/cs9d984d4s926vpqm0df5_sc0000gn/T/elevator/pm3-60370-20260712104855-session.log
/var/folders/ty/cs9d984d4s926vpqm0df5_sc0000gn/T/elevator/pm3-60370-20260712104855-errors.log
```

Observed failure:

```text
CONNECT_THROW executor=Process type=Pm3ConnectionException message=Failed to connect to Proxmark3. Exit code 134

connect start executor=Process port=/dev/cu.usbmodem1201
>>> hw version
<<< FAIL exit=134 Exit code 134
```

Raw `proxmark3 --list` failure:

```text
dyld: Library not loaded: /opt/homebrew/opt/lua/lib/liblua.dylib
Referenced from: /opt/homebrew/Cellar/proxmark3/4.20728/bin/proxmark3
Reason: tried: '/opt/homebrew/opt/lua/lib/liblua.dylib' ... no such file
exit:134
```

`otool -L /opt/homebrew/bin/proxmark3` confirms it references:

```text
/opt/homebrew/opt/lua/lib/liblua.dylib (compatibility version 5.4.0, current version 5.4.8)
```

but `/opt/homebrew/opt/lua/lib/liblua.dylib` is not present.

## Final token state after no-op investigation

Final read-only check after no-op tests:

```bash
printf 'connect\nread 0\nread 1\nread 2\nread 3\nread 4\nread 5\nread 6\nread 7\nexit\n' | dotnet run --project Pm3Cli
```

Result remained unchanged at that point:

```text
Block 0: 00148040
Block 1: 9BFE0062
Block 2: 5BA4A3DE
Block 3: D6D1C733
Block 4: D6D1C733
Block 5: 3FC6BD93
Block 6: 3FC6BD93
Block 7: 00000000
```

## Destructive native single-block Venus reset-style write test

User approved a destructive write test to check whether native single-block writes are problematic. The harness was extended with:

```bash
dotnet run --project debug/NoOpWriteCompare -- --executor native --mode venus-reset-single
```

Safety guard:

- only blocks 1..6 are targeted;
- block 0 and block 7 remain forbidden;
- each block is written individually, immediately read back, and the harness stops on the first write/verify failure.

PM3 logs:

```text
/var/folders/ty/cs9d984d4s926vpqm0df5_sc0000gn/T/elevator/pm3-60435-20260712105654-session.log
/var/folders/ty/cs9d984d4s926vpqm0df5_sc0000gn/T/elevator/pm3-60435-20260712105654-errors.log
```

Baseline before destructive writes:

```text
BEFORE block=0 hex=00148040
BEFORE block=1 hex=9BFE0062
BEFORE block=2 hex=5BA4A3DE
BEFORE block=3 hex=D6D1C733
BEFORE block=4 hex=D6D1C733
BEFORE block=5 hex=3FC6BD93
BEFORE block=6 hex=3FC6BD93
BEFORE block=7 hex=00000000
```

Single-block write/verify results:

```text
block 1 intended=43FE0062 readback=43FE0062 ok=True
block 2 intended=5BA494A3 readback=5BA494A3 ok=True
block 3 intended=D6D1C733 readback=D6D1C733 ok=True
block 4 intended=D6D1C733 readback=D6D1C733 ok=True
block 5 intended=48C74948 readback=48C74948 ok=True
block 6 intended=48C74948 readback=48C74948 ok=True
```

Final blocks:

```text
FINAL block=0 hex=00148040
FINAL block=1 hex=43FE0062
FINAL block=2 hex=5BA494A3
FINAL block=3 hex=D6D1C733
FINAL block=4 hex=D6D1C733
FINAL block=5 hex=48C74948
FINAL block=6 hex=48C74948
FINAL block=7 hex=00000000
```

Validation read:

```bash
printf 'read\nexit\n' | dotnet run --project RidesCli
```

Result:

```text
rides remaining: 0
```

## Destructive native single-block Mercury zero-rides write test

User then requested another single-block sequence to set the tag to Mercury encoding with 0 rides. The harness was extended with:

```bash
dotnet run --project debug/NoOpWriteCompare -- --executor native --mode mercury-reset-single
```

Targets:

```text
block 1 -> 9BFE0062
block 2 -> 5BA4A3DE
block 3 -> D5D1D713
block 4 -> D5D1D713
block 5 -> CCC749CC  # EncodingSequences.Mercury.Encode(0)
block 6 -> CCC749CC  # EncodingSequences.Mercury.Encode(0)
```

There was one initial connection attempt that failed with `UnauthorizedAccessException` because a stale `RidesCli` process still held `/dev/cu.usbmodem1201`; after killing that process, the test ran successfully.

PM3 logs for the successful run:

```text
/var/folders/ty/cs9d984d4s926vpqm0df5_sc0000gn/T/elevator/pm3-60575-20260712110519-session.log
/var/folders/ty/cs9d984d4s926vpqm0df5_sc0000gn/T/elevator/pm3-60575-20260712110519-errors.log
```

Baseline before Mercury writes:

```text
BEFORE block=0 hex=00148040
BEFORE block=1 hex=43FE0062
BEFORE block=2 hex=5BA494A3
BEFORE block=3 hex=D6D1C733
BEFORE block=4 hex=D6D1C733
BEFORE block=5 hex=48C74948
BEFORE block=6 hex=48C74948
BEFORE block=7 hex=00000000
```

Single-block write/verify results:

```text
block 1 intended=9BFE0062 readback=9BFE0062 ok=True
block 2 intended=5BA4A3DE readback=5BA4A3DE ok=True
block 3 intended=D5D1D713 readback=D5D1D713 ok=True
block 4 intended=D5D1D713 readback=D5D1D713 ok=True
block 5 intended=CCC749CC readback=CCC749CC ok=True
block 6 intended=CCC749CC readback=CCC749CC ok=True
```

Final blocks:

```text
FINAL block=0 hex=00148040
FINAL block=1 hex=9BFE0062
FINAL block=2 hex=5BA4A3DE
FINAL block=3 hex=D5D1D713
FINAL block=4 hex=D5D1D713
FINAL block=5 hex=CCC749CC
FINAL block=6 hex=CCC749CC
FINAL block=7 hex=00000000
```

Validation read:

```bash
printf 'read\nexit\n' | dotnet run --project RidesCli
```

Result:

```text
rides remaining: 0
```

## Reset implementation update

Implemented reset-specific per-block write/verify logic in `RidesCli/RidesCommandHandler.cs`:

- reset no longer calls batched `WriteAndVerifyPage0BlocksAsync(resetBlocks, 1, 6)`;
- it reads/snapshots current blocks 1..6 before prompting;
- if current blocks 1..4 match the requested reset image and blocks 5/6 are from the requested sequence, it only resets ride blocks 5/6 to `sequence.Encode(0)`;
- otherwise it writes the required target blocks in 1..6 order;
- each changed block is written individually, immediately read/verified, retried once after 500 ms on failure, and uses independent retry counts per block;
- if a block still fails, it best-effort rolls blocks 1..6 back to the pre-reset snapshot and reports rollback success/failures;
- block 0 and block 7 remain outside the reset writer's allowed range.

Added tests in `RidesCli.Tests/RidesCommandHandlerTests.cs` for:

- same-sequence reset only writing blocks 5/6;
- cross-sequence reset writing blocks 1..6 individually without the batch writer;
- failure retry once followed by rollback to previous values.

Validation commands:

```bash
dotnet test RidesCli.Tests/RidesCli.Tests.csproj -v:minimal
dotnet test ElevatorTokens.sln -v:minimal
```

`RidesCli.Tests` passed 71/71. Full solution `dotnet test` exited 0; populated test assemblies passed.

## Reset roundtrip hardware integration test

Added hardware integration harness:

```text
debug/ResetRoundtripIntegration/
```

Run command:

```bash
dotnet run --project debug/ResetRoundtripIntegration
```

PM3 logs:

```text
/var/folders/ty/cs9d984d4s926vpqm0df5_sc0000gn/T/elevator/pm3-60737-20260712112429-session.log
/var/folders/ty/cs9d984d4s926vpqm0df5_sc0000gn/T/elevator/pm3-60737-20260712112429-errors.log
```

Hardware result:

- baseline read decoded 0 rides on Mercury;
- reset Mercury -> Venus succeeded and verified blocks 1..6:
  - `43FE0062 5BA494A3 D6D1C733 D6D1C733 48C74948 48C74948`
- reset Venus -> Mercury succeeded and verified blocks 1..6:
  - `9BFE0062 5BA4A3DE D5D1D713 D5D1D713 CCC749CC CCC749CC`
- final read decoded 0 rides.

## Conclusion

- Current result is still **inconclusive for native-vs-process reset behavior**, because the old process executor cannot currently start the installed Homebrew `proxmark3` client.
- Native USB no-op writes did **not** reproduce the block-2 failure on the current token:
  - individual no-op writes for blocks 2, 1, 5, 6 all returned successfully and verified by immediate readback;
  - `WriteAndVerifyPage0BlocksAsync(currentBlocks, 1, 6)` also returned `true` and left all blocks unchanged.
- Native USB destructive **single-block** Venus reset-style writes also did **not** reproduce the failure:
  - block 1 changed from Mercury to Venus successfully;
  - block 2 then changed from Mercury to Venus successfully;
  - blocks 3..6 all wrote and verified successfully;
  - final `RidesCli read` decoded 0 rides.
- Native USB destructive **single-block** Mercury zero-rides writes also did **not** reproduce the failure:
  - block 1 changed from Venus to Mercury successfully;
  - block 2 then changed from Venus to Mercury successfully;
  - blocks 3..6 all wrote and verified successfully;
  - final `RidesCli read` decoded 0 rides.
- The evidence now points away from a fundamental native single-block write problem for this token/placement and toward the reset failure being related to the multi-command batch path, timing/settle behavior under batch writes, exception/retry handling, process/native differences, or placement/token intermittency.

## Recommended next steps

1. Repair or reinstall the process `proxmark3` client dependency (`liblua.dylib`) and rerun the no-op/process comparison with the same harness.
2. After process can connect, run process single no-op writes for blocks 2, 1, 5, 6, then process batch no-op write/verify for blocks 1..6.
3. Strongly consider replacing reset's current batched `WriteAndVerifyPage0BlocksAsync(resetBlocks, 1, 6)` with a reset-specific per-block writer that writes/verifies one block at a time and stops on first failure; both destructive single-block reset-image tests completed successfully.
4. Independently consider making `WriteAndVerifyPage0BlocksAsync`/native batch handling more resilient to per-block write exceptions so one thrown write does not hide which later blocks were skipped.
