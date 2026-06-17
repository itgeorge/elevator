# PM3 Native Integration — Master Plan & Handoff

**Branch:** `pm3-integration` (not merged to `master` yet)  
**Last updated:** 2026-06-18  
**Purpose:** Single source of truth for the multi-stage effort to replace the `proxmark3` process wrapper with direct USB CDC binary protocol communication. Use this file to onboard the next agent.

---

## Quick Status

| Stage / Slice | Scope | Status |
|---------------|--------|--------|
| **Stage A** | Process-wrapper executor (`proxmark3 -c "..."`) | ✅ Complete |
| **Slice 1** | Native connect, ping, hw version, LF tune | ✅ Complete (`00a9ac8`) |
| **Slice 2** | Native T55 detect + read, demod, integration tests | ✅ Complete (`828ae8d`, `8f2ef38`) |
| **Slice 3** | Native T55 write + dump | ✅ Complete (`52392ba`) |
| **Slice 4** | Production enablement, cross-platform validation | 🔲 Mostly done (see below) |
| **Slice 5** | Native default executor | ✅ Complete |
| **Slice 7** | Diagnostic logging (temp files) | ✅ Complete |
| **Slice 11** | Debug tooling → `debug/` | ✅ Complete |
| **Slices 6, 8–10** | Capabilities, cache, perf, modulation | 🔲 Planned — see handoff docs |

**Recent commits on `pm3-integration`:**

```
c6d5607 Fix native PM3 BigBuf download hang and validate on macOS
9c6436f Default RidesCli to native PM3 executor
52392ba Implement native T55 write and dump
828ae8d Fix native T55 detect/read hang (Slice 2 complete)
00a9ac8 Add native USB CDC executor for connect and LF tune (Slice 1)
```

---

## Goal

Replace **`Pm3ProcessExecutor`** (spawns installed `proxmark3` client) with **`Pm3NativeExecutor`** (speaks Proxmark3 NG/MIX/OLD frames over USB CDC serial). The public `Pm3` API, session layer, parsers, and CLI tooling stay unchanged.

```
Pm3 / RidesCli / tests
        ↓
   Pm3Session (detect chaining, port discovery, transcripts)
        ↓
   IPm3CommandExecutor
     ├── Pm3ProcessExecutor   [Stage A — wraps proxmark3 -c "..."]
     └── Pm3NativeExecutor    [Stage B — direct binary protocol]
```

**Executor selection:**

| Context | Default | Override |
|---------|---------|----------|
| `Pm3Options` | `Native` (Slice 5) | `PM3_EXECUTOR=process` |
| `RidesCli` / `Pm3Cli` | `Native` | `PM3_EXECUTOR=process` |

---

## Capability Matrix (Current)

| Capability | Native | Process |
|------------|:------:|:-------:|
| Connect / ping / hw version | ✅ | ✅ |
| LF tune | ✅ | ✅ |
| T55 detect | ✅ | ✅ |
| T55 read blocks 0–7 | ✅ | ✅ |
| T55 write blocks 1–6 | ✅ | ✅ |
| T55 dump | ✅ | ✅ |
| Raw CLI passthrough | ❌ (by design) | ✅ |

Native read/write scope is intentionally narrow: elevator T55x7 tokens, ASK/Manchester, RF/64, config block `0x00148040`. Dump uses eight sequential reads (not `RESET_READ`).

---

## Key Source Files

| Area | Path |
|------|------|
| Public API | `Pm3UsbApi/Pm3.cs` |
| Executor seam | `Pm3UsbApi/Execution/IPm3CommandExecutor.cs` |
| Process executor | `Pm3UsbApi/Execution/Pm3ProcessExecutor.cs` |
| Native executor | `Pm3UsbApi/Native/Pm3NativeExecutor.cs` |
| Packet codec | `Pm3UsbApi/Native/Protocol/Pm3NgPacketCodec.cs` |
| Serial transport | `Pm3UsbApi/Native/Transport/Pm3SerialTransport.cs` |
| T55 native service | `Pm3UsbApi/Native/T55/Pm3T55NativeService.cs` |
| LF demod | `Pm3UsbApi/Native/Demod/Pm3LfDemod.cs` |
| Synthetic CLI output | `Pm3UsbApi/Native/Pm3NativeOutputBuilder.cs` |
| Typed commands | `Pm3UsbApi/Commands/` |
| Integration tests | `Pm3UsbApi.Tests/Integration/Pm3IntegrationTests.cs` |
| Debug probe | `debug/NativeT55Probe/Program.cs` |
| Diagnostic logs | `Pm3UsbApi/Diagnostics/Pm3DiagnosticLog.cs` |

---

