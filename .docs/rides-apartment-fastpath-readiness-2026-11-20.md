# Apartment fast-path / presence-abort readiness — 20 Nov 2026

## Scope completed in this bounded turn

- Baseline is `8263505` (`test/rides-apt-fastpath-e2e`); worktree was clean at start.
- Read `.docs/rides-ui-followup-handoff.md`, `.docs/rides-ui-e2e-2026-11-19.md`, and the existing physical/UI test sources, including `RidesTabletUITests/PhysicalRidesUITests.swift`.
- Added a deterministic simulator UI regression for apartment-only edit: saving a changed apartment must close the editor, keep completed-charge history at zero, and preserve the pending-rides value. This is a UI assertion, not proof of backend request contents or physical behavior.
- No simulator/UI/unit tests were run. No device tests, hardware commands, mutations, field-off, or server restarts were performed.

## Current readiness (read-only observations)

- Existing bridge process identity still matches the handoff: parent PID `45227` (`dotnet run ... --everyday`) and child PID `45237` (`RidesBridge --everyday`). Read-only `GET http://127.0.0.1:5080/api/v1/health` returned `{"status":"ok","apiVersion":"v1","bridgeVersion":"1.0.0"}`. `/` and `/health` are not health routes and returned 404; use `/api/v1/health`.
- Xcode reports simulator `3F9DD641-D0EF-432D-ACEB-F8BE555DEBBA` booted and iPad CoreDevice `494BBD09-0DEB-5B41-B915-3B4258F1DBA2` available/paired. `/dev/cu.usbmodem11301` and `/dev/tty.usbmodem11301` exist. This confirms USB device-node availability only, not an active PM3 protocol session. No test has claimed the simulator lock in this turn.
- Source inspection only: the existing native bridge exposes LF signal measurement (`GET /api/v1/hardware/signal`) without T55 detect or block reads, and full page-0 scans combine tune, T55 detect and reads. The native LF tune command sequence explicitly starts, samples, then shuts down the tune operation; this is not a read-only query of the PM3 RF field-off state. T55 detect is the extant token-presence operation. No separate PM3 SDK/field-state API or RF-off control was found in the inspected project API/source. Do not infer physical token removal/reappearance from signal-only samples, fake fixtures, or an uncalibrated threshold.
- Relevant read-only code: `RidesBridge/Pm3BridgeDeviceAdapter.cs` (`MeasureSignalAsync`, `ScanPage0Async`), `RidesBridge/BridgeApplication.cs` (`/api/v1/hardware/signal`, `/page0/scan`), `Pm3UsbApi/Native/Pm3NativeLfTune.cs`, and `Pm3UsbApi/Native/T55/Pm3T55NativeService.cs` (`Detect`). No hardware requests were sent.

## Next safe steps / blockers

1. Wait for integration of the model contract and the narrowly scoped bridge implementation. Do not add a competing implementation in the UI worktree.
2. Then add deterministic mocked presence-abort UI cases for both charge and apartment edit using an explicitly labeled simulated `present → absent → present` fixture. Assertions must prove that absence cancels the entire pending operation (no charge-history entry, no apartment success, pending edits retained), and recovery allows a fresh attempt. Keep fixture-only behavior out of claims about physical removal.
3. Add physical cases only after parent GO. Current inspected API offers no documented read-only RF-off field-state control; a credible real absence/reappearance acceptance requires controlled actual removal/replacement or a separately reviewed PM3 mechanism. Never issue RF-off as readiness investigation.
4. Ensure operation traces distinguish background signal-only probes from fresh T55 detect calls, and record endpoint request count/latency independently from native internal verified-write readback. Apartment-only fast path should produce no Save/detect/read on the tablet path; background presence probes are a separately labeled path and full reads remain conditional on a justified probe result.
