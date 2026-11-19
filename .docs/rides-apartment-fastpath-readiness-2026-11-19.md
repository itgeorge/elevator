# Apartment fast-path / presence-abort readiness — 19 Nov 2026

## Integration and scope

- Merged `master` at `5aca501` into the validation branch; merge commit `801654b`.
- Added deterministic DEBUG simulator-only `RIDES_SIMULATOR_PRESENCE_SEQUENCE` (`present,absent,present`) handling in `RidesTablet/Views/RidesRootView.swift`, with a delayed scripted probe to leave a bounded UI interaction window. It delegates all scanning and writes to `FakeProxmark`; this is explicitly mocked and is not physical removal/reappearance evidence.
- Added UI regressions for charge and edit popup abort/reopen after the mocked presence sequence, plus apartment-only edit preserving completed-charge history and pending rides.

## Integrated test results

Full combined `xcodebuild test`, serial (`-parallel-testing-enabled NO`, `-collect-test-diagnostics never`):

- **Unit:** 284 executed; 282 passed, **2 failed**.
- **UI:** 20 executed; **18 passed, 2 expected physical skips, 0 failures**. This includes the new apartment-only edit and both mocked presence-loss/reappearance cases.
- Overall command exits **FAILED** due to the two model unit regressions. Do not report the combined suite green and do not proceed to physical writes/deployment until they are resolved and the latest combined run is green.
- Exact command log: `/tmp/rides-apt-fastpath-simulator/final-suite.log`; result bundle: `/tmp/rides-apt-fastpath-simulator/final-suite.xcresult`.
- Focused simulator UI run passed all three owned UI regressions; result `/tmp/rides-apt-fastpath-simulator/focused-ui5.xcresult` (the first two presence cases) and `/tmp/rides-apt-fastpath-simulator/focused-ui4.xcresult` (apartment edit case passed; presence interaction needed the final actor/preparation fixture correction subsequently exercised in `focused-ui5`).

The remaining model failures are:

1. `ApartmentModelTests.testAbsentPresenceArrivingWhileSaveIsDrainingPreventsWrite`, line 180: immediate `XCTAssertFalse(model.isApartmentSheetPresented)` after spawning `saveTask` races actor scheduling; save has not necessarily entered the model before the assertion. The subsequent held-probe/write assertions were not reached.
2. `ApartmentModelTests.testLateOldPresenceGenerationCannotInvalidateFreshPopup`, line 165: completing the held `.absent` result invalidates the fresh popup, contrary to the generation guarantee. Test also emits a leaked continuation warning. This appears to be a genuine generation/lifecycle behavior issue, or a test fixture/contract mismatch; model owner should review before a minimal fix. No assertions were weakened.

## Read-only readiness

- Bridge old process identities were confirmed before any server action: PID 45227 `dotnet run ... --everyday`, child PID 45237 `RidesBridge --everyday`. Health route `/api/v1/health` returned `ok`, bridge version `1.0.0`. Neither process was stopped or restarted in this blocked run.
- Simulator `3F9DD641-D0EF-432D-ACEB-F8BE555DEBBA` booted; iPad CoreDevice `494BBD09-0DEB-5B41-B915-3B4258F1DBA2` available/paired; USB device nodes `/dev/cu.usbmodem11301` and `/dev/tty.usbmodem11301` exist. No physical request, test, or token mutation was performed.
- Current source supports authenticated `/api/v1/hardware/presence` and `/api/v1/hardware/apartment/block4/write-verified`; backend implementation/unit counts supplied by parent are not a substitute for integrated combined UI/model green or for live physical endpoint traces.
- No actual physical removal/reappearance was performed. The physical PM3 field-off/presence-state capability was not tested; previous source inspection found signal-only tune and T55 detection, not a separate read-only field-state query.

## Blocker / next steps

1. Send the two failures above to the model owner/parent for a minimal regression fix; retain these results as failing evidence.
2. Rerun the complete serial combined unit/UI test against the corrected integration; preserve new log/result bundle. Continue only if green, retaining the two expected physical skips.
3. Only after green: follow the GO instructions to verify/stop only the owned old bridge processes, relaunch the latest native bridge on the same port/storage/client registry, verify health and iPad pairing, then perform the authorized Apt 43 → 44 apartment-only physical edit once. Inspect live request and SDK trace before concluding; no blind mutation retries.
4. No controlled actual token removal/reappearance was confirmed. If unavailable, limit absence proof to deterministic unit/simulator results and clearly state this limitation; do not claim full physical absence verification.