## Stage A: Process Wrapper — TODOs

Stage A is complete. All items checked for historical reference.

### Phase 1: Foundation

- [x] **1.1** `Pm3Options.cs` — configuration record
- [x] **1.2** `CommandResult.cs` — command result model
- [x] **1.3** `Pm3Exception.cs` — exception hierarchy
- [x] **1.4** `IPm3CommandExecutor.cs` — executor abstraction
- [x] **1.5** Project builds

### Phase 2: Process Executor

- [x] **2.1** `OutputParser.cs` — ANSI strip, error detection
- [x] **2.2** `Pm3ProcessExecutor.cs` — per-invocation `proxmark3 -c "..."`
- [x] **2.3** pm3 client auto-detection (`ResolvePm3ClientPath`)
- [x] **2.4** Windows-specific handling (ProxSpace, `pm3.bat`)
- [x] **2.5** Manual smoke test harness (`Pm3SmokeTest`)

### Phase 3: Session Layer

- [x] **3.1** `Pm3Session.cs`
- [x] **3.2** `ConnectAsync` — hw version verification
- [x] **3.3** `DisconnectAsync`
- [x] **3.4** `IsConnectedAsync`
- [x] **3.5** T55 command chaining (detect before read/write/dump)
- [x] **3.6** Raw command execution
- [x] **3.7** Transcript logging (optional)
- [x] **3.8** End-to-end session verification

### Phase 4: Parsers & Unit Tests

- [x] **4.1** `DetectParser`
- [x] **4.2** `TuneParser`
- [x] **4.3** `BlockReadParser`
- [x] **4.4** `DumpParser`
- [x] **4.5** `Pm3UsbApi.Tests` project
- [x] **4.6** Real pm3 output fixtures
- [x] **4.7** Parser unit tests
- [x] **4.8** `OutputParser` unit tests
- [x] **4.9** All tests pass

### Phase 5: Public API (`Pm3.cs`)

- [x] **5.1** Constructor and lifecycle
- [x] **5.2** `ConnectAsync`
- [x] **5.3** `DisconnectAsync`
- [x] **5.4** `IsConnectedAsync`
- [x] **5.5** `EnsureT55SessionActive`
- [x] **5.6** `ReadPage0BlockAsync`
- [x] **5.7** `WritePage0BlockAsync` (blocks 0 and 7 forbidden)
- [x] **5.8** `DumpAsync`
- [x] **5.9** `StartLfTuneAsync`
- [x] **5.10** `GetLfTuneLastMilliVoltsAsync`
- [x] **5.11** `StopLfTuneAsync` (no-op for per-invocation)
- [x] **5.12** `CancellationToken` on all public async methods
- [x] **5.13** Manual hardware smoke test

### Phase 6–7: CLI & Integration

- [x] **6.1** `Pm3Cli` project reference
- [x] **6.2** `Pm3CliProgram.cs` argument parsing
- [x] **6.3** CLI commands (connect, detect, read, write, dump, tune)
- [x] **6.4** User-friendly error handling
- [ ] **6.5** Full manual CLI hardware test pass *(optional maintenance)*
- [x] **7.1** Full manual smoke test via `Pm3Cli`
- [x] **7.2** `Pm3IntegrationTests.cs` (hardware-in-loop)
- [x] **7.3** Output format documentation
- [x] **7.4** Review and cleanup

### Post–Stage A enhancements (also complete)

- [x] Replace string command batches with typed `IPm3DeviceCommand`
- [x] Parameterized integration tests (`Process` + `Native` fixtures)
- [x] Fixture token snapshot/restore for blocks 5 and 6

---

## Stage B: Native Binary Protocol — TODOs

### Slice 1: Connect + LF Tune ✅

- [x] **S1.1** Add `System.IO.Ports` to `Pm3UsbApi.csproj`
- [x] **S1.2** `Pm3NgPacketCodec` — NG + OLD 544-byte MIX frames
- [x] **S1.3** `Pm3SerialTransport` — open/send/receive/ping/BigBuf
- [x] **S1.4** `CMD_PING` (0x0109) and `CMD_VERSION` (0x0107)
- [x] **S1.5** Packet codec unit tests (`Pm3NgPacketCodecTests.cs`)
- [x] **S1.6** `CMD_MEASURE_ANTENNA_TUNING_LF` (0x0402) — LF tune
- [x] **S1.7** `Pm3NativeExecutor` — hw version + LF tune dispatch
- [x] **S1.8** `Pm3NativeOutputBuilder` — synthetic CLI-style lines
- [x] **S1.9** `Pm3Options.ExecutorKind` + `PM3_EXECUTOR` env var
- [x] **S1.10** `Pm3.cs` wires native executor when selected

