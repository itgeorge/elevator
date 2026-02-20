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

- [x] **1.1** Create `Pm3UsbApi/Pm3Options.cs`:
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

- [x] **1.2** Create `Pm3UsbApi/CommandResult.cs`:
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

- [x] **1.3** Create `Pm3UsbApi/Pm3Exception.cs` with hierarchy:
  - `Pm3Exception` (base; carries optional `CommandResult`)
  - `Pm3ConnectionException` (cannot reach device or pm3 client)
  - `Pm3CommandException` (command returned error output)
  - `Pm3TimeoutException` (command timed out)
  - `Pm3ClientNotFoundException` (pm3 executable not found)

- [x] **1.4** Create `Pm3UsbApi/Execution/IPm3CommandExecutor.cs`:
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

- [x] **1.5** Verify the project builds: `dotnet build Pm3UsbApi/Pm3UsbApi.csproj`

---

## Phase 2: Process Executor

**Goal:** Implement `Pm3ProcessExecutor` that launches `proxmark3 -c "..."` for each operation, captures stdout, strips ANSI codes, and detects errors.

**Prerequisite:** Phase 1 complete.

### TODOs

- [x] **2.1** Create `Pm3UsbApi/Parsers/OutputParser.cs` with shared utilities:
  - `static string StripAnsi(string line)` -- regex `\x1B\[[0-9;]*[A-Za-z]` to remove ANSI escape sequences.
  - `static (bool hasErrors, string? errorSummary) DetectErrors(IReadOnlyList<string> lines)` -- scan for `[!]`, `[-]`, lines starting with `error` or `failed` (case-insensitive).

- [x] **2.2** Create `Pm3UsbApi/Execution/Pm3ProcessExecutor.cs`:
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

