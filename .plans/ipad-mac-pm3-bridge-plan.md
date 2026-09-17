# iPad-to-Mac PM3 Bridge — Vertical Slice Plan

## How agents should use this plan

Read this entire file before making changes. Start every session with `git status --short --branch`, inspect the current implementation and the latest notes in this plan, then find the next `[ ]` TODO. If new relevant work is discovered, add it under the current slice before continuing. Keep working until the current TODO, or a coherent group of TODOs forming one reviewable vertical chunk, is complete.

Use test-first development where practical: add or tighten a failing unit/contract test, implement the smallest behavior that passes it, then run the focused suite before broader validation. Each slice must retain a deterministic no-hardware test path and end with a physical end-to-end checkpoint where specified.

Mark completed items by changing `[ ]` to `[x]`. Record assumptions, deviations, exact test commands/results, hardware observations, and follow-up work in that slice's notes. Commit plan updates in the same commit as the corresponding code/tests so this handoff stays aligned with implementation. Stage only files owned by the active slice.

### Delegation policy for the long-lived orchestrator

The top-level agent executing this plan is an orchestrator/reviewer, not the default implementation agent.

- For actual implementation and tests, launch fresh `openai-codex/gpt-5.6-luna:high` Vigil subagents, one per independent and appropriately scoped chunk.
- Launch implementation agents with `allowSubagents:false`; delegation remains one level deep.
- Give every implementation agent precise file ownership, acceptance criteria, safety constraints, and explicit instructions not to touch unrelated untracked files.
- Do not run concurrent agents against the same files. Parallelize only disjoint research or file ownership.
- Wait for completion, inspect the diff and tests, identify gaps, and send focused follow-ups or launch a fresh Luna for an independent repair/test slice.
- Keep the prototype-first workflow. Do not introduce infrastructure, abstractions, or protocol features before the current vertical slice requires them.
- The orchestrator owns user communication, hardware-test authorization checks, integration review, plan updates, and final commits.

Known unrelated/pre-existing untracked paths must remain untouched unless the user explicitly assigns them:

```text
debug/EncodeRideBlock/
debug/RideBlockGuessPrototype/.idea/
debug/__pycache__/
debug/red-token-investigation/
debug/write-variant-profile.sh
```

---

## What this work is

Build a local-network bridge that lets the physical iPad operator app control the Proxmark3 connected to the Mac:

```text
RidesTablet on iPad
    -> local HTTP on Wi-Fi/iPhone hotspot
    -> RidesBridge on Mac (.NET)
    -> existing Pm3UsbApi native USB transport
    -> Proxmark3 RDV4
    -> T55xx token
```

The bridge is an intermediate/development/backup hardware path. The iPad owns operator state, ride decoding/encoding, identity profiles, unknown-dump persistence, and the Concept A workflow. The Mac owns PM3 connection lifecycle, serialized hardware access, minimal block reads, safe conditional writes, and read-back verification.

Work proceeds as vertical slices. The first slice proves one authenticated hardcoded block read. Mercury-only reading/writing comes next with full C#/Swift parity. Only then is the complete registered encoding-family set generalized. Token details/reset/unknown handling and Concept A integration follow after those paths have real end-to-end evidence.

---

## End goal of this plan

- A physical iPad can connect to `RidesBridge` on a Mac over normal LAN or the user's iPhone 13 Pro hotspot without internet access.
- Initial manual pairing uses a local bridge URL plus a short-lived PIN; successful pairing yields a persistent random bearer credential stored securely on both sides.
- Direct IP always remains available. QR onboarding and Bonjour convenience are added only after the manual path works.
- The Mac bridge reuses `Pm3UsbApi` directly and exposes no arbitrary PM3 command execution.
- Normal known-token detection reads only what the current workflow needs, not a full page-0 dump.
- Ride writes use exact block-level conditional mutations, not full-token snapshots.
- Swift behavior matches the production C# oracle for Mercury first, then all nine registered sequences.
- Unknown-family handling performs an eight-block read only after blocks 5/6 fail registered decoding, and writes the same 32-byte big-endian dump format.
- The network connector ultimately slots behind a hardware-neutral Swift boundary while `FakeProxmark` remains deterministic.
- Concept A is the only operator UI integration target. Concept B has been removed and must not be restored.
- Every slice has focused automated tests and a documented physical acceptance checkpoint.

---

## Confirmed baseline evidence

### Repository baseline

- Current baseline commit at plan creation: `1aac5b9 Adopt the Concept A tablet layout`.
- `RidesTablet` is iPad-only, iOS 17+, and currently defaults to `FakeProxmark`.
- `Tokens`, `RidesCli`, and their test suites are the behavioral oracle.
- `Pm3UsbApi` already implements native PM3 USB communication on macOS, including connect, detect, tune, T55 block reads/writes, mirror operations, dump, retries, and verification.
- PM3 is RDV4 firmware `Iceman/master/v4.20728-suspect`, USB CDC VID/PID `9AC4:4B8F`, and works through the existing native `.NET` executor.
- Concept A is the current/preferred tablet workflow. Do not plan against old Concept B screenshots or structure.

### Hotspot proof — already completed

The committed `iPadHotspotProbe/` is reproducible development evidence, not production code:

- Mac used `172.20.10.4/28`; iPad used `172.20.10.2` on the iPhone 13 Pro hotspot.
- Physical iPad `GET /health` reached the Mac and returned HTTP 200.
- The iPad discovered `_hotspotprobe._tcp` through Bonjour, resolved it, and reached TCP-ready state.
- No hotspot client isolation was observed.
- Internet access is not required for local bridge operation.
- Direct IP remains required as fallback on networks that block multicast/Bonjour.

### USB/accessory conclusion

The discarded `iPadAccessoryProbe/` established that `EAAccessoryManager` exposed no PM3 accessory. It was intentionally removed before this plan commit and is not part of the implementation. Do not recreate a direct-iPad PM3 USB path in this plan.

---

## Key decisions and constraints

### Network and pairing