### Slice 2: T55 Detect + Read ✅

- [x] **S2.1** `CMD_LF_T55XX_READBL` (0x0214)
- [x] **S2.2** `CMD_DOWNLOAD_BIGBUF` / `CMD_DOWNLOADED_BIGBUF` (0x0207/0x0208)
- [x] **S2.3** `Pm3LfDemod` — ASK/Manchester demod for elevator token profile
- [x] **S2.4** `Pm3T55NativeService` — detect (4 downlink modes × 2 inversions) + read
- [x] **S2.5** Sample conversion `raw[i] - 127` (matches proxmark3 `getSamplesFromBufEx`)
- [x] **S2.6** Read majority-vote / retry logic
- [x] **S2.7** Fix detect/read hang (Slice 2 completion commit `828ae8d`)
- [x] **S2.8** Native executor batch support for detect + read
- [x] **S2.9** Integration tests: native read path (10 pass / 7 skip before Slice 3)
- [x] **S2.10** `NativeT55Probe` debug tool for low-level T55 probing

### Slice 3: T55 Write + Dump ✅

- [x] **S3.1** `CMD_LF_T55XX_WRITEBL` (0x0215) in `Pm3T55NativeService.WriteBlock`
  - 10-byte little-endian payload (`data`, `password`, `blockno`, `flags`)
  - Flags: `(downlinkMode << 3)` for page-0/no-password writes
- [x] **S3.2** Write retry logic (3 attempts, RF settle delay, read-back verify)
- [x] **S3.3** `Pm3NativeOutputBuilder` — write success/failure lines
- [x] **S3.4** Native dump via repeated `ReadBlock` (blocks 0–7), not `RESET_READ`
- [x] **S3.5** `Pm3NativeOutputBuilder.BuildDumpLines` — `DumpParser`-compatible table
- [x] **S3.6** `Pm3NativeExecutor` — single + batched detect/write/dump paths
- [x] **S3.7** Unit tests: write payload/flags (`Pm3T55NativeServiceTests`), dump output (`Pm3NativeOutputBuilderTests`)
- [x] **S3.8** Integration tests: `SupportsWrite` and `SupportsDump` enabled for native
- [x] **S3.9** `RidesCli` defaults to native executor (`9c6436f`)

### Slice 4: Production Enablement 🔲

- [x] **S4.1** macOS validation — USB CDC as `/dev/cu.usbmodem*` (native detect/read/dump verified 2026-06-17)
- [~] **S4.2** Linux validation — **deferred** (no Linux hardware; revisit when `/dev/ttyACM0` available)
- [x] **S4.3** Native port discovery on Unix without requiring pm3 install (ioreg + sysfs fallback)
- [x] **S4.4** Global default: `Pm3Options` and env helper default to `Native` (Slice 5)
- [x] **S4.5** Run full native integration suite on macOS hardware (19 pass / 2 skip; load test 32 ops)
- [ ] **S4.6** Merge `pm3-integration` → `master` after validation sign-off (deferred)

#### Native hang fix (2026-06-17)

**Symptom:** RidesCli `read` hung after `signal strength` (post-tune). Native `DownloadBigBuf` timed out after successful `CMD_LF_T55XX_READBL`.

**Root cause:** `Pm3SerialTransport.ReadResponseFrame` used a fresh buffer per call and discarded trailing bytes when multiple PM3 OLD/NG frames arrived in one serial read. BigBuf download sends many back-to-back `CMD_DOWNLOADED_BIGBUF` (544-byte OLD) frames; losing tail bytes desynced the stream.

**Fix:** Persistent `_receiveBuffer` across reads; `ClearReceiveBuffer()` before download (mirrors proxmark3 `clearCommandBuffer`); WTX handling; post-LF-tune RF settle.

**Offline regression:** captured fixture `Pm3UsbApi.Tests/Fixtures/Native/t55-block0-samples.bin` + `Pm3T55NativeOfflineTests` (no device required). Re-capture via `dotnet run --project debug/NativeT55Probe -- --capture`.

#### Optional optimizations — moved to Slices 6–11

| Item | Slice | Handoff doc |
|------|-------|-------------|
| S4.7 `CMD_CAPABILITIES` | 6 | [pm3-slice-6-capabilities.md](pm3-slice-6-capabilities.md) |
| S4.8 Dump performance (not RESET_READ) | 10 | [pm3-slice-10-dump-performance.md](pm3-slice-10-dump-performance.md) |
| S4.9 Detect cache (30s TTL, test-first) | 9 | [pm3-slice-9-detect-cache.md](pm3-slice-9-detect-cache.md) |
| S4.10 Unsupported modulation detection | 8 | [pm3-slice-8-unsupported-modulation.md](pm3-slice-8-unsupported-modulation.md) |
| S4.11 Debug tooling → `debug/` | 11 | ✅ [pm3-slice-11-debug-relocation.md](pm3-slice-11-debug-relocation.md) |
| Diagnostic logging (temp files) | 7 | ✅ [pm3-slice-7-logging.md](pm3-slice-7-logging.md) |

