# RidesTablet feedback prototype — 2026-10-16

## Feedback status

- Fixed: positive EUR changes disappeared in dark appearance because `.primary` resolved to white on a white card. The value now has explicit contrast; white operator/signal cards use a light local color scheme so Current and signal labels remain readable too.
- Changed: pending equation includes the current count and signed delta, e.g. `73 + 150 =`, followed by the pending total.
- Added: session-local `Calibrate: NO token` → `Calibrate: PLACE token` → `Detect token` primary action. Each calibration sample tunes and checks T55 presence, rejects the wrong condition without advancing, and rejects equal voltage samples. The meter maps empty/present samples to 10%/90%, supporting either voltage direction. Calibration does not read or write token blocks.
- Removed: unused Cancel / Apt # / Save row in both layouts. Block 4 still participates in normal token scanning.
- Investigated, not changed: Charge performance. One HTTP mutation preflights blocks 5/6, writes both changed copies, and verifies each. Native writes already perform an internal readback, so the successful two-block path has six physical block reads, two writes, and four fresh T55 detects. Next prototype candidate: reuse a detected session within one serialized mutation while retaining preflight, verification, and recovery. No write safeguards were removed.

## Checks

- Focused Swift tests: 41 passed (DomainStateTests, NetworkRideTokenDeviceTests, BridgeClientTests).
- Focused bridge tests: 15 passed (Page0ScanBridgeTests), plus native-style no-chip calibration regression: 1 passed.
- Device Debug build succeeded and installed in place on the physical iPad Air 4; app launched successfully.
- Saved pairing reconnected to the updated real-PM3 bridge with proof/status HTTP 200.
- Simulator visual smoke: positive pending `73 + 150 = 223` and `€4.50` visible in dark system appearance; Apt row absent. Screenshot: `/tmp/RidesTablet-positive-dark-prototype.png`. This visual check used FakeProxmark, not physical hardware.
- Physical read-only adapter smoke: initial token sample 61,178 mV / chip present; empty sample 70,623 mV / chip absent; replaced token sample 58,755 mV / chip present. Both token scans returned block 4 `82045966` and unchanged mirrors `0DC7A9EB` / `0DC7A9EB`. Initial scan ~1.95 s; final scan ~1.95 s. Placement changed the measured loaded signal as expected.
- No physical Charge, Reset, or token write was performed. Full interactive iPad calibration remains for operator testing; adapter measurements and model progression were checked separately.

## Running prototype

The updated bridge was left running on TCP 5080 with the normal durable pairing store. To restart manually from this repo:

```sh
dotnet run --project RidesBridge/RidesBridge.csproj -- --everyday
```

Calibration is intentionally not persisted in this prototype: repeat the two steps after a new app session. Keep Mac and iPad on the same trusted network.

## Follow-up prototype

- Detect retains a fresh LF signal even when no T55 token is detected. The existing single scan returns the measured millivolts with its `409 no_chip` response; there is no second measurement request. Unavailable hardware clears stale values, and legacy no-chip responses without a measurement remain supported.
- EUR controls now have outer ±€1.50 and inner ±€1.00 buttons. The latter adjust 33 whole rides (actual payable change €0.99); the central value and history always show actual cost.
- Completed-charge history is stored on the iPad, newest first, capped at 100. Only successful charges create entries. Each row has clickable local `HH:mm` time expanding to `yyyy-MM-dd HH:mm:ss`, sequence, signed ride equation and EUR amount. It scrolls below Detect in landscape and below ride counts in portrait.
- Checks: 58 focused Swift tests and all 203 RidesBridge tests passed. The history fixture screenshot `/tmp/RidesTablet-followup-history.png` uses FakeProxmark and isolated persistence, not real token writes. It shows positive and negative history rows, inner EUR controls, and the actual €0.99 pending amount.
- Physical empty-reader check: calibration measured 70,597 mV / chip absent; normal scan returned `NoChip` with its fresh 70,503 mV measurement. No real Charge, Reset, or write operation was performed.
- Updated app installed and launched; updated bridge left running on port 5080. Full interactive timestamp/scroll and landscape review remain operator prototype checks.

## History/controls polish and hotspot reconnect

- Removed the inexact ±€1.00 controls; ±€1.50 remains.
- Added the blue PENDING card's top-right ×. It sets the pending total to zero, preserving current count/token and performing no write. Normal Charge remains explicit.
- History now uses body-sized single-line column rows, separators and a TOTAL row for count, net added rides and net EUR over the retained history (up to 100 charges). Shared horizontal scrolling preserves alignment in narrow landscape space; rows scroll vertically. Full timestamps open in a popover without changing the row layout.
- Confirmed calibration is only a meter indication after setup. Added a regression accepting a 3,000 mV known-token scan after 100–200 mV calibration. Out-of-range values clamp the meter, not token acceptance.
- 60 focused Swift tests passed; physical iPad build/install/launch succeeded. Simulator visual fixture screenshot: `/tmp/RidesTablet-history-totals.png` (fake charges, isolated history).
- Operator moved both devices to the phone hotspot. Restarted the bridge with unchanged durable pairing data at `http://172.20.10.4:5080/`; physical iPad reconnected with proof/status HTTP 200 and no new PIN. No token operations were initiated during this polish pass.

## Zero resets, full-width history, and archived unknown dumps

- Removed the operator Mercury profile's explicit 500-ride reset override. Every operator sequence now encodes zero into reset mirrors and reports zero afterward. The legacy Mercury 500 identity fixture remains historical test data, not the operator reset target.
- Removed the visible Sequence column while retaining saved metadata. Time/ride columns remain compact; EUR expands to the available right edge, and separators stretch across the full table width. Simulator fixture screenshot: `/tmp/RidesTablet-reset0-table-fullwidth.png` (fake-only charges).
- Full Swift suite: 213 tests passed. Updated physical iPad app built and installed. The bridge was already stopped by the operator and was left stopped. No physical reset or charge was performed.
- Copied the iPad's UnknownDumps read-only before reinstalling. Preserved 12 unique 32-byte page-0 snapshots (10 source mtimes on Oct 16 UTC and 2 on Sep 18 UTC) under [`investigations/unknown-tokens/2026-10-16`](../investigations/unknown-tokens/2026-10-16/README.md).
- Archive includes raw bytes, SHA-256, original names/source timestamps, all eight big-endian blocks, identity grouping, current-registry candidate checks, and a follow-up paired-capture protocol. Eleven snapshots have matching but undecodable mirrors; one has mirror disagreement. These are not proof of 12 new sequences. No actual ride counts or ride transitions were inferred. All 12 archived sizes and hashes verified; rotation-0 decoder sanity checked at Neptune/256.

## Landscape history clipping

- Reduced the landscape history's internally scrolling row viewport to 130 pt (portrait remains 200 pt) so the complete card clears the bottom action area. The outer horizontal scroller uses its full vertical ideal size to preserve bottom padding and rounded corners.
- 23 focused Swift tests passed; final iPad build and in-place install succeeded. Operator confirmed the physical landscape fix works. Bridge remains stopped; no hardware/token operations performed.