- Prototype transport is local HTTP/JSON, not public-internet HTTP.
- Bridge listens on configurable local interfaces/port and prints reachable private URLs.
- Manual direct-IP URL entry is the first supported connection path.
- A six-digit short-lived, one-time PIN exchanges for a cryptographically random bearer token.
- The iPad stores the bearer token in Keychain through an injectable credential-store abstraction.
- The bridge persists only what it needs to recognize paired clients; do not log PINs or bearer tokens.
- Authorization is required for every hardware endpoint. A health/version endpoint may remain unauthenticated but must expose no secrets or hardware mutation.
- QR onboarding may encode URL plus one-time pairing material later. Long-lived bearer tokens must not be placed in reusable QR artifacts.
- Bonjour service type should be purpose-specific, e.g. `_elevator-rides._tcp`, when added.
- No public port forwarding, cloud relay, dynamic DNS, or Tailscale integration is in scope.

### PM3 read pressure

- Do not use full-token snapshots as optimistic-concurrency tokens.
- Do not perform routine full dumps for known tokens.
- The bridge reads only requested blocks and serializes all hardware activity.
- A known-token operator scan should eventually need block 4 and ride mirrors 5/6, plus tune/detect as required—not blocks 0..7.
- An unknown encoding may trigger reads of the missing page-0 blocks exactly once so the iPad can construct the required dump.
- Avoid automatic repeated hardware retries above existing proven PM3 retry behavior. Network clients must not blindly replay ambiguous writes.

### Conditional block mutation

Each requested mutation carries only the block being changed:

```json
{
  "block": 5,
  "expected": "CCC7363B",
  "desired": "CCC749CC"
}
```

Before writing any mutation in a group, the bridge preflights every target block exactly once:

- `current == expected`: eligible to write;
- `current == desired`: already applied; count as success without rewriting;
- otherwise: abort the whole group as a conflict before any write.

After successful preflight:

- write only blocks still equal to `expected`;
- verify each block immediately;
- on a later failure, best-effort rollback already changed blocks to their `expected` values and report every rollback failure;
- if the client disconnects, the server-side operation still finishes verification/rollback;
- never auto-retry a write from the iPad after an ambiguous network timeout—re-read operation/token state first.

Blocks 0 and 7 are always forbidden. Normal ride writes target only 5/6. Reset may target changed blocks in 1..6.

### Hardware safety and authorization

The user has approved read/write testing on the black test card currently placed on the Mac-connected PM3.

- Begin every destructive hardware checkpoint with a read-only block preflight.
- Record exact before/target/after values in this plan.
- Never write blocks 0 or 7.
- Slice 2 hardware writes are limited to blocks 5/6 and must restore the original raw values before completion.
- Later reset/profile tests may touch only 1..6, must snapshot only those target blocks, use the verified per-block path, and restore the original values when the test's purpose permits.
- Stop and ask the user if the observed device/card differs materially from the expected setup or safe restoration cannot be guaranteed.
- Do not update PM3 firmware as part of this plan.

### Product scope

- The temporary connection screen may replace the app root during early slices, but preserve Concept A source and tests for later integration.
- Keep `FakeProxmark` useful throughout.
- New hardware/BLE implementation is a future follow-up, not a slice in this plan.
- Android, direct iPad USB, macOS Swift UI, public-internet access, and raw PM3 passthrough are non-goals.

---

# Slice 0 — Preserve evidence and establish a clean baseline

## Goal

Start implementation from a reproducible, reviewed baseline while retaining the successful hotspot proof and excluding obsolete accessory-probe work.

## Todos

- [x] Preserve `iPadHotspotProbe/` in source control with its physical test evidence and reproduction instructions.
- [x] Update hotspot-probe wording so it is clearly committed evidence rather than disposable untracked work.
- [x] Remove the untracked `iPadAccessoryProbe/`; do not commit its generated signing/build logs or recreate it.
- [x] At execution start, confirm branch/head and record the current working tree in these notes.
- [x] Read completely before implementation:
  - this plan;
  - `iPadHotspotProbe/README.md` and source;
  - `.plans/pm3-native-integration.md`;
  - `.plans/pm3-native-vs-process-write-reset-investigation.md`;
  - `Pm3UsbApi/Pm3.cs`, options/session/native transport relevant to read/write;
  - `RidesCli/IRidesPm3Api.cs`, `RideBlockResolver.cs`, reset/write safety code;
  - `Tokens/RideCounterCodec.cs`, `EncodingSequence.cs`, `TokenIdentityProfile.cs`;
  - `RidesTablet/Domain`, `Features/RidesViewModel.swift`, current Concept A view, and tests.
- [x] Run and record baseline deterministic tests without hardware integration:

  ```bash
  dotnet test ElevatorTokens.sln --filter 'Category!=Integration&Category!=IntegrationParity'
  xcodebuild test \
    -project RidesTablet/RidesTablet.xcodeproj \
    -scheme RidesTablet \
    -destination 'platform=iOS Simulator,name=RidesTablet iPad Air 4'
  ```

- [x] Confirm the physical iPad still appears through `xcrun devicectl list devices` before the first physical slice, without launching a simulator during physical testing.
- [x] Confirm PM3 port ownership is free and perform a read-only native connect before Slice 1's hardware checkpoint.

## Acceptance

- Hotspot evidence is tracked and reproducible.
- Accessory probe is absent.
- Existing deterministic suites have a recorded baseline.
- Unrelated untracked files remain untouched.

## Agent notes / assumptions

- Notes: Plan-creation baseline was `1aac5b9` on branch `ipad-rides`/`master`. Only the known unrelated `debug/` paths and the two probe directories were untracked. `iPadAccessoryProbe/` was removed; `iPadHotspotProbe/` was selected for commit.
- Execution baseline (2026-09-16): branch `ipad-rides`, HEAD `9132691873e0ffb4f2c059b6d51b2c9b65ba838c`; `master` remained `1aac5b937254f910c1c24d604d2d4cbb12fd8988`. The working tree contained only the five documented unrelated untracked `debug/` paths, which remained untouched.
- Deterministic baseline: `dotnet test ElevatorTokens.sln --filter 'Category!=Integration&Category!=IntegrationParity'` exited 0 across six test projects (409 passed, 1 skipped, 0 failed). The documented `xcodebuild test` command exited 0 on the `RidesTablet iPad Air 4` simulator (19 passed, 0 failed; 11 build warnings plus the existing UIKit orientation advisory).
- Hardware-readiness baseline: `xcrun devicectl list devices` showed the physical `itgeorge iPad Air 4th Gen` as `available (paired)`, model `iPad Air (4th generation) (iPad13,2)`. Current CoreDevice identifier is `494BBD09-0DEB-5B41-B915-3B4258F1DBA2`; the older hardware UDID recorded by the hotspot probe is not displayed by the current list format. No simulator was launched for this check.
- PM3 readiness: native port `/dev/cu.usbmodem11301` had no `lsof`/`fuser` owner and no competing PM3 process. A connect/version-only `Pm3Cli` run exited 0, logged `hw version` OK in 106 ms, then disconnected. Session log: `/var/folders/ty/cs9d984d4s926vpqm0df5_sc0000gn/T/elevator/pm3-50230-20260916131417-session.log`. No tune, detect, card read, dump, or write was issued.
- Assumptions: The available paired CoreDevice entry is the same physical iPad Air 4 used by the hotspot probe; CoreDevice's displayed identifier is allowed to differ from the hardware UDID.

