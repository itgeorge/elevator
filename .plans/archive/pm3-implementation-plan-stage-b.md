... This document continues from `pm3-implementation-plan.md`

## Stage B: Native Binary Protocol

**Branch:** `pm3-integration` (not merged to main yet)

### Slice progress

| Slice | Scope | Status |
|-------|--------|--------|
| **Slice 1** | Native connect, ping, hw version, LF tune | ✅ Complete (`00a9ac8`) |
| **Slice 2** | Native T55 detect + read (blocks 0–7), demod, integration tests | ✅ Complete (`828ae8d`, `8f2ef38`) |
| **Slice 3** | Native T55 write + dump | 🔲 Next — see `pm3-slice-3-handoff.md` |
| **Slice 4** | Production enablement, macOS/Linux validation | 🔲 Planned — tasks in handoff doc |

### Phase 8: Native USB Communication

**Goal:** Replace `Pm3ProcessExecutor` with `Pm3NativeExecutor` that speaks the Proxmark3 binary packet protocol directly over USB CDC serial. This eliminates the pm3 client dependency and produces a single self-contained .NET executable portable to any platform.

**Prerequisites:** Stage A complete and validated. Token configuration (Block 0) known from Stage A testing.

**Background:** The Proxmark3 firmware communicates using `PacketCommandNG` / `PacketResponseNG` binary packets over USB CDC (which appears as a serial port). The protocol is documented in [doc/new_frame_format.md](https://github.com/RfidResearchGroup/proxmark3/blob/master/doc/new_frame_format.md) and defined in [include/pm3_cmd.h](https://github.com/RfidResearchGroup/proxmark3/blob/master/include/pm3_cmd.h).

### Sub-phases

#### 8a: Protocol Layer

- [x] **8a.1** Add `System.IO.Ports` NuGet package to `Pm3UsbApi.csproj`.

- [x] **8a.2** Implement packet structures in C#:
  - `PacketCommandNG`: preamble magic `0x61334d50` ("PM3a"), 15-bit length, NG flag, 16-bit cmd, variable data, postamble CRC or magic `0x3361`.
  - `PacketResponseNG`: preamble magic `0x62334d50` ("PM3b"), 15-bit length, NG flag, status, reason, 16-bit cmd, variable data, postamble CRC or magic `0x3362`.
  - CRC-14a computation (or use magic postamble placeholder for USB where CRC is optional).
  - Implemented in `Pm3UsbApi/Native/Protocol/Pm3NgPacketCodec.cs` (NG + OLD 544-byte MIX frames).

- [x] **8a.3** Implement serial port transport:
  - Open USB CDC serial port (`SerialPort` class, cross-platform).
  - Device auto-detection: enumerate serial ports, try each one with `CMD_PING`.
  - Send packet: serialize `PacketCommandNG` to bytes, write to serial port.
  - Receive packet: read from serial, sync on magic bytes, parse `PacketResponseNG`.
  - Handle fragmented receives (USB splits large responses into 128-byte chunks).
  - Implemented in `Pm3UsbApi/Native/Transport/Pm3SerialTransport.cs`.

- [x] **8a.4** Implement basic commands to validate the protocol layer:
  - `CMD_PING` (0x0109) -- send ping, verify pong response.
  - `CMD_VERSION` (0x0107) -- get firmware version string.
  - [ ] `CMD_CAPABILITIES` (0x0112) -- get device capabilities. *(Optional; not needed for elevator token path yet.)*

- [x] **8a.5** Unit tests for packet serialization/deserialization using known reference frames from the protocol docs.
  - `Pm3UsbApi.Tests/Native/Pm3NgPacketCodecTests.cs`

#### 8b: T55xx Write + LF Tune

- [ ] **8b.1** Implement `CMD_LF_T55XX_WRITEBL` (0x0215):
  - Payload: `t55xx_write_block_t` = 4 bytes data + 4 bytes password + 1 byte block number + 1 byte flags.
  - Response: `PM3_SUCCESS` with no data payload.
  - Map to `Pm3.WritePage0BlockAsync()`.
  - **Slice 3**

- [x] **8b.2** Implement `CMD_MEASURE_ANTENNA_TUNING_LF` (0x0402):
  - Parse tuning response data.
  - Map to `Pm3.StartLfTune()` / `Pm3.GetLfTuneLastMilliVolts()`.
  - Slice 1

#### 8c: T55xx Read (requires signal processing)

- [x] **8c.1** Implement `CMD_LF_T55XX_READBL` (0x0214):
  - Send read command (triggers firmware to capture raw ADC samples into BigBuf).
  - Response: `PM3_SUCCESS` with no data (samples are in device BigBuf).

- [x] **8c.2** Implement `CMD_DOWNLOAD_BIGBUF` (0x0207) / `CMD_DOWNLOADED_BIGBUF` (0x0208):
  - Download raw ADC samples from device memory.
  - Handle chunked transfer (device sends 128-byte USB packets).
  - Firmware returns OLD 544-byte frames (not NG) for BigBuf download.

- [x] **8c.3** Implement demodulator for the specific token configuration:
  - Token: RF/64, Block 0 `0x00148040` (elevator tags).
  - ASK/Manchester demod in `Pm3LfDemod.cs` + `Pm3T55NativeService.cs`.
  - Sample conversion: `raw[i] - 127` (matches proxmark3 `getSamplesFromBufEx`).

- [ ] **8c.4** Implement `CMD_LF_T55XX_RESET_READ` (0x0216) as alternative read approach:
  - Sends reset to T55xx, chip transmits all page 0 data.
  - Candidate implementation path for **Slice 3 dump** (see `cmdlft55xx.c` `CmdT55xxDump`).

#### 8d: Integration

- [x] **8d.1** Create `Pm3NativeExecutor` implementing `IPm3CommandExecutor`:
  - Typed `IPm3DeviceCommand` dispatch (detect, read, tune, hw version).
  - Output via `Pm3NativeOutputBuilder` for existing parsers.

- [x] **8d.2** Update `Pm3.cs` to support selecting executor type via `Pm3Options`:
  - `Pm3Options.ExecutorKind` + `PM3_EXECUTOR` env (`process`|`native`).
  - Default remains `Process` for backward compatibility.

- [x] **8d.3** Run integration test suite against the native executor (read path):
  - Parameterized `[TestFixture(Process)]` + `[TestFixture(Native)]` in `Pm3IntegrationTests.cs`.
  - Native: 10 pass, 7 skipped (write/dump/CLI/sequential write tests).
  - Write/dump parity unblocked in **Slice 3**.

- [ ] **8d.4** Cross-platform validation:
  - [x] Windows (USB CDC as COM port) — validated on COM4.
  - [ ] macOS (USB CDC as `/dev/cu.usbmodem*`) — **Slice 4**
  - [ ] Linux (USB CDC as `/dev/ttyACM0`) — **Slice 4**

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

12. **Slice handoff:** For Slice 3+ context, start with `pm3-slice-3-handoff.md`.
