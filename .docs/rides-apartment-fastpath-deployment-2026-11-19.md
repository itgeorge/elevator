# Apartment fast path — deployed and physically verified, 19 Nov 2026

Machine-readable receipt: [JSON](rides-apartment-fastpath-deployment-2026-11-19.json). Physical token-removal/reappearance testing is deferred until the user returns; cancellation coverage so far is controlled unit/simulator testing.

## Validation

- Full serial combined simulator suite: **303 passed, 0 failed, 2 expected physical skips**; `/tmp/rides-apt-fastpath-simulator/combined-after-709a1d2.xcresult`.
- Commit `167253b` replaces an XCTest expectation callback that crashed the hosted app with bounded polling. The targeted test and subsequent full combined suite passed.

## Bridge deployment

- Confirmed the existing bridge at the exact loopback health URL with `curl --noproxy '*'`: HTTP 200, `status=ok`, API `v1`, bridge `1.0.0`.
- Built the current worktree's native bridge successfully, then gracefully stopped only owned PIDs 45227 (`dotnet run ... --everyday`) and 45237 (its `RidesBridge --everyday` child).
- Started the new Debug bridge with `--everyday`, same default durable data directory, and loopback health returned `status=ok`, API `v1`, bridge `1.0.0`. It is left running as PID recorded privately in `/tmp/rides-apartment-new-bridge.pid`.
- Verified persisted identity and paired-client store bytes were unchanged across restart by SHA-256; the store still has 19 records, 18 active. No credential/verifier contents were displayed.

## Physical apartment-only edit — completed

- The initial signing failure was resolved using the already cached, unexpired profile for `com.itgeorge.RidesTablet`, matched to the local signing identity. Provisioning profile TeamIdentifier was `TJY5296P7S` (not the certificate CN suffix). Installed the public cached profile in Xcode's profile cache; no Apple account, key, or credential was changed.
- Built and installed the current Debug app on iPad UDID `00008101-001A68EE21F0001E`, preserving the existing app bundle/keychain identity. The paired app's real Detect succeeded against the newly restarted bridge. Independent fresh app readback before mutation showed **0 rides, Apt 43**, with **no pending ride change**, **3 completed charges** and **€0.09**.
- Added and ran one focused physical-only UI test (not a simulator fixture): `testApartmentOnlyEdit43To44PreservesRidesHistoryAndPending`. It opened the existing Apt 43 edit prompt, set Apt 44 and invoked Save exactly once. The test passed; no retry occurred. Assertions confirmed the displayed rides remained 0, pending remained unchanged, and completed-charge count/history were unchanged.
- The same test then tapped ordinary Detect as a separate readback operation. Fresh readback showed **0 rides and Apt 44**; pending and completed-charge history were still unchanged. XCTest result: `/tmp/rides-apt-fastpath-device/apartment-edit-physical.xcresult`; protected log: `/tmp/rides-apt-fastpath-device/apartment-edit-physical.log`.
- Bridge HTTP log shows exactly **one** `POST /api/v1/hardware/apartment/block4/write-verified`, HTTP 200, completed in **835.193 ms**. No `page0/scan`/read endpoint appears between the final background presence probes and this POST. Seven preceding `/hardware/presence` requests are background presence polling, distinct from the Save request. A subsequent `page0/scan` was the explicitly separate readback Detect.
- Native PM3 session transcript `/var/folders/ty/cs9d984d4s926vpqm0df5_sc0000gn/T/elevator/pm3-62387-20261119004813-session.log` records one `lf t55 write -b 4` at 00:51:27.460 with successful native result (exit 0, 816 ms). No native block read or T55 detect occurs in that write interval; preceding T55 detects are the separate presence probes. The external Detect begins at 00:51:29.361 and performs the subsequent reads. Sensitive write payload is withheld. Native packet-level trace was disabled; evidence is the bridge HTTP request log plus PM3 command/session transcript.
- Actual iPad screenshots from the physical result are retained in `RidesTablet/Screenshots/apartment-fastpath-2026-11-19/` (pre-edit Apt 43, Apt 44 before Save, after Save, and separate fresh Detect/readback).
- Rebuilt, reinstalled, and launched the **normal Debug app** after the physical UI test, without physical-write compile flags or runner environment. Build log: `/tmp/rides-apt-fastpath-device/final-normal-debug-build.log`; app binary SHA-256 `7cbe774dd36f79040dad9c87ccc204476c9b08cdc25aeb50dc8c4a6708ee1ed1`. Bridge remains running as PID 62387 and loopback health is HTTP 200. No full charge acceptance, token reset, or token-removal/reappearance test was performed.