---

# Slice 1 — Walking skeleton: manual pairing and hardcoded block-5 read

## Goal

Prove the production-shaped chain with the smallest useful operation:

```text
iPad temporary screen -> authenticated local HTTP -> .NET bridge -> Pm3UsbApi -> read block 5 -> iPad
```

Do not add ride decoding, mirror logic, generic write APIs, QR scanning, or Concept A integration yet.

## Proposed shape

Create new .NET projects and add them to `ElevatorTokens.sln`:

```text
RidesBridge/
RidesBridge.Tests/
```

Likely bridge components; names may evolve if the implementation discovers a clearer split:

```text
BridgeOptions
PairingCodeService
PairedClientStore
BearerAuthenticationHandler (or narrow endpoint filter/middleware)
IBridgePm3Device
Pm3BridgeDeviceAdapter
Pm3OperationGate
ReadBlockResponse
```

Likely Swift components:

```text
RidesTablet/Bridge/BridgeClient.swift
RidesTablet/Bridge/BridgeModels.swift
RidesTablet/Bridge/BridgeCredentialStore.swift
RidesTablet/Bridge/BridgeConnectionModel.swift
RidesTablet/Views/BridgeConnectionView.swift
```

Use a temporary root screen from `RidesTabletApp`; do not delete or redesign Concept A.

## TDD todos — bridge

- [x] Add `RidesBridge.Tests` first with a fake `IBridgePm3Device`; no unit test may require USB hardware.
- [x] Characterize configuration validation:
  - valid private/local bind URL and port;
  - malformed/unsupported URL rejected clearly;
  - startup lists usable non-loopback IPv4 URLs without treating internet reachability as required.
- [x] Test pairing-code behavior before endpoint implementation:
  - six numeric digits;
  - expiration;
  - single successful use;
  - wrong/expired/reused code rejected;
  - concurrent attempts cannot redeem one code twice;
  - secrets never appear in structured request logs.
- [x] Test bearer issuance/storage/authentication:
  - generated from a cryptographically secure source;
  - only a one-way verifier/hash is persisted if practical;
  - valid token authorizes hardware endpoint;
  - missing/invalid/revoked token returns 401;
  - bridge restart persistence behavior is explicit and tested.
- [x] Test the operation gate so simultaneous hardware requests execute one at a time and cancellation while waiting does not enter the PM3 call.
- [x] Test `GET /api/v1/health` (or equivalent) returns bridge/API version without hardware access or secrets.
- [x] Test pairing endpoint contract and the authenticated hardcoded block-5 read endpoint using ASP.NET's in-memory test server.
- [x] Implement the minimal ASP.NET Core service to pass the tests.
- [x] Reference/reuse `Pm3UsbApi` directly in the production adapter; do not shell out to `Pm3Cli` or expose raw commands.
- [x] Make PM3 port/configuration explicit through bridge settings/environment while retaining existing safe auto-discovery where practical.
- [x] Add graceful startup/shutdown and clear states for PM3 unavailable, no chip, timeout, bridge busy, and malformed response.

## TDD todos — iPad

- [x] Add request/response contract tests before networking implementation:
  - eight-character uppercase block hex;
  - version/error decoding;
  - malformed response rejection.
- [x] Add `BridgeClient` tests with injected `URLSession`/`URLProtocol`:
  - URL normalization and direct-IP base URL;
  - pairing request encoding;
  - bearer header injection after pairing;
  - 401 clears/requires re-pairing without leaking the token;
  - timeout, unreachable host, invalid JSON, and server error mapping;
  - no retries for hardware requests.
- [x] Put Keychain calls behind `BridgeCredentialStore`; test connection-state logic with an in-memory store.
- [x] Add the local-network usage description and narrowly scoped ATS local-network allowance proven by `iPadHotspotProbe`; do not add a global arbitrary-load exemption.
- [x] Implement the temporary SwiftUI connection screen:
  - bridge URL;
  - six-digit PIN;
  - Pair/Forget controls;
  - connection/authentication state;
  - one `Read block 5` button;
  - returned hex or actionable error.
- [x] Temporarily route `RidesTabletApp` to the diagnostic screen while leaving Concept A intact for Slice 5.
- [x] Keep simulator/unit tests independent of the physical Mac and PM3.

## Physical acceptance

- [x] Run the bridge on the Mac with PM3 and black card present; record bind URL, API version, and PM3 port without recording secrets.
- [x] Pair the physical iPad through manual direct-IP URL and PIN.
- [x] Tap `Read block 5` once and compare the returned value with a direct read-only PM3 read.
- [x] Repeat the end-to-end read over the iPhone 13 Pro hotspot; no internet-dependent call may be required.
- [x] Verify bridge restart/reconnect behavior matches the documented token-persistence choice.
- [x] Verify no blocks were written and no full dump occurred.
- [x] Record exact test commands/results and update this plan in the slice commit.

## Acceptance

- One physical button press produces the actual block-5 value through the complete authenticated chain.
- Direct-IP hotspot use works.
- Unit/integration tests cover pairing, authorization, serialization, contracts, and client error mapping without hardware.
- There is no ride logic or generic mutation endpoint yet.

## Agent notes / assumptions

