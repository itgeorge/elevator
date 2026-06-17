# Proxmark3 USB API -- Implementation Notes

This document describes the approach for controlling a Proxmark3 by sending the same CLI commands you use in the `proxmark3` client (e.g., `lf tune`, `lf t55 detect`, `lf t55 dump`, `lf t55 write`, etc.).

Goal: Implement `Pm3UsbApi.Pm3` in C# with a **two-stage strategy**:
- **Stage A (prototype):** Wrap the `proxmark3` client process, parse its text output.
- **Stage B (future):** Replace with native USB binary protocol for a self-contained executable.

> Scope: This covers **host-side comms + parsing**. The Proxmark3 firmware remains unchanged on the device.

---

## Architecture: Binary Protocol vs Text CLI

The Proxmark3 firmware speaks a **binary packet protocol** (`PacketCommandNG` / `PacketResponseNG`) over USB CDC -- not plain text. The text CLI (prompts like `proxmark3>` or `pm3 -->`, human-readable output) is rendered entirely by the **pm3 client application**, not by the device hardware.

**Stage A -- Process Wrapper (prototype):**

```
C# Pm3 API  -->  Pm3Session  -->  Pm3ProcessExecutor
                                        |
                                        v
                              proxmark3 -c "cmd1; cmd2"
                                        |
                                        v
                              (USB binary protocol to device)
```

The pm3 client handles all USB communication and signal processing. We parse its text output. This gets us to a working prototype quickly.

**Stage B -- Native Binary Protocol (future):**

```
C# Pm3 API  -->  Pm3Session  -->  Pm3NativeExecutor
                                        |
                                        v
                              USB CDC serial port (System.IO.Ports)
                                        |
                                        v
                              PacketCommandNG / PacketResponseNG
                                        |
                                        v
                              Proxmark3 device firmware
```

The `IPm3CommandExecutor` interface is the seam. Everything above it (`Pm3.cs`, `Pm3Session`, parsers, CLI) stays the same across both stages.

