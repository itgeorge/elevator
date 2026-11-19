# Apartment fast-path deployment attempt — 19 Nov 2026

## Validation

- Full serial combined simulator suite: **303 passed, 0 failed, 2 expected physical skips**; `/tmp/rides-apt-fastpath-simulator/combined-after-709a1d2.xcresult`.
- Commit `167253b` replaces an XCTest expectation callback that crashed the hosted app with bounded polling. The targeted test and subsequent full combined suite passed.

## Bridge deployment

- Confirmed the existing bridge at the exact loopback health URL with `curl --noproxy '*'`: HTTP 200, `status=ok`, API `v1`, bridge `1.0.0`.
- Built the current worktree's native bridge successfully, then gracefully stopped only owned PIDs 45227 (`dotnet run ... --everyday`) and 45237 (its `RidesBridge --everyday` child).
- Started the new Debug bridge with `--everyday`, same default durable data directory, and loopback health returned `status=ok`, API `v1`, bridge `1.0.0`. It is left running as PID recorded privately in `/tmp/rides-apartment-new-bridge.pid`.
- Verified persisted identity and paired-client store bytes were unchanged across restart by SHA-256; the store still has 19 records, 18 active. No credential/verifier contents were displayed.

## Physical operation blocked before token access

- Attempted the requested normal iPad Debug build using the connected device's Xcode UDID. The project has no `DEVELOPMENT_TEAM` configured, so the first build could not sign.
- Retried with the existing local signing identity's team and the installed app's bundle identifier, including `-allowProvisioningUpdates`. Xcode reported **“No Account for Team”** and **“No profiles”** for `com.itgeorge.RidesTablet`; it cannot provision without an Apple developer account authenticated in Xcode.
- No app was installed or launched, no physical Detect was performed, and no PM3 presence call or token mutation was made. Consequently no apartment value, external readback, history/pending comparison, screenshots, or native write trace can be reported. The requested Apt 43 → 44 edit remains unattempted; no retry is pending.
- The bridge remains healthy. Before proceeding, configure valid Xcode account/provisioning for the already-installed app's identifier (preserving Keychain access), then build/install/launch, verify pairing via the app's real Detect, and perform one explicitly authorized apartment-only save with the required live trace/readback checks. No token-removal test was attempted.