- Notes: The first Slice 1 backend checkpoint added `RidesBridge` and `RidesBridge.Tests` to the solution. The API is `v1`: unauthenticated `GET /api/v1/health`, `POST /api/v1/pair`, authenticated `POST /api/v1/pair/revoke`, and authenticated hardcoded `GET /api/v1/hardware/page0/block5`. There is no generic block or raw-command endpoint.
- Pairing/security: startup issues a CSPRNG six-digit PIN to the terminal with a two-minute lifetime; it is single-use, invalidates after 10 wrong attempts, and concurrent redemption is serialized. Pairing returns a 256-bit URL-safe bearer token. Only its SHA-256 verifier and revocation state persist in the configured JSON store, so credentials survive restart without plaintext bearer storage. Structured HTTP logs contain method/path/status only. Cleartext bearer transport remains limited to the plan's trusted local-network prototype scope.
- Binding/configuration: the safe default is loopback and reports no fake LAN URL. An explicit private IPv4 bind reports only that URL; wildcard bind reports active private non-loopback IPv4 URLs. PM3 port and auto-discovery are configurable through `Bridge`/`Pm3` settings or documented environment keys in `BridgeOptions`.
- Lifecycle: `BridgeOperationGate` serializes requests and drains before idempotent adapter disposal. The production adapter directly owns `Pm3UsbApi.Pm3`, uses the native executor, and exposes only block 5. Expected PM3/serial failures map to stable secret-free HTTP error contracts.
- Tests (2026-09-16): `dotnet test RidesBridge.Tests/RidesBridge.Tests.csproj --no-restore` passed 41/41. The full non-integration solution run passed 450 with 1 skipped and 0 failed. A warnings-as-errors solution build completed with 0 warnings/0 errors; `git diff --check` passed. No USB/card operation occurred.
- iPad client (2026-09-16): added strict v1 DTO decoding, local direct-IP URL validation, an injected `URLSession` client with bearer authorization, explicit 30-second/no-cache/no-retry requests, cancellation/error mapping, and synchronized credential access. Keychain persistence is behind `BridgeCredentialStore`; save failures revoke the newly issued bearer or retain it only for an explicit Forget retry. Forget calls the authenticated server revocation endpoint and keeps credentials when revocation fails so a live bearer is not silently orphaned.
- Temporary iPad UI (2026-09-16): app startup now routes to a diagnostic URL/PIN Pair/Forget/read-block-5 screen while the Concept A `ContentView` remains intact. The app plist contains `NSLocalNetworkUsageDescription` and only `NSAllowsLocalNetworking`; there is no global arbitrary-load exemption. The URL field starts blank and directs the operator to the private URL printed by the Mac bridge.
- iPad tests (2026-09-16): the focused bridge suite passed 21/21 and an independent full `xcodebuild test -project RidesTablet/RidesTablet.xcodeproj -scheme RidesTablet -destination 'platform=iOS Simulator,name=RidesTablet iPad Air 4'` passed 40/40, preserving the original 19 tests. The independent full non-integration .NET run again passed 450 with 1 skipped. `plutil -lint`, Xcode project listing, and `git diff --check` passed; only AppIntents metadata notices were emitted. No physical device, USB, PM3, or card operation occurred.
- Physical acceptance (2026-09-16): on the iPhone 13 Pro hotspot, the Mac bridge bound explicitly to `http://172.20.10.4:5080`, reported API `v1` / bridge `1.0.0`, and used `/dev/cu.usbmodem11301`. The physical iPad Air 4 paired by direct URL and a terminal-only PIN. The first iOS local-network permission transition required a second Pair tap; the client now accepts concise `IP[:port]` input and performs a retryable unauthenticated health preflight before consuming the one-time PIN.
- Physical defect and repair (2026-09-16): the first authenticated read succeeded, but the original adapter failed to release its private operation semaphore afterward. A second token read therefore showed no PM3 activity, timed out, and blocked graceful shutdown while retaining the serial port. A forced read-only-process cleanup was required; a fresh direct native detect plus block-5-only read of the approved black card returned `3FC6B8C3`. The repair releases the adapter lock on every path, invalidates cross-request detect state, gives queued requests a five-second bound, gives hardware execution a server-owned 20-second cancellation bound, and discards a cancelled/timed-out PM3 session before reconnecting.
- Physical retest (2026-09-16): the repaired app was installed in place and the repaired bridge restarted with the same one-way-verifier store. The iPad restored its Keychain credential without URL/PIN re-entry. The operator successfully read the approved black card, swapped in another token and read it, then restored and successfully read the black card again. All traffic remained on the iPhone hotspot; no internet call was needed. Final bridge shutdown completed gracefully and released both TCP port 5080 and `/dev/cu.usbmodem11301`.
- Physical safety: the bridge and direct comparison issued only detect plus explicit page-0 block-5 reads. No full dump, tune, or write occurred; blocks 0/7 were never written, and no explicit block read other than block 5 was requested.
- Repair verification (2026-09-16): `dotnet test ElevatorTokens.sln --filter 'Category!=Integration&Category!=IntegrationParity' --no-restore` passed 471 with 1 skipped and 0 failed; `dotnet build ElevatorTokens.sln --no-restore -warnaserror` completed with 0 warnings/errors. `xcodebuild test -project RidesTablet/RidesTablet.xcodeproj -scheme RidesTablet -destination 'platform=iOS Simulator,name=RidesTablet iPad Air 4'` passed 49/49. Bridge-focused tests passed 62/62 and Swift bridge-focused tests passed 30/30. `git diff --check` passed.
- Assumptions: Manual URL + PIN is intentionally first. Bonjour/QR convenience must not block this slice. Physical runs will explicitly bind the current hotspot/private Mac address (or intentionally choose wildcard); loopback remains the safe development default.

---

# Slice 1B — Connection convenience: QR onboarding, Bonjour, and reconnect

## Goal

Remove routine typing only after the direct-IP/authentication skeleton is proven. Keep manual URL entry as the diagnostic fallback.

## Todos

- [x] Define and test a versioned pairing payload, e.g. URL plus one-time pairing code/nonce; never encode a long-lived bearer token in reusable QR output.
- [x] Add bridge-side QR generation/display suitable for a terminal or small local status page; choose the smallest dependency with deterministic tests.
- [x] Add iPad QR scan/import with explicit camera permission text and parser validation:
  - correct scheme/version;
  - local/private URL policy;
  - expired/reused pairing data;
  - malformed/untrusted payload rejection.