**pm3 client installation (Stage A):**
- Windows: [ProxSpace](https://github.com/Gator96100/ProxSpace)
- macOS: `brew install proxmark3` or build from [RRG source](https://github.com/RfidResearchGroup/proxmark3)
- Linux: `apt install proxmark3` or build from source

---

## 1) Sanity check on current class design

Current approach structure is valid:

- **Connect/Disconnect**: locate and manage the `proxmark3` client process (Stage A) or serial port (Stage B).
- **Command methods** (`StartLfTune`, `Dump`, `ReadPage0BlockAsync`, `WritePage0BlockAsync`): execute commands and parse the output.
- **Session state**: `EnsureT55SessionActive()` calling `lf t55 detect` is correct because subsequent `lf t55 ...` commands rely on detect state. With the per-invocation approach, chain detect + command in a single `-c` call (e.g., `-c "lf t55 detect; lf t55 read -b 0"`).
- **Async**: good idea; command IO is naturally async and benefits from timeouts/cancellation.

What to refine:

- For per-invocation strategy: chain dependent commands in a single `-c` invocation to preserve session state.
- For interactive strategy (future): add **single-flight / command queue** so only one command is in-flight at a time.
- Ensure you have a robust **prompt detection** strategy for the interactive approach.
- Track "tune session" state carefully (some tune outputs are multi-line and may include periodic updates).

---

## 2) High-level architecture

### 2.1 Components

- **Transport / Command Executor**
  - Stage A per-invocation: launches `proxmark3 -c "..."` and captures stdout.
  - Stage A interactive (optional): maintains a long-running pm3 process with stdin/stdout piping.
  - Stage B native: opens serial port, sends/receives binary packets.
  - All strip ANSI escape codes from output (Stage A) or parse binary responses (Stage B).

- **Protocol / CLI Session**
  - `SendCommandAsync(cmd, completionPredicate)` that returns the captured output.
  - For interactive mode: uses a **prompt recognizer** to know when the command completed.

- **Parsers**
  - Per command: parse tune peak, parse `read` output block hex, parse `dump`, parse errors.
  - Stage A: text parsing of pm3 client output.
  - Stage B: binary response parsing (same result types).

### 2.2 The "prompt" is your delimiter (interactive mode only)

Most reliable approach:
- Read lines until you see a line that matches a prompt pattern such as:
  - `[usb] pm3 -->` (RRG/Iceman recent builds)
  - `proxmark3>` (older builds)
  - `pm3 -->` (some skins)

Implement a configurable list of prompt regex patterns, e.g.:

- `^\[usb\].*pm3\s*-->\s*$`
- `^proxmark3>\s*$`
- `^pm3\s*-->\s*$`

Store it as a list and treat "prompt seen" as "command completed".

> Note: Some outputs may contain the word proxmark3, so match whole-line prompt formats, not substrings.

---

## 3) Transport details (C#)

### 3.1 Pm3 client discovery (Stage A)

Options:
- Allow user to optionally pass in a path to the pm3 client executable.
- Implement auto-discovery and execute if path not specified:
  - Check PATH for `proxmark3.exe` or `pm3.bat`
  - Search common ProxSpace locations (e.g., `C:\ProxSpace\pm3\proxmark3.exe`)
  - On Linux/macOS: look for `proxmark3` or `pm3` in PATH

### 3.2 Per-invocation execution (Stage A, recommended initial approach)

Launch the pm3 client for each operation:
```
proxmark3 -c "lf t55 detect; lf t55 read -b 0"
```

- The pm3 client auto-detects the device port (or pass it as first positional arg).
- Chain dependent commands with `;` to preserve session state within one invocation.
- Capture stdout as the command output.
- Process exit code can indicate success/failure.

### 3.3 Interactive execution (Stage A, optional optimization)

Launch the pm3 client once in interactive mode:
- Redirect stdin/stdout/stderr.
- Send commands via stdin.
- Read stdout with prompt detection.
- Requires ANSI escape code stripping.
- Requires process lifecycle management.

### 3.4 Native USB serial (Stage B)

Open USB CDC serial port using `System.IO.Ports.SerialPort`:
- Windows: `COM3`, `COM4`, etc.
- macOS: `/dev/cu.usbmodem*`
- Linux: `/dev/ttyACM0`

Send/receive `PacketCommandNG`/`PacketResponseNG` binary packets. Protocol is documented in the [RRG source](https://github.com/RfidResearchGroup/proxmark3/blob/master/doc/new_frame_format.md).

### 3.5 ANSI escape code stripping (Stage A)

The pm3 client uses ANSI color codes extensively. Strip them from output before parsing:
- Regex: `\x1B\[[0-9;]*[A-Za-z]`

### 3.6 Line endings & encoding

- Send commands terminated with `\n` (LF). Many devices also accept `\r\n`.
- Use ASCII/UTF-8. Output is mostly ASCII.

---

## 4) Concurrency & correctness requirements

### 4.1 Single command at a time (critical for interactive mode)

The CLI is not designed for interleaved commands. For interactive mode, implement:
- A `SemaphoreSlim` to serialize `SendCommandAsync`.
- A single shared read buffer.

For per-invocation mode, this is inherently serialized (one process per command).

### 4.2 Timeouts & cancellation

Each command should have:
- A default timeout (e.g., 10-15s for simple commands, longer for `dump` or `restore`). Note: per-invocation timeouts should be longer to account for process startup and USB device handshake.
- `CancellationToken` support.
- For per-invocation: kill the process on timeout.

### 4.3 Reconnection logic

- `IsConnectedAsync()` should confirm the pm3 client can reach the device.
  - Per-invocation: run `proxmark3 -c "hw version"` and check for success.
  - Interactive: check process is alive and send a harmless command.

---

## 5) Error handling rules (text protocol)

Proxmark output can include warnings/errors in many forms.
Implement a conservative error detector:
- If output contains lines beginning with:
  - `[!]`, `[-]`, or `error`, `failed`, `timeout`
- Then return failure / throw a typed exception.

Keep a whitelist for known non-fatal warnings if needed.

---

## 6) Mapping your methods to CLI commands

### 6.1 `StartLfTune()`

- Command: `lf tune`
- Completion: read until prompt returns (interactive) or process exits (per-invocation).
- Store the full output in a `LastTuneOutput` field (and timestamp).

### 6.2 `GetLfTuneLastMilliVolts()`

Parse the last tune output.

You mentioned a line like:

- `[=] 60276 mV / 60 V / 60 Vmax`

Robust parsing:
- Regex: `\[=\]\s*(\d+)\s*mV\b`
- Return the integer group as `uint`.

If multiple matches exist, choose the **last** match.

### 6.3 `StopLfTune()`

There isn't always a distinct "stop" command; tune usually ends when output finishes.
If your build supports aborting long ops:
- send `\x03` (Ctrl+C) or `stop`/`abort` if supported by the client skin.
Implementation suggestion:
- Provide `CancelCurrentOperationAsync()` that writes Ctrl+C and waits for prompt (interactive mode).
- For per-invocation mode: kill the process.

If `lf tune` completes quickly on your build, `StopLfTune()` can be a no-op.

### 6.4 `EnsureT55SessionActive()`

- Command: `lf t55 detect`
- Completion: prompt returns (interactive) or process exits (per-invocation).
- Success criteria: output contains something like `Chip found` or `Chip Type` and indicates `T55x7` / `T5577` (depends on build).
- For per-invocation mode: chain detect before each T55 command in the same `-c` call.

### 6.5 `Dump()`

- Command: `lf t55 dump`
- Per-invocation: `proxmark3 -c "lf t55 detect; lf t55 dump"`
- Completion: process exits or prompt returns.
- Return raw output (string).
- Optionally parse into a structured model later.

### 6.6 `ReadBlockAsync(int page, int block)`

- If `page == 0`: `lf t55 read -b {block}`
- If `page == 1`: `lf t55 read --pg1 -b {block}`
- Per-invocation: `proxmark3 -c "lf t55 detect; lf t55 read -b {block}"`
- Parse hex data from output.

Typical output includes a row like:
- `00 | 00148040 | ...`

Regex options:
- `^\[\+\]\s*\s*{block:00}\s*\|\s*([0-9A-Fa-f]{8})\b`
- or a simpler `\b{block:00}\s*\|\s*([0-9A-Fa-f]{8})\b` if formatting varies

Return the 8-hex string normalized uppercase.

### 6.7 `WritePage0BlockAsync(int block, string data)`

- Validate `block` in 0..7.
- Validate `data` is exactly 8 hex characters.
- Command: `lf t55 write -b {block} -d {data}`
- Per-invocation: `proxmark3 -c "lf t55 detect; lf t55 write -b {block} -d {data}"`
- Completion: prompt returns or process exits.
- Success criteria: output contains a success marker (commonly `[+]` without `failed`).
- Optional verify: call `ReadBlockAsync(0, block)` and confirm matches.

> Important: Avoid writing block 0 and 7 unless explicitly intended. Consider guarding those blocks behind an explicit override flag.

---

## 7) Recommended internal helper API

### 7.1 `ExecuteAsync` (per-invocation transport)

Suggested signature:

- `Task<CommandResult> ExecuteAsync(string[] commands, TimeSpan timeout, CancellationToken ct)`

Where `CommandResult` contains:
- `string[] Commands`
- `string RawOutput`
- `string[] Lines`
- `int ExitCode`
- `bool Success`
- `string? ErrorSummary`

### 7.2 `SendCommandAsync` (interactive transport)

Suggested signature:

- `Task<CommandResult> SendCommandAsync(string command, TimeSpan timeout, CancellationToken ct)`

Where `CommandResult` also contains:
- `bool PromptSeen`

### 7.3 Read loop (interactive mode)

Have one background task that reads from the process stdout and pushes full lines to:
- a `Channel<string>` or
- a thread-safe queue

`SendCommandAsync` subscribes to lines until it sees prompt.

---

## 8) Testing strategy

### 8.1 Use a transcript file

During development:
- Log all input commands and output lines to a timestamped file.
- This makes parsing bugs trivial to diagnose.

### 8.2 Unit-test parsers

Feed stored real outputs into parsers and assert:
- Tune peak parsing
- Block read parsing
- Dump extraction

### 8.3 Hardware-in-loop smoke tests

- Connect
- `lf t55 detect`
- `lf t55 read -b 0`
- `lf t55 dump`
- Write to a known-writable block on a sacrificial tag and verify.

---

## 9) Licensing notes

**Stage A (prototype):** The pm3 client is GPL-licensed. Running it as a subprocess is acceptable. Our C# code parses its text output and does not link to or embed any GPL code. This is analogous to a shell script that calls `proxmark3`.

**Stage B (native):** The binary protocol itself is not copyrightable (it's a wire format / fact). However, the demodulation algorithms may be informed by reading the GPL source. If distributing, consider clean-room implementation or ensure compliance with GPL if derived from their code. For internal/personal use this is not a concern.

---

## 10) Implementation checklist (agent-ready)

### Stage A (prototype)
- [ ] Create `Pm3Options` (pm3 client path, COM port, timeouts, ANSI stripping flag).
- [ ] Implement per-invocation command executor (launch `proxmark3 -c "..."`, capture stdout).
- [ ] Implement ANSI escape code stripping.
- [ ] Implement `ConnectAsync` (verify pm3 client is reachable and device responds).
- [ ] Implement `DisconnectAsync` (cleanup).
- [ ] Implement `EnsureT55SessionActive` via chained detect.
- [ ] Implement `StartLfTune` (`lf tune`) capturing output.
- [ ] Implement `GetLfTuneLastMilliVolts` parser using regex.
- [ ] Implement `ReadBlockAsync` with page flag and hex parsing.
- [ ] Implement `WritePage0BlockAsync` and optional read-back verify.
- [ ] Add logging + transcripts.
- [ ] Add unit tests for parsers.

### Stage B (future native)
- [ ] Implement PacketCommandNG / PacketResponseNG binary protocol over serial.
- [ ] Implement CMD_PING, CMD_VERSION for connection testing.
- [ ] Implement CMD_LF_T55XX_WRITEBL for block writes.
- [ ] Implement CMD_LF_T55XX_READBL + BigBuf download + demodulation for block reads.
- [ ] Implement CMD_MEASURE_ANTENNA_TUNING_LF for LF tune.
- [ ] Create `Pm3NativeExecutor` implementing `IPm3CommandExecutor`.
- [ ] Cross-platform testing (Windows, macOS, Linux).

---

## Appendix A -- Regex snippets (Stage A)

- Prompt (example): `^\[usb\].*pm3\s*-->\s*$`
- Tune peak mV: `\[=\]\s*(\d+)\s*mV\b`
- 8-hex: `\b([0-9A-Fa-f]{8})\b`
- ANSI strip: `\x1B\[[0-9;]*[A-Za-z]`

---

## Appendix B -- Example command lines (Stage A)

Per-invocation:
- `proxmark3 -c "lf tune"`
- `proxmark3 -c "lf t55 detect"`
- `proxmark3 -c "lf t55 detect; lf t55 dump"`
- `proxmark3 -c "lf t55 detect; lf t55 read -b 2"`
- `proxmark3 -c "lf t55 detect; lf t55 read --pg1 -b 3"`
- `proxmark3 -c "lf t55 detect; lf t55 write -b 5 -d CCC71509"`

Interactive:
- `lf tune`
- `lf t55 detect`
- `lf t55 dump`
- `lf t55 read -b 2`
- `lf t55 read --pg1 -b 3`
- `lf t55 write -b 5 -d CCC71509`

---

## Appendix C -- Binary protocol reference (Stage B)

### Packet format (NG, command to device)
```
[magic:4] [length:15|ng:1] [cmd:2] [data:0..512] [crc:2]
 PM3a                                              a3 (or CRC-14a)
```

### Packet format (NG, response from device)
```
[magic:4] [length:15|ng:1] [status:1] [reason:1] [cmd:2] [data:0..512] [crc:2]
 PM3b                                                                    b3 (or CRC-14a)
```

### Key T55xx structs
```c
typedef struct {
    uint32_t data;
    uint32_t pwd;
    uint8_t blockno;
    uint8_t flags;
} PACKED t55xx_write_block_t;  // payload for CMD_LF_T55XX_WRITEBL
```

### T55xx read flags
```
bit 0:  password mode
bit 1:  page (0 or 1)
bit 2:  test mode
bit 3-4: downlink mode (0=fixed, 1=LLR, 2=leading-zero, 3=1-of-4)
bit 5:  reg_read mode (no address)
bit 6:  read command (no data packet)
bit 7:  reset
bit 8:  brute force / leave field on
bit 9-11: block number (0-7)
```
