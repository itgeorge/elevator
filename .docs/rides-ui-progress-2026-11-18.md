# Rides UI work — 18 Nov 2026

## Physical acceptance and final deployment

- Physical XCTest `PhysicalRidesUITests/testPhysicalRidesAcceptance` passed on iPad Air 4 (UDID `00008101-001A68EE21F0001E`) in 141.637s with 0 failures, using live RidesBridge + PM3. Result: `/tmp/rides-ui-physical-e2e-attempt3.xcresult`; log: `/tmp/rides-ui-physical-e2e-attempt3.log`.
- Verified real writes and UI flows: 200 rides +10×3 returned to 200, no additional charge; Cancel/Skip/Save and edit-only apartment change with pending preserved; loaded session accepts charge; secret must be reentered after app restart; Venus then Mercury reset preserves apt 43; calibration cancel/confirm preserves completed history. Test-only calibration bypass remained explicit DEBUG opt-in and called real `model.detect`; no fake signal bounds or Auto testing.
- Fresh authenticated bridge read decoded Mercury, 0 rides, apartment 43, mirrors match (`/tmp/rides-ui-physical-e2e/final-token.json`). No restore/write was done after the test.
- Physical device session copy verified active session has 3 charges, +3 rides, €0.09 and two other retained empty sessions. No apartment secret text found in session files.
- Exported XCTest attachments; committed selected full-screen evidence under `RidesTablet/Screenshots/ui-2026-11-18`. Landscape screenshot was losslessly rotated upright from the sideways native attachment, with no crop; raw export remains in `/tmp/rides-ui-physical-e2e/final-attachments`.
- Normal Debug app build succeeded, installed, and launched on the iPad with `--terminate-existing` and no test environment. Build log: `/tmp/rides-ui-final-normal-build.log`. Bridge server left running. App is expected to require calibration and secret reentry.
- Mac test-client credential was revoked and removed locally; iPad credential was not revoked. No credentials or raw token backup committed.
- Compact evidence receipt: `.docs/rides-ui-e2e-2026-11-18.json`; see it and README for screenshot links/provenance.
- Simulator final 269-unit + 13-UI suite remains owned by the separate simulator agent; its result is intentionally not claimed here.
