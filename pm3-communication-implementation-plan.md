\# Proxmark3 USB Text Interface (No GPL Dependencies) — Implementation Notes



This document describes a \*\*safe, dependency-free approach\*\* for controlling a Proxmark3 over its \*\*USB CDC/serial text interface\*\* by sending the same CLI commands you use in the `proxmark3` client (e.g., `lf tune`, `lf t55 detect`, `lf t55 dump`, `lf t55 write`, etc.).



Goal: Implement `Pm3UsbApi.Pm3` in C# by \*\*speaking plain text over the serial port\*\*, without linking to, embedding, or reusing GPL client libraries.



> Scope: This covers \*\*host-side comms + parsing\*\*. It does not require using any Proxmark3 source code. You will still be running Proxmark3 firmware/client on the device side as usual.



---



\## 1) Sanity check on current class design



Current approach structure is valid:



\- \*\*Connect/Disconnect\*\*: wrap a `SerialPort`/stream.

\- \*\*Command methods\*\* (`StartLfTune`, `Dump`, `ReadPage0BlockAsync`, `WritePage0BlockAsync`): send a command line and parse the textual output.

\- \*\*Session state\*\*: `EnsureT55SessionActive()` calling `lf t55 detect` is correct because subsequent `lf t55 ...` commands often rely on the detected parameters cached in the client/firmware interaction.

\- \*\*Async\*\*: good idea; command IO is naturally async and benefits from timeouts/cancellation.



What to refine:



\- Add \*\*single-flight / command queue\*\* so only one command is in-flight at a time (Proxmark CLI is essentially single-user).

\- Ensure you have a robust \*\*prompt detection\*\* strategy, because “command finished” is best detected by reading until the \*\*Proxmark prompt\*\* returns.

\- Track “tune session” state carefully (some tune outputs are multi-line and may include periodic updates).



---



\## 2) High-level architecture



\### 2.1 Components



\- \*\*Transport\*\*

&nbsp; - Opens the serial device (USB CDC).

&nbsp; - Provides `WriteLineAsync(string)` and a continuous `ReadLoop` to capture lines.



\- \*\*Protocol / CLI Session\*\*

&nbsp; - `SendCommandAsync(cmd, completionPredicate)` that returns the captured output.

&nbsp; - Uses a \*\*prompt recognizer\*\* to know when the command completed.



\- \*\*Parsers\*\*

&nbsp; - Per command: parse tune peak, parse `read` output block hex, parse `dump`, parse errors.



\### 2.2 The “prompt” is your delimiter



Most reliable approach:

\- Read lines until you see a line that matches a prompt pattern such as:

&nbsp; - `proxmark3>` (common)

&nbsp; - `pm3 -->` (some skins)

&nbsp; - `\[`…`] proxmark3>` variants (depends on build)



Implement a configurable list of prompt regex patterns, e.g.:



\- `^proxmark3>\\s\*$`

\- `^pm3\\s\*-->\\s\*$`



Store it as a list and treat “prompt seen” as “command completed”.



> Note: Some outputs may contain the word proxmark3, so match whole-line prompt formats, not substrings.



---



\## 3) Transport details (C#)



\### 3.1 Device discovery



Options:

\- Allow user to optionally pass in a port name

\- Implement auto-discovery and execute if port not specified:

&nbsp; - Windows: enumerate COM ports (WMI or `SerialPort.GetPortNames()` + friendly name check).

&nbsp; - Linux/macOS: scan `/dev/ttyACM\*`, `/dev/cu.usbmodem\*`, etc.



\### 3.2 Serial settings



Typical Proxmark3 USB CDC defaults are commonly:

\- 115200 baud

\- 8 data bits, no parity, 1 stop bit

\- No hardware flow control



But: some builds may use different settings. Make these configurable.



\### 3.3 Line endings \& encoding



\- Send commands terminated with `\\n` (LF). Many devices also accept `\\r\\n`.

\- Use ASCII/UTF-8. Output is mostly ASCII.



---



\## 4) Concurrency \& correctness requirements



\### 4.1 Single command at a time (critical)



The CLI is not designed for interleaved commands. Implement:

\- A `SemaphoreSlim` to serialize `SendCommandAsync`.

\- A single shared read buffer.



\### 4.2 Timeouts \& cancellation



Each command should have:

\- A default timeout (e.g., 2–5s for simple commands, longer for `dump` or `restore`).

\- `CancellationToken` support.



\### 4.3 Reconnection logic



\- `IsConnectedAsync()` should confirm both:

&nbsp; - Serial port is open

&nbsp; - Device still responds (e.g., send a harmless command like `help` or just newline and wait for prompt).



---



\## 5) Error handling rules (text protocol)



Proxmark output can include warnings/errors in many forms.

Implement a conservative error detector:

\- If output contains lines beginning with:

&nbsp; - `\[!]`, `\[-]`, or `error`, `failed`, `timeout`

\- Then return failure / throw a typed exception.



Keep a whitelist for known non-fatal warnings if needed.



---



\## 6) Mapping your methods to CLI commands



\### 6.1 `StartLfTune()`



\- Command: `lf tune`

\- Completion: read until prompt returns.

\- Store the full output in a `LastTuneOutput` field (and timestamp).



\### 6.2 `GetLfTunePeakMilliVolts()`



Parse the last tune output.



You mentioned a line like:



\- `\[=] 60276 mV / 60 V / 60 Vmax`



Robust parsing:

\- Regex: `\\\[=\\]\\s\*(\\d+)\\s\*mV\\b`

