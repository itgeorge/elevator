# Slice 6 — Capabilities on Connect (S4.7)

**Status:** 🔲 Not started  
**Depends on:** Slice 5 (native default)  
**Branch:** `pm3-integration`

## Goal

Fetch `CMD_CAPABILITIES` (0x0112) after native connect, parse `capabilities_t`, and use it for sizing and early validation.

## Background

Proxmark3 client calls this in `comms.c` immediately after opening the port. Response is a packed struct (`CAPABILITIES_VERSION = 7`):

- `bigbuf_size` — sample RAM size (we hardcode 12 000 today)
- `baudrate`, `via_usb`, `via_fpc`
- `compiled_with_lf` and other feature flags
- `is_rdv4`, flash/smartcard availability

## Tasks

- [ ] Add `Pm3Capabilities` record + decoder from response bytes (mirror `pm3_cmd.h` `capabilities_t`)
- [ ] Add `CmdCapabilities = 0x0112` to `Pm3CommandCodes`
- [ ] Call capabilities during native connect (after version/ping), store on `Pm3NativeExecutor` or transport
- [ ] Use `bigbuf_size` where acquisition/download sizes are chosen (clamp T55 sample count if needed)
- [ ] Fail fast with clear error if `compiled_with_lf == false` before T55 ops
- [ ] Unit tests: golden-byte decode for `capabilities_t` v7; version mismatch handling
- [ ] Optional integration test: connect and assert `bigbuf_size > 0`

## Key files

- `Pm3UsbApi/Native/Protocol/Pm3CommandCodes.cs`
- `Pm3UsbApi/Native/Pm3NativeExecutor.cs` (connect path)
- New: `Pm3UsbApi/Native/Protocol/Pm3Capabilities.cs`
- `Pm3UsbApi.Tests/Native/Pm3CapabilitiesTests.cs`

## References

- `~/proxmark3/include/pm3_cmd.h` — `capabilities_t`, `CMD_CAPABILITIES`
- `~/proxmark3/client/src/comms.c` — fetch on connect

## Done when

Capabilities fetched on native connect; LF feature guard works; unit tests pass; master plan S4.7 marked `[x]`.
