# Slice 6 — Capabilities on Connect (S4.7)

**Status:** ✅ Complete  
**Depends on:** Slice 5 (native default)  
**Branch:** `pm3-integration`

## Goal

Fetch `CMD_CAPABILITIES` (0x0112) after native connect, parse `capabilities_t`, and use it for sizing and early validation.

## Background

Proxmark3 client calls this in `comms.c` immediately after opening the port. Response is a packed 13-byte struct:

- `bigbuf_size` — sample RAM size (we hardcode 12 000 today)
- `baudrate`, `via_usb`, `via_fpc`
- `compiled_with_lf` and other feature flags
- `is_rdv4`, flash/smartcard availability

**Firmware note:** Current client defines `CAPABILITIES_VERSION = 7`, but many devices still report **v6** with the same 13-byte layout (baudrate field zeroed; use 115200 default). Decoder accepts v6–v7.

## Tasks

- [x] Add `Pm3Capabilities` record + decoder from response bytes (mirror `pm3_cmd.h` `capabilities_t`)
- [x] Add `CmdCapabilities = 0x0112` to `Pm3CommandCodes`
- [x] Call capabilities during native connect (after version/ping), store on transport
- [x] Use `bigbuf_size` where acquisition/download sizes are chosen (clamp T55 sample count if needed)
- [x] Fail fast with clear error if `compiled_with_lf == false` before T55 ops
- [x] Unit tests: golden-byte decode for v7 + device-captured v6; version mismatch handling
- [x] Integration test: connect and exercise T55 read (LF guard + bigbuf sizing)

## Key files

- `Pm3UsbApi/Native/Protocol/Pm3CommandCodes.cs`
- `Pm3UsbApi/Native/Pm3NativeExecutor.cs` (connect path)
- `Pm3UsbApi/Native/Protocol/Pm3Capabilities.cs`
- `Pm3UsbApi/Native/Transport/Pm3SerialTransport.cs`
- `Pm3UsbApi.Tests/Native/Pm3CapabilitiesTests.cs`
- `Pm3UsbApi.Tests/Integration/Pm3NativeCapabilitiesIntegrationTests.cs`

## References

- `~/proxmark3/include/pm3_cmd.h` — `capabilities_t`, `CMD_CAPABILITIES`
- `~/proxmark3/client/src/comms.c` — fetch on connect

## Done when

Capabilities fetched on native connect; LF feature guard works; unit tests pass; master plan S4.7 marked `[x]`.