\- Return the integer group as `uint`.



If multiple matches exist, choose the \*\*last\*\* match.



\### 6.3 `StopLfTune()`



There isn’t always a distinct “stop” command; tune usually ends when output finishes.

If your build supports aborting long ops:

\- send `\\x03` (Ctrl+C) or `stop`/`abort` if supported by the client skin.

Implementation suggestion:

\- Provide `CancelCurrentOperationAsync()` that writes Ctrl+C and waits for prompt.



If `lf tune` completes quickly on your build, `StopLfTune()` can be a no-op or just `CancelCurrentOperationAsync()` if currently tuning.



\### 6.4 `EnsureT55SessionActive()`



\- Command: `lf t55 detect`

\- Completion: prompt returns.

\- Success criteria: output contains something like `Chip found` and indicates `T55xx` / `T5577` (depends on build).

\- Cache a short-lived “active” flag with expiry (e.g., 2–3 seconds) because tag presence may change.



\### 6.5 `Dump()`



\- Command: `lf t55 dump`

\- Completion: prompt returns.

\- Return raw output (string).

\- Optionally parse into a structured model later.



\### 6.6 `ReadBlockAsync(int page, int block)`



\- If `page == 0`: `lf t55 read -b {block}`

\- If `page == 1`: `lf t55 read --pg1 -b {block}`

\- Parse hex data from output.



Typical output includes a row like:

\- `00 | 00148040 | ...`



Regex options:

\- `^\\\[\\+\\]\\s\*\\s\*{block:00}\\s\*\\|\\s\*(\[0-9A-Fa-f]{8})\\b`

\- or a simpler `\\b{block:00}\\s\*\\|\\s\*(\[0-9A-Fa-f]{8})\\b` if formatting varies



Return the 8-hex string normalized uppercase.



\### 6.7 `WritePage0BlockAsync(int block, string data)`



\- Validate `block` in 0..7.

\- Validate `data` is exactly 8 hex characters.

\- Command: `lf t55 write -b {block} -d {data}`

\- Completion: prompt returns.

\- Success criteria: output contains a success marker (commonly `\[+]` without `failed`).

\- Optional verify: call `ReadBlockAsync(0, block)` and confirm matches.



> Important: Avoid writing block 0 and 7 unless explicitly intended. Consider guarding those blocks behind an explicit override flag.



---



\## 7) Recommended internal helper API



\### 7.1 `SendCommandAsync`



Suggested signature:



\- `Task<CommandResult> SendCommandAsync(string command, TimeSpan timeout, CancellationToken ct)`



Where `CommandResult` contains:

\- `string Command`

\- `string RawOutput`

\- `string\[] Lines`

\- `bool PromptSeen`

\- `bool Success`

\- `string? ErrorSummary`



\### 7.2 Read loop



Have one background task that reads from the serial stream and pushes full lines to:

\- a `Channel<string>` or

\- a thread-safe queue



`SendCommandAsync` subscribes to lines until it sees prompt.



---



\## 8) Testing strategy



\### 8.1 Use a transcript file



During development:

\- Log all input commands and output lines to a timestamped file.

\- This makes parsing bugs trivial to diagnose.



\### 8.2 Unit-test parsers



Feed stored real outputs into parsers and assert:

\- Tune peak parsing

\- Block read parsing

\- Dump extraction



\### 8.3 Hardware-in-loop smoke tests



\- Connect

\- `lf t55 detect`

\- `lf t55 read -b 0`

\- `lf t55 dump`

\- Write to a known-writable block on a sacrificial tag and verify.



---



\## 9) Security / licensing notes



\- \*\*OK\*\*: talk to Proxmark over serial using text commands.

\- \*\*Avoid\*\*: copying client code, headers, structs, or linking libraries from the GPL repo.

\- Treat Proxmark as an external tool/device.



---



\## 10) Implementation checklist (agent-ready)



\- \[ ] Create `Pm3` with injected `Pm3Options` (port name, baud, prompt regexes, timeouts).

\- \[ ] Implement `ConnectAsync` (open serial, start read loop, sync to prompt).

\- \[ ] Implement `DisconnectAsync` (stop read loop, close serial).

\- \[ ] Implement `SendCommandAsync` with:

&nbsp; - \[ ] command serialization (SemaphoreSlim)

&nbsp; - \[ ] timeout + cancellation

&nbsp; - \[ ] capture output until prompt

\- \[ ] Implement `EnsureT55SessionActive` (`lf t55 detect`) with short-lived cache.

\- \[ ] Implement `StartLfTune` (`lf tune`) capturing output.

\- \[ ] Implement `GetLfTunePeakMilliVolts` parser using regex.

\- \[ ] Implement `ReadBlockAsync` with page flag and hex parsing.

\- \[ ] Implement `WritePage0BlockAsync` and optional read-back verify.

\- \[ ] Add logging + transcripts.

\- \[ ] Add unit tests for parsers.



---



\## Appendix A — Regex snippets



\- Prompt (example): `^proxmark3>\\s\*$`

\- Tune peak mV: `\\\[=\\]\\s\*(\\d+)\\s\*mV\\b`

\- 8-hex: `\\b(\[0-9A-Fa-f]{8})\\b`



---



\## Appendix B — Example command lines



\- `lf tune`

\- `lf t55 detect`

\- `lf t55 dump`

\- `lf t55 read -b 2`

\- `lf t55 read --pg1 -b 3`

\- `lf t55 write -b 5 -d CCC71509`