- [ ] Advertise a purpose-specific Bonjour service such as `_elevator-rides._tcp` and include only non-secret TXT metadata such as bridge ID/API version.
- [ ] Browse/resolve with `Network.framework`; if exactly one compatible bridge is found, offer it without hiding the manual direct-IP path.
- [ ] Test reconnect using stored credentials when IP changes but Bonjour bridge identity remains the same.
- [ ] Ensure duplicate bridge names/multiple bridges require explicit operator selection rather than choosing nondeterministically.
- [ ] Add deterministic parser/discovery state tests with fakes; do not require multicast in the unit suite.
- [x] Physically validate QR onboarding and an authenticated read on normal Wi-Fi.
- [ ] Physically validate Bonjour on normal Wi-Fi and the iPhone hotspot, using `iPadHotspotProbe` only as reference evidence.
- [ ] Confirm operation still works when Bonjour is unavailable but direct IP is entered manually.

## Acceptance

- Normal first-time setup is scan/confirm rather than manually typing a URL.
- Subsequent launches reconnect automatically when safe.
- Direct IP remains functional on multicast-restricted networks.

## Agent notes / assumptions

- Notes: Deferred on 2026-09-16 at the user's direction to proceed with Slice 2 functional Mercury work first. Manual direct-IP pairing remains the supported diagnostic path; no Slice 1B convenience work is mixed into Slice 2.
- QR backend checkpoint (2026-09-16): added a strict `ridesbridge-pairing` / `v1` JSON payload containing only a private IPv4 bridge URL, the current short-lived one-time PIN and expiration, stable non-secret 128-bit bridge ID, and API version. Unknown/duplicate fields, hostile URLs, missing/wrong type or version, and expired payloads are rejected. Reuse remains authoritatively rejected by the existing server-owned one-time PIN endpoint; no bearer/verifier is encoded. Bridge identity creation uses a crash-released cross-process lock, flushed same-directory temporary file, and atomic no-overwrite publication; malformed final identities fail closed.
- Terminal QR checkpoint: QRCoder 1.6.0 emits the payload at ECC-M. The renderer uses explicit ANSI black/white foreground/background plus Unicode half-block packing, retaining QRCoder's four-module quiet zone while fitting the full production payload in 65 visible columns by 33 terminal rows. Deterministic tests reconstruct the logical matrix from terminal output, exercise concurrent identity creation and replay, and preserve the manual PIN fallback. Bridge tests passed 104/104; the full non-integration .NET suite passed 522 with 1 skipped; warnings-as-errors and `git diff --check` passed.
- iPad QR checkpoint (2026-09-17): added a strict duplicate-key-aware parser for the exact public payload, canonical private IPv4 URL validation, injected expiration clock, and backward-compatible optional `bridgeId` persistence in the Keychain credential. QR pairing performs one health preflight and one PIN attempt with no permission-transition retry; the initial credential save transactionally includes the bridge ID and retains the existing revoke-on-save-failure behavior. Manual pairing remains unchanged. A VisionKit QR-only scanner has injectable availability/authorization seams, one-shot stop-before-import delivery, actionable permission/unavailable/runtime states, and lifecycle cancellation for interactive dismissal, permission-prompt dismissal, representable teardown, and coordinator reuse. The plist now contains a narrowly worded camera usage description; manual URL/PIN remains available. Focused scanner/core tests passed 43/43 before lifecycle hardening and scanner lifecycle tests passed 10/10 afterward; the final full Swift suite passed 107/107, Release simulator build, plist/project checks, and `git diff --check` passed.
- Physical QR repair (2026-09-17): the first real camera pass exposed two deterministic parity gaps before any bridge request. Terminal's ANSI half-block rendering distorted the dense production matrix, and `System.Text.Json` trims trailing fractional-second zeroes while the Swift parser required exactly seven digits. The bridge now emits a temporary lossless QRCoder PNG (16 pixels/module; 1040×1040 in this run) in its private data directory using an atomic owner-only `0600` file, a secret-free predictable filename, and PIN-expiry/shutdown cleanup; manual PIN remains available. Swift now accepts the strict `System.Text.Json` ISO-8601 extended profile with zero through seven fractional digits and rejects malformed dates and offsets. Pairing-focused backend tests passed 18/18, bridge tests 108/108, the full non-integration .NET suite passed 527 with 1 skipped, and warnings-as-errors/diff checks passed. Swift pairing-focused tests passed 9/9 and the full simulator suite passed 108/108; a freshly provisioned physical build installed and launched successfully.
- Physical QR acceptance (2026-09-17): VisionKit automatically locked onto the lossless QR without a tap, dismissed once, paired successfully, and the operator immediately completed an authenticated block-5 read with HTTP 200. Pairing returned exactly one HTTP 200; no failed or replayed pairing request appeared. PM3 evidence `/var/folders/ty/cs9d984d4s926vpqm0df5_sc0000gn/T/elevator/pm3-84181-20260916235624-session.log` contains only connect/version, fresh T55 detection, block-5 read, and disconnect—no write or dump. Clean shutdown removed the PNG and released TCP 5080 and `/dev/cu.usbmodem1301`.
- Assumptions: Continue with Bonjour/reconnect. Direct IP remains available throughout.

---

# Slice 2 — Mercury-only ride read/set with full C#/Swift parity

## Goal

Deliver the first real ride workflow end to end using Mercury only. It is acceptable—and preferred—to hardcode Mercury behavior for this slice. Do not introduce a generalized Swift family registry merely to anticipate Slice 3.

The existing preliminary `RideSequence` multi-family implementation may remain for the fake prototype, but the new network workflow must not claim parity through it until Slice 3 validates/generalizes it.

## Shared characterization fixture

Create a checked-in language-neutral fixture under a stable location such as:

```text
TestFixtures/RideEncoding/mercury-v1.json
```

The C# oracle and Swift tests must both validate the same fixture. Include:

- every Mercury encoding for rides `0...500`;
- boundary vectors `0/1/7/8/127/128/255/256/383/384/500`;
- structurally malformed blocks;
- encoded `501...511` rejected by the application range;
- mirror cases: matching, only block 5 valid, only block 6 valid, both valid/different, neither valid;
- expected source block and warning/conflict metadata where relevant.

Do not hand-maintain two independent fixture copies.

## TDD todos — Mercury codec/resolver

- [x] Add/extend C# tests that export or validate the immutable shared Mercury fixture from `Tokens`/`RideBlockResolver` behavior.
- [x] Add Swift fixture-loading tests before implementing the Mercury network workflow.
- [x] Add a narrow Mercury-only Swift codec/resolver with no family registry abstraction. Suggested temporary shape:

  ```swift
  enum MercuryRideCodec {
      static func encode(_ rides: UInt) -> UInt32?
      static func decode(_ block: UInt32) -> UInt?
  }

  enum MercuryMirrorResolver {
      static func resolve(block5: UInt32, block6: UInt32) -> MercuryRideRead
  }
  ```