Broader demod beyond ASK/Manchester: **explicitly skipped**; use process executor fallback (Slice 8).

### Slice 5: Native Default Executor ✅

- [x] **S5.1** `Pm3Options.ExecutorKind` default → `Native`
- [x] **S5.2** `ReadExecutorKindFromEnvironment()` default → `Native`; `PM3_EXECUTOR=process` overrides
- [x] **S5.3** `RidesCli` uses shared `Pm3Options.ReadExecutorKindFromEnvironment()`
- [x] **S5.4** Unit tests: `Pm3OptionsTests`
- [x] **S5.5** Mark S4.2 deferred, S4.4 done in this plan

---

## Safety Requirements

**Never write blocks 0 or 7** in tests or tooling. Block 0 is configuration; block 7 is password. Incorrect writes can brick the tag.

Public API guards in `Pm3.WritePage0BlockAsync`:

- Block 0 → `ArgumentException`
- Block 7 → `ArgumentException`
- Blocks > 7 → `ArgumentOutOfRangeException`

Integration tests use blocks **5** and **6** only, with snapshot/restore in fixture setup/teardown.

---

## Testing

### Unit tests (no hardware)

```bash
dotnet test --filter "Category!=Integration&Category!=IntegrationParity"
```

**Current result:** 71+ passed (no hardware); integration tests explicit.

### Integration tests (hardware required)

```bash
dotnet test --filter "Category=Integration" -- NUnit.RunExplicitTests=true
```

Parameterized `[TestFixture(Process)]` + `[TestFixture(Native)]`. Native skips only raw CLI passthrough tests.

**Env var:** `PM3_DEVICE_PORT` — e.g. `COM4`, or unset/`auto` for discovery.

### Parity test

`Pm3ExecutorParityTests` — compares LF tune peaks between executors (within 3000 mV tolerance).

---

## Recommended Next Steps for Handoff Agent

**Suggested order (remaining):**

1. [Slice 9](pm3-slice-9-detect-cache.md) — detect cache (30s TTL, test-first)
2. [Slice 8](pm3-slice-8-unsupported-modulation.md) — unsupported modulation error
3. [Slice 6](pm3-slice-6-capabilities.md) — capabilities on connect
4. [Slice 10](pm3-slice-10-dump-performance.md) — dump perf tuning
5. **S4.6** — merge to `master` when ready
6. **S4.2** — Linux validation when hardware available

---

## Archive — Prior Planning Documents

The following files were the working plans and handoff notes during implementation. They are **archived for reference** and may be stale (e.g. Slice 3 listed as "next" in the Stage B doc). **This file supersedes them.**

| File | Description |
|------|-------------|
| [archive/pm3-implementation-plan.md](archive/pm3-implementation-plan.md) | Original Stage A phased plan (Phases 1–7) + Stage B overview |
| [archive/pm3-implementation-plan-stage-b.md](archive/pm3-implementation-plan-stage-b.md) | Stage B sub-phases (8a–8d) with detailed checklist |
| [archive/pm3-communication-implementation-notes.md](archive/pm3-communication-implementation-notes.md) | Design rationale, regex patterns, command mappings, licensing notes |
| [archive/pm3-slice-3-handoff.md](archive/pm3-slice-3-handoff.md) | Slice 3 handoff written before write/dump landed; useful for protocol payload details |

### External Proxmark3 references

- [new_frame_format.md](https://github.com/RfidResearchGroup/proxmark3/blob/master/doc/new_frame_format.md)
- [pm3_cmd.h](https://github.com/RfidResearchGroup/proxmark3/blob/master/include/pm3_cmd.h)
- Local source (if available): `proxmark3/include/pm3_cmd.h`, `client/src/cmdlft55xx.c`, `armsrc/lfops.c`

---

## Notes for Agents

1. **Only the executor layer changes** between Stage A and B. Do not modify parsers or public API unless necessary.
2. **`Pm3Session.ExecuteT55Async`** automatically chains `[T55DetectCommand, command]` — native batch support must handle detect+write and detect+dump.
3. **Raw CLI passthrough** should remain process-only (`CliPassthroughCommand` throws in native executor).
4. **Mark TODOs done** in this file (`[x]`) when completing Slice 4 work and commit alongside code changes.
5. **Never write to blocks 0 or 7** during testing.