- [x] **2.3** Implement pm3 client auto-detection in a helper method `ResolvePm3ClientPath()`:
  - If `Pm3Options.Pm3ClientPath` is set and file exists, use it.
  - Search PATH for `proxmark3.exe` (Windows) or `proxmark3` (Linux/macOS).
  - Search common locations: `C:\ProxSpace\pm3\`, user's home directory, etc.
  - Throw `Pm3ClientNotFoundException` if not found.
  - Cache the resolved path after first successful resolution.

- [x] **2.4** Handle Windows-specific considerations:
  - ProxSpace builds may need certain DLLs on PATH. Document this as a requirement.
  - If using `pm3.bat`, the process may spawn a child shell -- handle accordingly or require direct `proxmark3.exe` path.
  - Ensure `CreateNoWindow = true` to avoid console window popups.

- [x] **2.5** Write a simple integration smoke test (manual, not automated):
  - Instantiate `Pm3ProcessExecutor` with appropriate options.
  - Execute `["hw version"]` and print the output.
  - Verify output contains Proxmark3 version information.
  - This confirms the executor can find the client and communicate with the device.

---

## Phase 3: Session Layer

**Goal:** Implement `Pm3Session` that provides higher-level command orchestration: T55 session state management (chaining detect), connection verification, and optional transcript logging.

**Prerequisite:** Phase 2 complete.

### TODOs

- [x] **3.1** Create `Pm3UsbApi/Session/Pm3Session.cs`:
  - Constructor takes `IPm3CommandExecutor` and `Pm3Options`.
  - Implement `IAsyncDisposable`.
  - Internal state:
    - `bool _connected` flag
    - `DateTime _lastDetectTime` for session cache
    - `TimeSpan _detectCacheTtl = TimeSpan.FromSeconds(5)`

- [x] **3.2** Implement `ConnectAsync(CancellationToken ct)`:
  - Execute `["hw version"]` via the executor.
  - If successful (no errors, exit code 0), set `_connected = true`.
  - If failed, throw `Pm3ConnectionException` with details.
  - Log the version info for diagnostics.

- [x] **3.3** Implement `DisconnectAsync()`:
  - Set `_connected = false`.
  - Dispose the executor.

- [x] **3.4** Implement `IsConnectedAsync(CancellationToken ct)`:
  - If not `_connected`, return false.
  - Optionally run a quick `hw version` ping to verify device still responds.

- [x] **3.5** Implement `ExecuteT55CommandAsync(string command, TimeSpan? timeout, CancellationToken ct)`:
  - Chains `lf t55 detect` before the provided command.
  - For per-invocation mode, always chain detect since each invocation is a new process.
  - Calls executor with `["lf t55 detect", command]`.
  - Returns `CommandResult` (output from both commands).

- [x] **3.6** Implement `ExecuteCommandAsync(string command, TimeSpan? timeout, CancellationToken ct)`:
  - For non-T55 commands (e.g., `hw version`, `lf tune`).
  - Calls executor with `[command]`.
  - Returns `CommandResult`.

- [x] **3.7** (Optional) Implement transcript logging:
  - If `Pm3Options.EnableTranscriptLogging` is true:
    - Open/create log file at `TranscriptPath` (or auto-generate path).
    - Log timestamped entries: `[timestamp] >>> command` and `[timestamp] <<< output lines`.
  - Use a simple `StreamWriter` with auto-flush.

- [x] **3.8** Verify session layer works end-to-end:
  - Connect, run `lf t55 detect` (with a tag on the reader), verify output.

---

## Phase 4: Output Parsers + Unit Tests

**Goal:** Implement parsers for each pm3 command and validate with unit tests using real captured output.

**Prerequisite:** Phase 2 complete (parsers only depend on `CommandResult` and string processing).

### TODOs

- [x] **4.1** Create `Pm3UsbApi/Parsers/DetectResult.cs` and `DetectParser.cs`:
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

- [x] **4.2** Create `Pm3UsbApi/Parsers/TuneResult.cs` and `TuneParser.cs`:
  ```csharp
  public record TuneResult(bool Success, uint PeakMilliVolts);

  public static class TuneParser
  {
      public static TuneResult Parse(CommandResult result);
  }
  ```
  - Regex: `\[=\]\s*(\d+)\s*mV\b`
  - Use the last match if multiple exist.

- [x] **4.3** Create `Pm3UsbApi/Parsers/BlockReadResult.cs` and `BlockReadParser.cs`:
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

- [x] **4.4** Create `Pm3UsbApi/Parsers/DumpResult.cs` and `DumpParser.cs`:
  ```csharp
  public record DumpResult(bool Success, IReadOnlyList<T55Block> Blocks, string RawOutput);

  public static class DumpParser
  {
      public static DumpResult Parse(CommandResult result);
  }
  ```
  - Parse table rows with format: `<block_num> | <hex_data> | <binary>` or similar.
  - Build `T55Block` from each hex value.

- [x] **4.5** Create test project `Pm3UsbApi.Tests`:
  - Create `Pm3UsbApi.Tests/Pm3UsbApi.Tests.csproj`:
    - Target framework: `net9.0`
    - References: `Pm3UsbApi` project
    - Packages: `NUnit`, `NUnit3TestAdapter`, `Microsoft.NET.Test.Sdk`, `JetBrains.Annotations`
  - Add to `ElevatorTokens.sln`.

- [x] **4.6** Capture real pm3 output samples for test fixtures:
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

- [x] **4.7** Write parser unit tests:
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

- [x] **4.8** Write `OutputParser` (ANSI strip + error detection) unit tests:
  - ANSI stripping correctly removes color codes
  - Error detection finds `[!]`, `[-]`, `failed` lines
  - Non-error lines (e.g., `[+]`, `[=]`) are not flagged

- [x] **4.9** Verify all tests pass: `dotnet test`

---

## Phase 5: Pm3 High-Level API

**Goal:** Wire everything together in `Pm3.cs`. Replace all `NotImplementedException` stubs with real implementations using the session layer and parsers.

**Prerequisite:** Phases 3 and 4 complete.

### TODOs

- [x] **5.1** Update `Pm3.cs` constructor and lifecycle:
  - Accept `Pm3Options` parameter (with sensible defaults).
  - Internally create `Pm3ProcessExecutor` and `Pm3Session`.
  - Implement `IAsyncDisposable` to clean up session and executor.

- [x] **5.2** Implement `ConnectAsync()`:
  - Delegate to `Pm3Session.ConnectAsync()`.
  - Return `true` on success, `false` on failure (or throw -- decide on convention).

- [x] **5.3** Implement `DisconnectAsync()`:
  - Delegate to `Pm3Session.DisconnectAsync()`.

- [x] **5.4** Implement `IsConnectedAsync()`:
  - Delegate to `Pm3Session.IsConnectedAsync()`.

- [x] **5.5** Implement `EnsureT55SessionActive()`:
  - Execute `lf t55 detect` via session.
  - Parse with `DetectParser`.
  - Throw `Pm3CommandException` if chip not found.

- [x] **5.6** Implement `ReadPage0BlockAsync(uint block)`:
  - Validate block 0-7 (existing check).
  - Execute `lf t55 read -b {block}` via session's `ExecuteT55CommandAsync` (which chains detect).
  - Parse with `BlockReadParser`.
  - Return hex string.

- [x] **5.7** Implement `WritePage0BlockAsync(uint block, T55Block data)`:
  - Validate block 0-7, block != 7 (existing checks).
  - Execute `lf t55 write -b {block} -d {data.ToHex()}` via session's `ExecuteT55CommandAsync`.
  - Check for success in output.
  - (Optional) Read back and verify.

- [x] **5.8** Implement `Dump()`:
  - Execute `lf t55 dump` via session's `ExecuteT55CommandAsync`.
  - Parse with `DumpParser` (or return raw output as currently designed).
  - Return raw output string.

- [x] **5.9** Implement `StartLfTune()`:
  - Execute `lf tune` via session's `ExecuteCommandAsync`.
  - Store the `CommandResult` internally for later parsing by `GetLfTuneLastMilliVolts`.

- [x] **5.10** Implement `GetLfTuneLastMilliVolts()`:
  - Parse stored tune output with `TuneParser`.
  - Throw if no tune output available (i.e., `StartLfTune` not called).

- [x] **5.11** Implement `StopLfTune()`:
  - For per-invocation mode: this is likely a no-op since `lf tune` runs and exits.
  - For interactive mode (future): send break signal.

- [x] **5.12** Add `CancellationToken` parameters to all public async methods.

- [x] **5.13** Verify all methods work against real hardware (manual smoke test).

---

## Phase 6: Pm3Cli Interactive Tool

**Goal:** Build the interactive CLI in the `Pm3Cli` project for development and testing.

**Prerequisite:** Phase 5 complete.

### TODOs

- [x] **6.1** Add project reference: `Pm3Cli.csproj` --> `Pm3UsbApi`.

- [x] **6.2** Implement `Pm3CliProgram.cs` with argument parsing:
  - Optional args: `--pm3-path <path>`, `--port <COM3>`, `--timeout <seconds>`.
  - Create `Pm3Options` from args.
  - Create `Pm3` instance.
  - Enter interactive prompt loop with prompt `pm3api>`.

- [x] **6.3** Implement CLI commands:
  | Command               | Description                          | Maps to                          |
  |-----------------------|--------------------------------------|----------------------------------|
  | `connect`             | Connect to device                    | `Pm3.ConnectAsync()`             |
  | `disconnect`          | Disconnect                           | `Pm3.DisconnectAsync()`          |
  | `status`              | Show connection status               | `Pm3.IsConnectedAsync()`         |
  | `detect`              | Run T55 detect                       | `Pm3.EnsureT55SessionActive()`   |
  | `tune`                | Run LF tune, show peak mV            | `StartLfTune` + `GetLfTuneLastMilliVolts`  |
  | `read <block>`        | Read page 0 block                    | `Pm3.ReadPage0BlockAsync(block)` |
  | `write <block> <hex>` | Write page 0 block                   | `Pm3.WritePage0BlockAsync(...)`  |
  | `dump`                | Dump all blocks                      | `Pm3.Dump()`                     |
  | `raw <pm3 command>`   | Send raw pm3 command, show output    | Session.ExecuteCommandAsync      |
  | `help`                | Show available commands              |                                  |
  | `exit`                | Quit                                 |                                  |

- [x] **6.4** Add user-friendly error handling:
  - Catch `Pm3Exception` subtypes and display helpful messages.
  - Show raw pm3 output on error for debugging.

- [ ] **6.5** Test all CLI commands with real hardware. (manual)

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
See `pm3-implementation-plan-stage-b.md` for the continuation of this plan.