- [x] Prove exact Swift/C# encode parity for all `0...500` fixture entries.
- [x] Prove exact structural rejection and `>500` rejection.
- [x] Match C# mirror semantics exactly:
  - matching valid mirrors use block 5 as source;
  - if both differ and are valid, block 6 wins;
  - if only one is valid, use it;
  - neither valid is unknown/failure;
  - preserve useful mismatch metadata for the UI/logs.
- [x] Avoid silently routing Mercury tests through the existing generalized Swift enum; the slice should be reviewably Mercury-specific.

## TDD todos — bridge ride endpoints and conditional writer

- [x] Add fake-device tests for a Mercury mirror-read endpoint that reads only blocks 5 and 6 and returns raw values; the iPad performs decode.
- [x] Define a versioned conditional mutation request/response contract with statuses such as:
  - `written`;
  - `alreadyApplied`;
  - `conflict` with actual block values;
  - `verifyFailed`;
  - `rollbackSucceeded` / `rollbackIncomplete`.
- [x] Test request validation:
  - only distinct blocks 1...6 may appear;
  - blocks 0/7/out-of-range rejected before hardware access;
  - hex must be exact 32-bit values;
  - duplicate mutations rejected;
  - Slice 2 ride endpoint permits only 5 and 6.
- [x] Test preflight-all-before-write behavior for every expected/desired/conflict combination.
- [x] Test desired-state retry semantics, including one mirror already desired after a partial prior operation.
- [x] Test write order, immediate read-back verification, stop-on-failure, and best-effort rollback to `expected` only for blocks changed by this operation.
- [x] Test client cancellation/disconnect does not abandon in-progress verification/rollback.
- [x] Reuse existing `Pm3` block methods and proven delays/retries rather than duplicating native protocol logic.
- [x] Do not use full dumps or read blocks other than 5/6 in this slice.

## TDD todos — temporary iPad Mercury screen

- [x] Extend the diagnostic screen with `Read Mercury rides` while keeping transport state explicit.
- [x] Show raw block 5/6 values, resolved rides, and mismatch/source information.
- [x] Add a bounded target-rides input `0...500` and `Set Mercury rides` action.
- [x] Build conditional mutations using the last-read raw 5/6 as `expected` and Mercury encoding as `desired`.
- [x] On conflict or ambiguous network timeout, do not automatically replay; force a fresh mirror read and explain the state.
- [x] Test model behavior with fake bridge responses for success, already applied, conflict, verify failure, rollback outcomes, no chip, and network loss.

## Physical acceptance — approved black card

- [x] Read-only preflight blocks 5/6 and record exact raw values.
- [x] Choose a valid Mercury test value different from the current values; document why it is safe.
- [x] Execute the write from the physical iPad through the bridge.
- [x] Confirm bridge preflight read only 5/6, wrote only 5/6, and verified both.
- [x] Repeat the same desired request and confirm `alreadyApplied` without a rewrite.
- [x] Exercise one safe stale-expected conflict without writing.
- [x] Restore the original raw block 5/6 values and verify them.
- [x] Run no full dump and write no other block.
- [x] Record PM3 diagnostic log locations and final card state without committing generated logs.

## Acceptance

- Mercury read/set works end to end on the physical iPad/Mac/PM3 chain.
- Swift matches the C# oracle for every ride count and resolver edge case in scope.
- Conditional writes are deterministic, target-only, retry-safe, and covered without hardware.
- Original test-card mirror values are restored.

## Agent notes / assumptions

- Notes (software checkpoint, 2026-09-16): added the single checked-in `TestFixtures/RideEncoding/mercury-v1.json` fixture with all 501 application-valid encodings, 501...511 application-range rejections, structural failures, boundaries, and mirror-resolution cases. Independent C# and Swift tests consume this one fixture. The new Swift `MercuryRideCodec`/`MercuryMirrorResolver` is deliberately Mercury-only and does not route through the preliminary generalized registry.
- Bridge contract: authenticated `GET /api/v1/hardware/mercury/mirrors` returns only uppercase raw blocks 5/6. Authenticated `POST /api/v1/hardware/mercury/mutations` accepts a `v1` list restricted to distinct blocks 5/6 and returns `written`, `alreadyApplied`, `conflict`, or `verifyFailed` with bounded rollback details. Validation precedes hardware access; preflight reads every target; writes are block-ordered and immediately verified; rollback is reverse-ordered and includes uncertain writes whose response may have been lost.
- Safety hardening: client disconnect is detached only after the hardware gate is acquired; mutation recovery has an independent four-second server-owned bound. Defaults total 29 seconds (5-second queue + 20-second execution + 4-second recovery), strictly below the iPad's 30-second request deadline. Transport-fatal failures discard the dirty PM3 session. Native BigBuf cancellation is rethrown rather than converted into a retry/failure. Unknown/non-Mercury mirrors remain visible diagnostically but can never authorize a write. The iPad never retries a hardware read or mutation and invalidates stale write snapshots after ambiguous/failure outcomes.
- Address migration (2026-09-16): the physical iPad retained its existing one-way-verifier-backed bearer while moving from the proven iPhone hotspot to home Wi-Fi. A launch-only `RIDES_BRIDGE_ADDRESS_OVERRIDE=192.168.0.163:5080` caused one authenticated, no-hardware `GET /api/v1/pair/status` (HTTP 200), then transactionally updated the Keychain URL. No new PIN, pairing token, revocation, or PM3 operation was required. Manual `Use entered address` remains the fallback.
- Physical acceptance (2026-09-16): the bridge bound explicitly to `http://192.168.0.163:5080` on the shared home router and used `/dev/cu.usbmodem1301`. A DEBUG-only, exact seven-request iPad acceptance runner first recorded `block5=3FC6B8C3`, `block6=3FC6B8C3` (Mercury 497), then selected adjacent in-range Mercury 498 (`3FC6BBF3`). The authenticated iPad request wrote and immediately verified only blocks 5/6. Repeating the same desired request returned `alreadyApplied`; a stale request for a distinct value returned `conflict`; neither caused a write. A fresh read confirmed 498 before an exact raw restore, and the final fresh read confirmed `3FC6B8C3` / `3FC6B8C3` byte-for-byte.
- Physical audit evidence: PM3 session log `/var/folders/ty/cs9d984d4s926vpqm0df5_sc0000gn/T/elevator/pm3-65644-20260916175351-session.log` records exactly four writes: block 5 then 6 to `3FC6BBF3`, followed by block 5 then 6 to `3FC6B8C3`. Its only explicit block operands are 5 and 6; it contains no dump or tune command. The app console emitted `RIDES_PHASE2_ACCEPTANCE_SUCCESS original5=3FC6B8C3 original6=3FC6B8C3 targetRides=498`. Generated logs remain outside the repository. Graceful shutdown released both TCP port 5080 and `/dev/cu.usbmodem1301`.
- Final deterministic validation (2026-09-16): full non-integration .NET suite passed 508 with 1 skipped and 0 failed; full Swift simulator suite passed 80/80; `dotnet build ElevatorTokens.sln --no-restore -warnaserror` completed with 0 warnings/errors. Bridge-focused tests passed 89/89, guarded physical-runner tests passed 4/4, fixture schema checks, plist lint, Xcode project listing, and `git diff --check` passed.
- Assumptions: Mercury hardcoding is deliberate. Generalization belongs in Slice 3.

