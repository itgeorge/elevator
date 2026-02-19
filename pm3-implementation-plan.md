# Proxmark3 USB API -- Implementation Plan

This plan is designed to be executed sequentially by agents. Each phase builds on the previous one. Agents should mark TODOs as `[x]` after completing each item and committing changes. Agents may modify this plan after consulting with the user.

**Reference:** See `pm3-communication-implementation-notes.md` for detailed design rationale, regex patterns, and command mappings.

---

## Strategy Overview

This plan follows a **two-stage approach**:

**Stage A -- Process Wrapper Prototype (Phases 1-7)**
Use the `proxmark3` client as a subprocess (`-c` flag) to get a working implementation fast. This proves the `Pm3.cs` API surface, parsers, and CLI tooling against real hardware. The pm3 client handles all USB communication and signal processing.

- Requires pm3 client installed on the host machine
  - Windows: via [ProxSpace](https://github.com/Gator96100/ProxSpace) (already working)
  - macOS: `brew install proxmark3` or build from [RRG source](https://github.com/RfidResearchGroup/proxmark3)
  - Linux: `apt install proxmark3` or build from source

**Stage B -- Native Binary Protocol (Phase 8, future)**
Replace the process-wrapper executor with a native C# implementation that speaks the Proxmark3 binary packet protocol (`PacketCommandNG`/`PacketResponseNG`) directly over USB CDC serial. This eliminates the pm3 client dependency and produces a single self-contained .NET executable.

- Only the executor layer changes; `Pm3.cs`, parsers, session, and CLI remain untouched
- Requires implementing: packet framing, CRC, T55xx write commands, raw sample download + demodulation for reads

The architecture is designed so Stage B is a drop-in replacement behind the same `IPm3CommandExecutor` interface:

```
Pm3.cs (public API -- stable across both stages)
  --> Pm3Session (orchestration -- stable)
        --> IPm3CommandExecutor (swappable seam)
              |
              +--> Pm3ProcessExecutor     [Stage A: wraps `proxmark3 -c "..."`]
              |
              +--> Pm3NativeExecutor      [Stage B: raw USB binary protocol]
  --> Parsers
        Stage A: parse pm3 client text output
        Stage B: parse binary response payloads (new parsers, same result types)
```

---

## Stage A: Process Wrapper Prototype

### File structure target (Pm3UsbApi project)

```
Pm3UsbApi/
  Pm3.cs                              # High-level public API (exists, stub)
  Pm3Options.cs                        # Configuration
  Pm3Exception.cs                      # Exception hierarchy
  CommandResult.cs                     # Command result model
  Execution/
    IPm3CommandExecutor.cs             # Abstraction for command execution
    Pm3ProcessExecutor.cs              # Stage A: -c flag per-process implementation
  Session/
    Pm3Session.cs                      # Orchestrates commands, error detection, session state
  Parsers/
    OutputParser.cs                    # Shared parsing utilities (ANSI strip, error detect)
    DetectParser.cs                    # lf t55 detect
    TuneParser.cs                      # lf tune
    BlockReadParser.cs                 # lf t55 read
    DumpParser.cs                      # lf t55 dump
```

---

## Phase 1: Foundation -- Models, Configuration & Executor Interface

**Goal:** Establish the configuration, result models, exception types, and executor abstraction. Everything compiles. No functional behavior yet.

**Prerequisite:** None.

### TODOs

- [ ] **1.1** Create `Pm3UsbApi/Pm3Options.cs`:
  ```csharp
  public record Pm3Options
  {
      // Absolute path to proxmark3 executable (or pm3.bat).
      // null = auto-detect from PATH / common locations.
      public string? Pm3ClientPath { get; init; }

      // COM port or device path (e.g., "COM3", "/dev/ttyACM0").
      // null = let pm3 client auto-detect.
      public string? DevicePort { get; init; }

      // Timeout for a single command execution (including process startup for per-invocation).
      public TimeSpan DefaultCommandTimeout { get; init; } = TimeSpan.FromSeconds(15);

      // Timeout for connection verification.
      public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(20);

      // Prompt regex patterns for interactive mode (future).
      public string[] PromptPatterns { get; init; } = [
          @"^\[usb\].*pm3\s*-->\s*$",
          @"^proxmark3>\s*$",
          @"^pm3\s*-->\s*$"
      ];

      // Enable transcript logging of all commands and responses.
      public bool EnableTranscriptLogging { get; init; }

      // Path for transcript log file. null = auto-generate in temp.
      public string? TranscriptPath { get; init; }
  }
  ```

- [ ] **1.2** Create `Pm3UsbApi/CommandResult.cs`:
  ```csharp
  public class CommandResult
  {
      public required string[] Commands { get; init; }
      public required IReadOnlyList<string> OutputLines { get; init; }
      public string RawOutput => string.Join(Environment.NewLine, OutputLines);
      public int ExitCode { get; init; }
      public bool HasErrors { get; init; }
      public string? ErrorSummary { get; init; }
  }
  ```

- [ ] **1.3** Create `Pm3UsbApi/Pm3Exception.cs` with hierarchy:
  - `Pm3Exception` (base; carries optional `CommandResult`)
  - `Pm3ConnectionException` (cannot reach device or pm3 client)
  - `Pm3CommandException` (command returned error output)
  - `Pm3TimeoutException` (command timed out)
  - `Pm3ClientNotFoundException` (pm3 executable not found)

- [ ] **1.4** Create `Pm3UsbApi/Execution/IPm3CommandExecutor.cs`:
  ```csharp
  public interface IPm3CommandExecutor : IAsyncDisposable
  {
      // Execute one or more chained commands. For per-invocation, these are joined with "; ".
      Task<CommandResult> ExecuteAsync(
          string[] commands,
          TimeSpan? timeout = null,
          CancellationToken ct = default);

      // Send a break/cancel signal to abort a running operation.
      Task CancelCurrentAsync(CancellationToken ct = default);
  }
  ```

- [ ] **1.5** Verify the project builds: `dotnet build Pm3UsbApi/Pm3UsbApi.csproj`

---

## Phase 2: Process Executor

**Goal:** Implement `Pm3ProcessExecutor` that launches `proxmark3 -c "..."` for each operation, captures stdout, strips ANSI codes, and detects errors.

**Prerequisite:** Phase 1 complete.

### TODOs

- [ ] **2.1** Create `Pm3UsbApi/Parsers/OutputParser.cs` with shared utilities:
  - `static string StripAnsi(string line)` -- regex `\x1B\[[0-9;]*[A-Za-z]` to remove ANSI escape sequences.
  - `static (bool hasErrors, string? errorSummary) DetectErrors(IReadOnlyList<string> lines)` -- scan for `[!]`, `[-]`, lines starting with `error` or `failed` (case-insensitive).

- [ ] **2.2** Create `Pm3UsbApi/Execution/Pm3ProcessExecutor.cs`:
  - Constructor takes `Pm3Options`.
  - `ExecuteAsync`:
    1. Resolve pm3 client path (from options or auto-detect).
    2. Build argument string: `{devicePort} -c "{cmd1}; {cmd2}; ..."` (omit port arg if null).
    3. Create `Process` with `RedirectStandardOutput = true`, `RedirectStandardError = true`, `UseShellExecute = false`, `CreateNoWindow = true`.
    4. Start process, read all stdout + stderr.
    5. Apply ANSI stripping to each line.
    6. Wait for process exit with timeout.
    7. On timeout: kill process, throw `Pm3TimeoutException`.
    8. Run error detection on output lines.
    9. Return `CommandResult`.
  - `CancelCurrentAsync`: kill the running process (if any).

- [ ] **2.3** Implement pm3 client auto-detection in a helper method `ResolvePm3ClientPath()`:
  - If `Pm3Options.Pm3ClientPath` is set and file exists, use it.
  - Search PATH for `proxmark3.exe` (Windows) or `proxmark3` (Linux/macOS).
  - Search common locations: `C:\ProxSpace\pm3\`, user's home directory, etc.
  - Throw `Pm3ClientNotFoundException` if not found.
  - Cache the resolved path after first successful resolution.

- [ ] **2.4** Handle Windows-specific considerations:
  - ProxSpace builds may need certain DLLs on PATH. Document this as a requirement.
  - If using `pm3.bat`, the process may spawn a child shell -- handle accordingly or require direct `proxmark3.exe` path.
  - Ensure `CreateNoWindow = true` to avoid console window popups.

- [ ] **2.5** Write a simple integration smoke test (manual, not automated):
  - Instantiate `Pm3ProcessExecutor` with appropriate options.
  - Execute `["hw version"]` and print the output.
  - Verify output contains Proxmark3 version information.
  - This confirms the executor can find the client and communicate with the device.

---

## Phase 3: Session Layer

**Goal:** Implement `Pm3Session` that provides higher-level command orchestration: T55 session state management (chaining detect), connection verification, and optional transcript logging.

**Prerequisite:** Phase 2 complete.

### TODOs

- [ ] **3.1** Create `Pm3UsbApi/Session/Pm3Session.cs`:
  - Constructor takes `IPm3CommandExecutor` and `Pm3Options`.
  - Implement `IAsyncDisposable`.
  - Internal state:
    - `bool _connected` flag
    - `DateTime _lastDetectTime` for session cache
    - `TimeSpan _detectCacheTtl = TimeSpan.FromSeconds(5)`

- [ ] **3.2** Implement `ConnectAsync(CancellationToken ct)`:
  - Execute `["hw version"]` via the executor.
  - If successful (no errors, exit code 0), set `_connected = true`.
  - If failed, throw `Pm3ConnectionException` with details.
  - Log the version info for diagnostics.

- [ ] **3.3** Implement `DisconnectAsync()`:
  - Set `_connected = false`.
  - Dispose the executor.

- [ ] **3.4** Implement `IsConnectedAsync(CancellationToken ct)`:
  - If not `_connected`, return false.
  - Optionally run a quick `hw version` ping to verify device still responds.

- [ ] **3.5** Implement `ExecuteT55CommandAsync(string command, TimeSpan? timeout, CancellationToken ct)`:
  - Chains `lf t55 detect` before the provided command.
  - For per-invocation mode, always chain detect since each invocation is a new process.
  - Calls executor with `["lf t55 detect", command]`.
  - Returns `CommandResult` (output from both commands).

- [ ] **3.6** Implement `ExecuteCommandAsync(string command, TimeSpan? timeout, CancellationToken ct)`:
  - For non-T55 commands (e.g., `hw version`, `lf tune`).
  - Calls executor with `[command]`.
  - Returns `CommandResult`.

- [ ] **3.7** (Optional) Implement transcript logging:
  - If `Pm3Options.EnableTranscriptLogging` is true:
    - Open/create log file at `TranscriptPath` (or auto-generate path).
    - Log timestamped entries: `[timestamp] >>> command` and `[timestamp] <<< output lines`.
  - Use a simple `StreamWriter` with auto-flush.

- [ ] **3.8** Verify session layer works end-to-end:
  - Connect, run `lf t55 detect` (with a tag on the reader), verify output.

---

## Phase 4: Output Parsers + Unit Tests

**Goal:** Implement parsers for each pm3 command and validate with unit tests using real captured output.

**Prerequisite:** Phase 2 complete (parsers only depend on `CommandResult` and string processing).

### TODOs

- [ ] **4.1** Create `Pm3UsbApi/Parsers/DetectResult.cs` and `DetectParser.cs`:
  ```csharp
  public record DetectResult(
      bool ChipFound,
      string? ChipType,      // e.g., "T55x7"
      string? Modulation,
      string? Block0Hex);

  public static class DetectParser
  {
      public static DetectResult Parse(CommandResult result);
  }
  ```
  - Parse lines for `Chip Type`, `Modulation`, `Block0` values.
  - `ChipFound = true` if `Chip Type` line present and does NOT contain `none` or `unknown`.

- [ ] **4.2** Create `Pm3UsbApi/Parsers/TuneResult.cs` and `TuneParser.cs`:
  ```csharp
  public record TuneResult(bool Success, uint PeakMilliVolts);

  public static class TuneParser
  {
      public static TuneResult Parse(CommandResult result);
  }
  ```
  - Regex: `\[=\]\s*(\d+)\s*mV\b`
  - Use the last match if multiple exist.

- [ ] **4.3** Create `Pm3UsbApi/Parsers/BlockReadResult.cs` and `BlockReadParser.cs`:
  ```csharp
  public record BlockReadResult(bool Success, string? HexData);

  public static class BlockReadParser
  {
      public static BlockReadResult Parse(CommandResult result, int block);
  }
  ```
  - Look for lines containing `[+]` and a hex pattern.
  - Regex to extract 8-char hex value from table-formatted line.
  - Normalize to uppercase.

- [ ] **4.4** Create `Pm3UsbApi/Parsers/DumpResult.cs` and `DumpParser.cs`:
  ```csharp
  public record DumpResult(bool Success, IReadOnlyList<T55Block> Blocks, string RawOutput);

  public static class DumpParser
  {
      public static DumpResult Parse(CommandResult result);
  }
  ```
  - Parse table rows with format: `<block_num> | <hex_data> | <binary>` or similar.
  - Build `T55Block` from each hex value.

- [ ] **4.5** Create test project `Pm3UsbApi.Tests`:
  - Create `Pm3UsbApi.Tests/Pm3UsbApi.Tests.csproj`:
    - Target framework: `net9.0`
    - References: `Pm3UsbApi` project
    - Packages: `NUnit`, `NUnit3TestAdapter`, `Microsoft.NET.Test.Sdk`, `JetBrains.Annotations`
  - Add to `ElevatorTokens.sln`.

- [ ] **4.6** Capture real pm3 output samples for test fixtures:
  - Connect Proxmark3 with T5577 tag.
  - Run via ProxSpace and capture exact output (including ANSI codes if present) for:
    - `lf t55 detect` (successful, with chip info)
    - `lf t55 detect` (failed, no tag present -- if possible to capture)
    - `lf t55 read -b 0` through at least `-b 3`
    - `lf t55 dump`
    - `lf tune`
    - `hw version`
  - Store as string constants in a `TestFixtures` static class in the test project.
  - Include both raw (with ANSI) and stripped versions.

- [ ] **4.7** Write parser unit tests:
  - `DetectParserTests`:
    - Successful detect with T55x7 chip
    - Failed detect (no tag)
    - Edge cases (different chip types if available)
  - `TuneParserTests`:
    - Typical tune output with mV value
    - Multiple mV lines (use last)
    - No mV line (failure case)
  - `BlockReadParserTests`:
    - Successful read for each block
    - Failed read
  - `DumpParserTests`:
    - Full 8-block dump
    - Parse block values match expected T55Block values

- [ ] **4.8** Write `OutputParser` (ANSI strip + error detection) unit tests:
  - ANSI stripping correctly removes color codes
  - Error detection finds `[!]`, `[-]`, `failed` lines
  - Non-error lines (e.g., `[+]`, `[=]`) are not flagged

- [ ] **4.9** Verify all tests pass: `dotnet test`

---

## Phase 5: Pm3 High-Level API

**Goal:** Wire everything together in `Pm3.cs`. Replace all `NotImplementedException` stubs with real implementations using the session layer and parsers.

**Prerequisite:** Phases 3 and 4 complete.

### TODOs

- [ ] **5.1** Update `Pm3.cs` constructor and lifecycle:
  - Accept `Pm3Options` parameter (with sensible defaults).
  - Internally create `Pm3ProcessExecutor` and `Pm3Session`.
  - Implement `IAsyncDisposable` to clean up session and executor.

- [ ] **5.2** Implement `ConnectAsync()`:
  - Delegate to `Pm3Session.ConnectAsync()`.
  - Return `true` on success, `false` on failure (or throw -- decide on convention).

- [ ] **5.3** Implement `DisconnectAsync()`:
  - Delegate to `Pm3Session.DisconnectAsync()`.

- [ ] **5.4** Implement `IsConnectedAsync()`:
  - Delegate to `Pm3Session.IsConnectedAsync()`.

- [ ] **5.5** Implement `EnsureT55SessionActive()`:
  - Execute `lf t55 detect` via session.
  - Parse with `DetectParser`.
  - Throw `Pm3CommandException` if chip not found.

- [ ] **5.6** Implement `ReadPage0BlockAsync(uint block)`:
  - Validate block 0-7 (existing check).
  - Execute `lf t55 read -b {block}` via session's `ExecuteT55CommandAsync` (which chains detect).
  - Parse with `BlockReadParser`.
  - Return hex string.

- [ ] **5.7** Implement `WritePage0BlockAsync(uint block, T55Block data)`:
  - Validate block 0-7, block != 7 (existing checks).
  - Execute `lf t55 write -b {block} -d {data.ToHex()}` via session's `ExecuteT55CommandAsync`.
  - Check for success in output.
  - (Optional) Read back and verify.

- [ ] **5.8** Implement `Dump()`:
  - Execute `lf t55 dump` via session's `ExecuteT55CommandAsync`.
  - Parse with `DumpParser` (or return raw output as currently designed).
  - Return raw output string.

- [ ] **5.9** Implement `StartLfTune()`:
  - Execute `lf tune` via session's `ExecuteCommandAsync`.
  - Store the `CommandResult` internally for later parsing by `GetLfTunePeakMilliVolts`.

- [ ] **5.10** Implement `GetLfTunePeakMilliVolts()`:
  - Parse stored tune output with `TuneParser`.
  - Throw if no tune output available (i.e., `StartLfTune` not called).

- [ ] **5.11** Implement `StopLfTune()`:
  - For per-invocation mode: this is likely a no-op since `lf tune` runs and exits.
  - For interactive mode (future): send break signal.

- [ ] **5.12** Add `CancellationToken` parameters to all public async methods.

- [ ] **5.13** Verify all methods work against real hardware (manual smoke test).

---

## Phase 6: Pm3Cli Interactive Tool

**Goal:** Build the interactive CLI in the `Pm3Cli` project for development and testing.

**Prerequisite:** Phase 5 complete.

### TODOs

- [ ] **6.1** Add project reference: `Pm3Cli.csproj` --> `Pm3UsbApi`.

- [ ] **6.2** Implement `Pm3CliProgram.cs` with argument parsing:
  - Optional args: `--pm3-path <path>`, `--port <COM3>`, `--timeout <seconds>`.
  - Create `Pm3Options` from args.
  - Create `Pm3` instance.
  - Enter interactive prompt loop with prompt `pm3api>`.

- [ ] **6.3** Implement CLI commands:
  | Command               | Description                          | Maps to                          |
  |-----------------------|--------------------------------------|----------------------------------|
  | `connect`             | Connect to device                    | `Pm3.ConnectAsync()`             |
  | `disconnect`          | Disconnect                           | `Pm3.DisconnectAsync()`          |
  | `status`              | Show connection status               | `Pm3.IsConnectedAsync()`         |
  | `detect`              | Run T55 detect                       | `Pm3.EnsureT55SessionActive()`   |
  | `tune`                | Run LF tune, show peak mV            | `StartLfTune` + `GetLfTunePeak`  |
  | `read <block>`        | Read page 0 block                    | `Pm3.ReadPage0BlockAsync(block)` |
  | `write <block> <hex>` | Write page 0 block                   | `Pm3.WritePage0BlockAsync(...)`  |
  | `dump`                | Dump all blocks                      | `Pm3.Dump()`                     |
  | `raw <pm3 command>`   | Send raw pm3 command, show output    | Session.ExecuteCommandAsync      |
  | `help`                | Show available commands              |                                  |
  | `exit`                | Quit                                 |                                  |

- [ ] **6.4** Add user-friendly error handling:
  - Catch `Pm3Exception` subtypes and display helpful messages.
  - Show raw pm3 output on error for debugging.

- [ ] **6.5** Test all CLI commands with real hardware.

---

## Phase 7: Integration Testing & Polish

**Goal:** Validate the full stack with real hardware. Add automated integration tests (skippable in CI). Fix any issues found.

**Prerequisites:** Phase 6 complete. Proxmark3 connected with T5577 tag.

### TODOs

- [ ] **7.1** Full manual smoke test via `Pm3Cli`:
  - [ ] `connect` -- verify success message
  - [ ] `status` -- shows connected
  - [ ] `detect` -- shows T55x7 chip info
  - [ ] `read 0` through `read 7` -- shows hex values
  - [ ] `dump` -- shows full block table
  - [ ] `tune` -- shows peak mV value
  - [ ] `write 5 <hex>` -- write to block 5 (safe block), then `read 5` to verify
  - [ ] `raw hw version` -- shows device info
  - [ ] `disconnect` -- clean disconnect
  - [ ] `exit` -- exits cleanly

- [ ] **7.2** Create `Pm3UsbApi.Tests/Integration/Pm3IntegrationTests.cs`:
  - Mark with `[Category("Integration")]` so CI can skip.
  - Test: connect/disconnect lifecycle
  - Test: detect -> read all blocks -> verify non-null
  - Test: write block 5 -> read back -> verify match
  - Test: dump -> verify contains expected number of blocks
  - Test: timeout behavior (execute with very short timeout)
  - Test: error handling (disconnect, then try to read -- should get meaningful error)

- [ ] **7.3** Document output format differences:
  - During testing, if any pm3 output doesn't match expected patterns, update parsers.
  - Add new test fixtures for any format variations discovered.
  - Update regex patterns in notes if needed.

- [ ] **7.4** Review and clean up:
  - Ensure all public APIs have XML doc comments.
  - Remove any TODO comments left in code.
  - Verify no compiler warnings.
  - Run `dotnet test` -- all unit tests pass.

---

## Stage B: Native Binary Protocol (Future)

### Phase 8: Native USB Communication

**Goal:** Replace `Pm3ProcessExecutor` with `Pm3NativeExecutor` that speaks the Proxmark3 binary packet protocol directly over USB CDC serial. This eliminates the pm3 client dependency and produces a single self-contained .NET executable portable to any platform.

**Prerequisites:** Stage A complete and validated. Token configuration (Block 0) known from Stage A testing.

**Background:** The Proxmark3 firmware communicates using `PacketCommandNG` / `PacketResponseNG` binary packets over USB CDC (which appears as a serial port). The protocol is documented in [doc/new_frame_format.md](https://github.com/RfidResearchGroup/proxmark3/blob/master/doc/new_frame_format.md) and defined in [include/pm3_cmd.h](https://github.com/RfidResearchGroup/proxmark3/blob/master/include/pm3_cmd.h).

### Sub-phases

#### 8a: Protocol Layer

- [ ] **8a.1** Add `System.IO.Ports` NuGet package to `Pm3UsbApi.csproj`.

- [ ] **8a.2** Implement packet structures in C#:
  - `PacketCommandNG`: preamble magic `0x61334d50` ("PM3a"), 15-bit length, NG flag, 16-bit cmd, variable data, postamble CRC or magic `0x3361`.
  - `PacketResponseNG`: preamble magic `0x62334d50` ("PM3b"), 15-bit length, NG flag, status, reason, 16-bit cmd, variable data, postamble CRC or magic `0x3362`.
  - CRC-14a computation (or use magic postamble placeholder for USB where CRC is optional).

- [ ] **8a.3** Implement serial port transport:
  - Open USB CDC serial port (`SerialPort` class, cross-platform).
  - Device auto-detection: enumerate serial ports, try each one with `CMD_PING`.
  - Send packet: serialize `PacketCommandNG` to bytes, write to serial port.
  - Receive packet: read from serial, sync on magic bytes, parse `PacketResponseNG`.
  - Handle fragmented receives (USB splits large responses into 128-byte chunks).

- [ ] **8a.4** Implement basic commands to validate the protocol layer:
  - `CMD_PING` (0x0109) -- send ping, verify pong response.
  - `CMD_VERSION` (0x0107) -- get firmware version string.
  - `CMD_CAPABILITIES` (0x0112) -- get device capabilities.

- [ ] **8a.5** Unit tests for packet serialization/deserialization using known reference frames from the protocol docs.

#### 8b: T55xx Write (straightforward)

- [ ] **8b.1** Implement `CMD_LF_T55XX_WRITEBL` (0x0215):
  - Payload: `t55xx_write_block_t` = 4 bytes data + 4 bytes password + 1 byte block number + 1 byte flags.
  - Response: `PM3_SUCCESS` with no data payload.
  - Map to `Pm3.WritePage0BlockAsync()`.

- [ ] **8b.2** Implement `CMD_MEASURE_ANTENNA_TUNING_LF` (0x0402):
  - Parse tuning response data.
  - Map to `Pm3.StartLfTune()` / `Pm3.GetLfTunePeakMilliVolts()`.

#### 8c: T55xx Read (requires signal processing)

- [ ] **8c.1** Implement `CMD_LF_T55XX_READBL` (0x0214):
  - Send read command (triggers firmware to capture raw ADC samples into BigBuf).
  - Response: `PM3_SUCCESS` with no data (samples are in device BigBuf).

- [ ] **8c.2** Implement `CMD_DOWNLOAD_BIGBUF` (0x0207) / `CMD_DOWNLOADED_BIGBUF` (0x0208):
  - Download raw ADC samples from device memory.
  - Handle chunked transfer (device sends 128-byte USB packets).

- [ ] **8c.3** Implement demodulator for the specific token configuration:
  - Determine exact modulation from Block 0 config (captured during Stage A).
  - Implement the required demodulator (likely ASK/Manchester -- verify during Stage A testing).
  - Extract 32-bit block value from demodulated bitstream.

- [ ] **8c.4** Implement `CMD_LF_T55XX_RESET_READ` (0x0216) as alternative read approach:
  - Sends reset to T55xx, chip transmits all page 0 data.
  - Potentially simpler than per-block reads.

#### 8d: Integration

- [ ] **8d.1** Create `Pm3NativeExecutor` implementing `IPm3CommandExecutor`:
  - Map command strings to binary commands (or create a new native-specific interface).
  - Alternative: create `IPm3DeviceApi` with typed methods that `Pm3Session` calls directly, bypassing string commands entirely.

- [ ] **8d.2** Update `Pm3.cs` to support selecting executor type via `Pm3Options`:
  - `Pm3Options.ExecutorMode = ProcessWrapper | Native` (default to ProcessWrapper for backward compat).

- [ ] **8d.3** Run the full Stage A test suite against the native executor. All tests must pass.

- [ ] **8d.4** Cross-platform validation:
  - Test on Windows (USB CDC as COM port).
  - Test on macOS (USB CDC as `/dev/cu.usbmodem*`).
  - Test on Linux (USB CDC as `/dev/ttyACM0`).

### Key command codes reference (from pm3_cmd.h)

| Command                       | Code     | Notes                                    |
|-------------------------------|----------|------------------------------------------|
| `CMD_PING`                    | `0x0109` | Connection test                          |
| `CMD_VERSION`                 | `0x0107` | Firmware version                         |
| `CMD_CAPABILITIES`            | `0x0112` | Device capabilities                      |
| `CMD_LF_T55XX_READBL`        | `0x0214` | Read T55xx block (captures raw samples)  |
| `CMD_LF_T55XX_WRITEBL`       | `0x0215` | Write T55xx block                        |
| `CMD_LF_T55XX_RESET_READ`    | `0x0216` | Reset-then-read                          |
| `CMD_LF_T55XX_WAKEUP`        | `0x0224` | Wake up with password                    |
| `CMD_LF_T55XX_SET_CONFIG`    | `0x0226` | Set T55xx timing config                  |
| `CMD_MEASURE_ANTENNA_TUNING_LF` | `0x0402` | LF antenna tuning                     |
| `CMD_DOWNLOAD_BIGBUF`        | `0x0207` | Download samples from device             |
| `CMD_DOWNLOADED_BIGBUF`      | `0x0208` | Chunk of downloaded data                 |

---

## Notes for Agents

1. **Execute phases sequentially.** Each phase depends on the previous ones. Within a phase, TODOs can sometimes be parallelized.

2. **Mark TODOs as done** by changing `- [ ]` to `- [x]` in this file after completing and committing each item.

3. **Capture real pm3 output** when testing with hardware. These samples are essential for parser unit tests. Save them as string constants in the test project.

4. **ANSI stripping is critical.** The pm3 client uses ANSI color codes in stdout. Always strip these before parsing. Test with both raw and stripped output.

5. **Prompt patterns vary by build.** Common patterns:
   - `[usb] pm3 -->` (RRG/Iceman recent builds)
   - `proxmark3>` (older builds)
   - `pm3 -->` (some configurations)

6. **Never write to blocks 0 or 7** during testing. Block 0 is the configuration block. Block 7 is the password block. Writing incorrect values can brick the tag.

7. **Consult the user** if you encounter:
   - Unexpected pm3 output formats
   - pm3 client not found or DLL issues
   - Need to modify the public API surface
   - Any uncertainty about whether to proceed

8. **pm3 client installation:**
   - Windows: [ProxSpace](https://github.com/Gator96100/ProxSpace) -- `proxmark3.exe` is in the `pm3/` folder. May need MSYS2 DLLs on PATH.
   - macOS: `brew install proxmark3` or build from [RRG source](https://github.com/RfidResearchGroup/proxmark3) with `make clean && make all`.
   - Linux: `apt install proxmark3` (some distros) or build from source.
   - If auto-detection fails, the user should provide the full path via `Pm3Options.Pm3ClientPath`.

9. **The `-c` flag** joins multiple commands with `; ` and runs them in sequence in a single pm3 client session. This preserves state (e.g., `lf t55 detect` state persists for the subsequent `lf t55 read`).

10. **Error indicators in pm3 output:**
    - `[+]` = success/info (green)
    - `[=]` = data/status (blue)
    - `[!]` = warning (yellow) -- treat as error unless whitelisted
    - `[-]` = error (red) -- always treat as error
    - `[#]` = debug info -- usually ignore

11. **Stage B note:** When implementing the native binary protocol (Phase 8), reference the Proxmark3 RRG source code freely. The firmware source at `armsrc/lfops.c` contains the T55xx command handlers. The protocol is defined in `include/pm3_cmd.h` and documented in `doc/new_frame_format.md`. The key insight is that T55xx reads return raw ADC samples (not decoded data), requiring client-side demodulation.