---

# Slice 3 — Generalize to all registered ride encoding sequences

## Goal

Only after Mercury is proven, replace the Mercury-only Swift path with a registered structural model matching production C# for:

```text
mercury, venus, earth, pluto, mars,
jupiter, saturn, uranus, neptune
```

## TDD todos — shared fixtures and codec

- [ ] Expand the shared fixture format/version to include sequence metadata `(name, zeroBlock, rotation, min/max)` and all `0...500` encodings for all nine sequences.
- [ ] Make C# tests validate the shared fixture against `EncodingSequences`; fixture drift must fail visibly.
- [ ] Add selected hardware-observed boundary vectors and malformed blocks independent of generated round trips.
- [ ] Add Swift tests for every fixture entry before changing the network workflow.
- [ ] Generalize the Mercury codec into a small sequence/counter model matching `RideCounterCodec` and `EncodingSequence`:
  - exact rotation handling including 0 and 4;
  - nine-bit counter structure;
  - application range `0...500`;
  - exact structural round-trip validation.
- [ ] Add registry validation for duplicate friendly names and encoded collisions.
- [ ] Prove no self/cross collisions over `0...500`; retain `0...511` as diagnostic parity where useful.
- [ ] Match C# full-registry decoding and ambiguity behavior; do not guess families from visible high words.
- [ ] Replace the preliminary Swift multi-family implementation or refactor it into the proven model; remove temporary Mercury-only production code once equivalent tests pass.
- [ ] Preserve `FakeProxmark` samples and tests through the migration.

## TDD todos — network workflow

- [ ] Update ride reads to report the decoded sequence and preserve the sequence selected by the authoritative mirror block.
- [ ] Encode desired rides using that same sequence; never default a known non-Mercury token to Mercury.
- [ ] Keep bridge contracts sequence-agnostic and raw-block based. The Mac must not become the ride-family authority.
- [ ] Add endpoint/client tests showing identical bridge behavior regardless of sequence.
- [ ] Add regression cases across `7/8`, `127/128`, `255/256`, and `383/384` for both rotation layouts.
- [ ] Physically read the black card through the iPad flow and confirm its current registered sequence if one is present.
- [ ] Perform only a narrowly chosen target-only write if needed for end-to-end family preservation, restoring original 5/6 afterward.

## Acceptance

- All nine registered families have shared C#/Swift fixture parity and exhaustive Swift tests.
- Network ride reads/writes preserve the source sequence.
- No identity/reset concerns are mixed into the ride codec.

## Agent notes / assumptions

- Notes:
- Assumptions:

---

# Slice 4 — Targeted token details, explicit-profile reset, and unknown dumps

## Goal

Complete the non-UI operator behavior while preserving the low-read-pressure rule.

### Read strategy

- Known-flow scan: detect/tune as needed, then read block 4 and mirrors 5/6.
- Decode mirrors on iPad.
- If registered: stop; do not read a full page.
- If unknown: read only missing blocks `0,1,2,3,7` (block 4/5/6 are already available), assemble exactly eight blocks, and save the big-endian dump on iPad.

## TDD todos — targeted scan and unknown path

- [ ] Define/test raw targeted-read contracts without introducing a generic unaudited command endpoint.
- [ ] Test known scans touch only blocks 4/5/6 and return signal strength plus raw values.
- [ ] Test no-chip/tune/read failures remain distinguishable and do not trigger write or dump requests.
- [ ] Test unknown decode triggers reads of only the missing five blocks exactly once.
- [ ] Test page assembly preserves block order and writes exactly 32 bytes, big-endian, with the existing filename convention.
- [ ] Preserve `Unknown, logged` only after successful local iPad persistence; log failure remains an error.
- [ ] Test interruption during missing-block reads produces no falsely successful dump.

## TDD todos — identity/reset parity

- [ ] Add shared C#/Swift identity-profile fixture parity for all canonical and recognition-only profiles.
- [ ] Validate canonical reset images against the embedded C# big-endian images, including block 4 and Neptune block 7 metadata.
- [ ] Keep reset selection explicit and empty by default.
- [ ] Build reset mutations only for blocks that actually need change:
  - first read candidate target blocks 1...6;
  - if identity 1...4 already matches and mirrors belong to the selected sequence, target only 5/6;
  - otherwise target changed blocks in 1...6;
  - never include 0/7.
- [ ] Use the same conditional mutation primitive from Slice 2; do not add a blind overwrite endpoint.
- [ ] Test full preflight-before-write, desired-state retry, per-block verification, rollback, and incomplete rollback reporting across 1...6.
- [ ] Confirm server continues reset verification/rollback after client disconnect.
- [ ] Return verified current block values after success so the iPad state reflects hardware, not optimistic assumptions.
- [ ] Preserve block 4 as Apt # during normal ride writes; explicit profile reset may overwrite it by design.

## Temporary-screen acceptance

- [ ] Add block 4 display, signal value, explicit reset-profile picker, reset confirmation, and unknown-log result to the diagnostic workflow.
- [ ] Keep controls functional rather than visually polishing Concept A in this slice.
- [ ] Add view-model tests for explicit selection, cancellation, conflict, success, rollback warning, and unknown log save/failure.

## Physical acceptance — black card

- [ ] Record read-only blocks 1...6 before reset testing; do not read 0/7 unless the unknown-dump test specifically requires the complete image.
- [ ] Exercise one explicit reset profile through the conditional path only after confirming restoration values are available.
- [ ] Verify only expected target blocks changed and the app reports verified hardware state.
- [ ] Restore the original 1...6 values when safe/required and verify each target block.
- [ ] Never write blocks 0 or 7.
- [ ] Keep the number of repeated reads/writes minimal and record any antenna instability.

## Acceptance

- Known scans avoid full dumps.
- Unknown scans produce the required exact dump with no redundant reads.
- Reset is explicit, conditional, verified, rollback-capable, and block-safe.
- C#/Swift profile/reset parity is test-backed.

## Agent notes / assumptions

- Notes:
- Assumptions:

---

# Slice 5 — Integrate the proven connector into Concept A

## Goal

Replace the temporary diagnostic root with the existing preferred Concept A operator workflow without moving transport logic into SwiftUI views.

## Architecture direction

Generalize the hardware boundary only as far as the proven workflows require. A likely shape is:

```swift
protocol RideTokenDevice: Sendable {
    func scan() async -> ScanOutcome
    func writeRideMirrors(_ request: RideMirrorWriteRequest) async -> WriteOutcome
    func reset(_ request: ResetMutationRequest) async -> ResetOutcome
}
```

Adapters:

```text
FakeRideTokenDevice
NetworkRideTokenDevice -> BridgeClient
```

Names and exact method grouping may evolve, but the domain/view model must not depend on HTTP DTOs or PM3-specific transport details. Update this plan if implementation evidence supports a different boundary.

## TDD todos

- [ ] Add characterization tests for current Concept A behavior before changing the device boundary.
- [ ] Refactor/rename `ProxmarkDevice` to a hardware-neutral boundary without coupling views to URLSession, HTTP status, bearer tokens, or PM3 command names.
- [ ] Adapt `FakeProxmark` (renaming only if valuable) and keep all fake scenarios deterministic.
- [ ] Implement `NetworkRideTokenDevice` as a translation layer from bridge raw results into domain outcomes.
- [ ] Keep connection/pairing state separate from token state:
  - bridge unconfigured;
  - discovering/connecting;
  - pairing required;
  - connected/ready;
  - Mac reachable but PM3 unavailable;
  - connection lost while idle;
  - connection lost during read;
  - ambiguous/disconnected write requiring refresh.
- [ ] Integrate connection setup into Concept A with large/simple operator behavior; diagnostics may remain behind a small developer/settings affordance.
- [ ] Route Detect through the targeted scan path and preserve signal display, block 4 Apt #, current/pending rides, EUR, and unknown logging.
- [ ] Route Charge through conditional mirror mutations and only update current rides from verified server results.
- [ ] Route Reset through explicit-profile conditional mutations; confirmation remains disabled until selection.
- [ ] Preserve exact user-facing requirements already tested in `RidesViewModel`.
- [ ] Remove the temporary root/diagnostic-only workflow once Concept A covers the proven operations; retain useful connection diagnostics without duplicate business logic.
- [ ] Update `RidesTablet/README.md` and screenshots only after physical Concept A review.

## Validation

- [ ] Run full Swift XCTest suite in simulator with fakes and network stubs.
- [ ] Run full non-integration .NET suite including `RidesBridge.Tests`.
- [ ] Run physical iPad smoke matrix over the iPhone hotspot:
  - first pairing/manual or QR;
  - reconnect;
  - known read;
  - ride adjustment/write and verified reread;
  - stale expected conflict;
  - no chip;
  - unknown dump;
  - explicit reset/cancel;
  - bridge/PM3 unavailable;
  - network loss before a write;
  - network loss after server accepted a write, followed by required refresh.
- [ ] Confirm known read/write paths did not issue a full dump.
- [ ] Confirm the black card's final state and record it.
- [ ] Capture final Concept A screenshots on the physical iPad or agreed iPad Air 4 simulator.

## Acceptance

- Concept A is the normal app workflow using the Mac bridge.
- Fake mode remains available for deterministic UI work.
- Transport can later be replaced without rewriting operator/domain logic.
- No Concept B code or documentation is restored.

## Agent notes / assumptions

- Notes:
- Assumptions:

---

# Final validation and handoff

## Todos

- [ ] Run all focused and complete deterministic suites; record counts and commands:

  ```bash
  dotnet test ElevatorTokens.sln --filter 'Category!=Integration&Category!=IntegrationParity'
  xcodebuild test \
    -project RidesTablet/RidesTablet.xcodeproj \
    -scheme RidesTablet \
    -destination 'platform=iOS Simulator,name=RidesTablet iPad Air 4'
  ```

- [ ] Run `git diff --check` and inspect `git status --short --branch`.
- [ ] Confirm no unrelated debug/probe/build outputs are staged.
- [ ] Confirm committed hotspot evidence still reproduces and no production code depends on its hardcoded IP.
- [ ] Confirm secrets, pairing codes, bearer tokens, PM3 logs, provisioning artifacts, and unknown token dumps are not committed.
- [ ] Confirm all HTTP hardware endpoints require authorization and expose no raw PM3 passthrough.
- [ ] Confirm all write paths reject blocks 0/7 before hardware access.
- [ ] Confirm normal known-token paths do not perform page-0 dumps.
- [ ] Summarize final API versions, Swift device boundary, fixture strategy, test results, physical evidence, and remaining risks in this plan.
- [ ] Commit the final plan update with the final implementation/test/documentation chunk.

## Agent notes / assumptions

- Notes:
- Assumptions:

---

# Follow-up work explicitly outside this plan

- [ ] Design and implement the future BLE custom reader hardware and `BluetoothRideTokenDevice` connector.
- [ ] Decide whether any Mac bridge path should remain as a supported backup after BLE hardware exists.
- [ ] Add public-internet/overlay networking only if a real operational need appears; do not expose this HTTP bridge directly to the internet.
- [ ] Evaluate Android direct-USB PM3 support separately if desired.
- [ ] Consider packaging the bridge as a signed menu-bar app or launch agent only after the command-line bridge proves reliable.
