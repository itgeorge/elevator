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
- [x] Advertise a purpose-specific Bonjour service such as `_elevator-rides._tcp` and include only non-secret TXT metadata such as bridge ID/API version.
- [x] Browse/resolve with `Network.framework`; if exactly one compatible bridge is found, offer it without hiding the manual direct-IP path.
- [x] Test reconnect using stored credentials when IP changes, with Bonjour used only to locate candidates and a token-bound relocation proof required before sending the saved bearer.
- [x] Ensure duplicate bridge names/multiple bridges require explicit operator selection rather than choosing nondeterministically.
- [x] Add deterministic parser/discovery state tests with fakes; do not require multicast in the unit suite.
- [x] Physically validate QR onboarding and an authenticated read on normal Wi-Fi.
- [x] Physically validate Bonjour on normal Wi-Fi and the iPhone hotspot, using `iPadHotspotProbe` only as reference evidence.
- [x] Confirm operation still works when Bonjour is unavailable or ambiguous but direct IP is entered manually.
- [x] Add a strict one-command `--everyday` launch mode with wildcard Bonjour binding, bounded free-port selection, durable state, and PM3 USB auto-discovery.
- [x] Make operator Forget an immediate local reset that stops discovery/reconnect and permits QR re-pairing without contacting the old bridge.
- [x] Keep the Connection state truthful when an automatically verified Bonjour bridge disappears and discovery resumes.

## Acceptance

- Normal first-time setup is scan/confirm rather than manually typing a URL.
- Subsequent launches reconnect automatically when safe.
- Direct IP remains functional on multicast-restricted networks.

## Agent notes / assumptions

- Notes: Bonjour advertisement was implemented as a bridge-side-only slice on 2026-09-17. `Haukcode.Mdns` 1.0.18 is a small maintained pure-managed C# RFC 6762/6763 implementation with a net8 asset usable by net9 and no external dependencies; it is isolated behind `IBonjourPublisher` so the multicast implementation is replaceable and tests remain multicast-free. The bridge starts the publisher after options and the persistent identity are resolved, and deterministically stops/disposes it on shutdown or startup failure. Bonjour startup failure is non-fatal after cleanup, preserving direct-IP HTTP operation; device startup failure remains fatal. Cleanup deliberately uses the package's synchronous `MdnsAdvertiser.Dispose()` off-thread: source inspection shows the 1.0.18 async disposer can time out before releasing sockets because its disposed flag suppresses the timer's second-goodbye state. No shell command or `dns-sd` process is used.
- Bonjour contract: service type is exactly `_elevator-rides._tcp`. Each service instance has a stable ASCII name `elevator-rides-<persistent-bridge-id>-<url-sha256-prefix>`, with the URL suffix making wildcard multi-address advertisements distinct. TXT is exactly `type=elevator-rides`, `bridgeId=<uppercase 128-bit persistent ID>`, `apiVersion=v1`, and `url=http://<private-ipv4>:<explicit-port>/`; no PIN, bearer, verifier, paths, or other configuration is included. Exact private binds advertise exactly one URL; wildcard binds advertise one deterministic service per distinct sorted active multicast-capable private IPv4; loopback/localhost binds advertise none. The TXT URL is included for future Network.framework-to-URLSession relocation because an NWBrowser endpoint is not itself a URLSession address. Important package limitation: `MdnsAdvertiser(address)` restricts the A record to that address, not multicast transmit links; these are address-specific instances, not interface-scoped advertisements. A future interface-aware publisher is required if link isolation is a product requirement.
- Bonjour deterministic tests (2026-09-17): descriptor/TXT allowlist and secret exclusion, canonical URL/address filtering, exact/wildcard/loopback bind semantics, stable/unique names, lifecycle idempotency, direct-IP fallback on publisher failure, device-startup failure propagation, cleanup use fake publishers only, and strict rejection of malformed configured values. Added regressions prevent stop-before-start and silent fallback from malformed timeout/boolean configuration. Focused bridge tests passed 124/124; full nonintegration .NET passed 543 with 1 skipped and 0 failed; solution warnings-as-errors build passed with 0 warnings/errors; `git diff --check` passed. No multicast, hardware, Swift, or physical Bonjour validation was performed.
- iPad Bonjour discovery checkpoint (2026-09-17): added a lifecycle-owned `NWBrowser` for `_elevator-rides._tcp` with an injectable multicast-free seam. Raw DNS-SD TXT parsing enforces the exact `type`/`bridgeId`/`apiVersion`/`url` allowlist and rejects duplicate, unknown, malformed, non-UTF-8, non-string, incompatible, and hostile URL records. Results validate the service endpoint, deduplicate and sort deterministically, and retain multiple URLs for one public identifier as separate choices. One result is offered but never silently selected; multiple results always require explicit selection, and direct-IP/QR controls remain available. Unpaired selection only fills the address. Paired selection sends no request for legacy identifier-less or mismatched credentials. Browsing remains live through offered/selection states so additions and removals update correctly, with generation gates and terminal cancellation rejecting stale callbacks. Focused tests passed 21/21, the full simulator suite passed 129/129, Release iOS build, plist/project checks, and `git diff --check` passed. Physical multicast validation remains open.
- QR backend checkpoint (2026-09-16): added a strict `ridesbridge-pairing` / `v1` JSON payload containing only a private IPv4 bridge URL, the current short-lived one-time PIN and expiration, stable non-secret 128-bit bridge ID, and API version. Unknown/duplicate fields, hostile URLs, missing/wrong type or version, and expired payloads are rejected. Reuse remains authoritatively rejected by the existing server-owned one-time PIN endpoint; no bearer/verifier is encoded. Bridge identity creation uses a crash-released cross-process lock, flushed same-directory temporary file, and atomic no-overwrite publication; malformed final identities fail closed.
- Terminal QR checkpoint: QRCoder 1.6.0 emits the payload at ECC-M. The renderer uses explicit ANSI black/white foreground/background plus Unicode half-block packing, retaining QRCoder's four-module quiet zone while fitting the full production payload in 65 visible columns by 33 terminal rows. Deterministic tests reconstruct the logical matrix from terminal output, exercise concurrent identity creation and replay, and preserve the manual PIN fallback. Bridge tests passed 104/104; the full non-integration .NET suite passed 522 with 1 skipped; warnings-as-errors and `git diff --check` passed.
- iPad QR checkpoint (2026-09-17): added a strict duplicate-key-aware parser for the exact public payload, canonical private IPv4 URL validation, injected expiration clock, and backward-compatible optional `bridgeId` persistence in the Keychain credential. QR pairing performs one health preflight and one PIN attempt with no permission-transition retry; the initial credential save transactionally includes the bridge ID and retains the existing revoke-on-save-failure behavior. Manual pairing remains unchanged. A VisionKit QR-only scanner has injectable availability/authorization seams, one-shot stop-before-import delivery, actionable permission/unavailable/runtime states, and lifecycle cancellation for interactive dismissal, permission-prompt dismissal, representable teardown, and coordinator reuse. The plist now contains a narrowly worded camera usage description; manual URL/PIN remains available. Focused scanner/core tests passed 43/43 before lifecycle hardening and scanner lifecycle tests passed 10/10 afterward; the final full Swift suite passed 107/107, Release simulator build, plist/project checks, and `git diff --check` passed.
- Physical QR repair (2026-09-17): the first real camera pass exposed two deterministic parity gaps before any bridge request. Terminal's ANSI half-block rendering distorted the dense production matrix, and `System.Text.Json` trims trailing fractional-second zeroes while the Swift parser required exactly seven digits. The bridge now emits a temporary lossless QRCoder PNG (16 pixels/module; 1040×1040 in this run) in its private data directory using an atomic owner-only `0600` file, a secret-free predictable filename, and PIN-expiry/shutdown cleanup; manual PIN remains available. Swift now accepts the strict `System.Text.Json` ISO-8601 extended profile with zero through seven fractional digits and rejects malformed dates and offsets. Pairing-focused backend tests passed 18/18, bridge tests 108/108, the full non-integration .NET suite passed 527 with 1 skipped, and warnings-as-errors/diff checks passed. Swift pairing-focused tests passed 9/9 and the full simulator suite passed 108/108; a freshly provisioned physical build installed and launched successfully.
- Physical QR acceptance (2026-09-17): VisionKit automatically locked onto the lossless QR without a tap, dismissed once, paired successfully, and the operator immediately completed an authenticated block-5 read with HTTP 200. Pairing returned exactly one HTTP 200; no failed or replayed pairing request appeared. PM3 evidence `/var/folders/ty/cs9d984d4s926vpqm0df5_sc0000gn/T/elevator/pm3-84181-20260916235624-session.log` contains only connect/version, fresh T55 detection, block-5 read, and disconnect—no write or dump. Clean shutdown removed the PNG and released TCP 5080 and `/dev/cu.usbmodem1301`.
- Proof-backed reconnect checkpoint (2026-09-17): public Bonjour TXT and the persistent bridge ID are treated only as untrusted discovery hints. Before either explicit Bonjour relocation or launch-time automatic relocation can disclose the saved bearer, the iPad sends unauthenticated `POST /api/v1/pair/proof` with a verifier-derived locator, fresh 256-bit nonce, and canonical candidate URL. The bridge returns an HMAC-SHA256 proof bound to the verifier, nonce, uppercase public bridge ID, exact currently reported private URL, and API `v1`; a shared C#/Swift fixed vector proves byte-level transcript parity. Only a strict, duplicate-key-free, 4 KiB-bounded valid response permits one bearer-authenticated `GET /api/v1/pair/status`. Wrong/malformed/revoked locators, forged/replayed-for-another-transcript proofs, identity/API/URL mismatch, churn, and cancellation send no status bearer and preserve the old credential/address. Manual entered-address and launch-override migration remain operator-selected status-only paths.
- Reconnect behavior: automatic browse starts once only for a persisted credential with a valid public bridge ID, waits for an injected 300 ms stable-result window, and requires exactly one candidate identity and URL. Legacy credentials, mismatches, multiple identities, one identity at multiple URLs, and result churn do not select automatically. Generation/token ownership gates every post-await mutation; stale proof completions cannot begin status, stale status completions cannot commit, and an attempted candidate is not retried automatically. Backend proof scans are bounded to 256 validated verifier records, compare every record, perform HMAC work for malformed/revoked/nonmatching entries, expose no verifier, persist no nonce/proof, access no hardware, and return generic secret-free failures.
- Reconnect validation (2026-09-17): independent security review found and repaired stale proof-to-status cancellation races, unbounded Swift proof responses, duplicate-key acceptance, and malformed verifier-store failures. Bridge-focused tests passed 132/132; the full non-integration .NET suite passed 552 with 1 skipped and 0 failed. The full Swift simulator suite passed 146/146; focused client tests passed 18/18 in final orchestrator verification. Release simulator warnings-as-errors build, solution warnings-as-errors build, plist/project checks, and `git diff --check` passed. No multicast, PM3, card, or other hardware operation occurred. Residual prototype limitation: local transport is plaintext HTTP, so an active LAN MITM could relay/observe later authenticated traffic; proof prevents a merely spoofed Bonjour service from receiving the bearer but is not a substitute for authenticated encryption.
- Physical Bonjour/reconnect acceptance (2026-09-17): a freshly signed build using provisioning profile `a4165047-bd83-4552-8612-6756111ff847` was installed in place, preserving the physical iPad's Keychain credential. On home Wi-Fi, one advertised bridge at `192.168.0.163:5080` caused exactly one unauthenticated proof request followed by exactly one bearer-authenticated status request, both HTTP 200, and transactionally retained the pairing. Two physically advertised instances with the same public bridge ID at ports 5080/5081 were both observed by DNS-SD and caused zero iPad requests; adding a third instance with a different ID at port 5082 was likewise observed and caused zero requests. A direct-IP launch override while discovery was ambiguous issued exactly one authenticated status request and no proof request, confirming the explicit fallback remains independent of automatic Bonjour selection.
- Physical hotspot acceptance (2026-09-17): after both devices moved to the iPhone 13 Pro hotspot, the Mac bound `172.20.10.4:5080`; DNS-SD observed exactly one `_elevator-rides._tcp` addition. Launching the existing paired app without an address override automatically sent exactly one `POST /api/v1/pair/proof` (HTTP 200) and then one `GET /api/v1/pair/status` (HTTP 200), moving the saved credential from the home address without a PIN, QR, retry, or bearer disclosure before proof. Both network runs kept `/dev/cu.usbmodem1301` unopened and issued no hardware/card request. Final shutdown released TCP ports 5080/5081/5082, removed all temporary QR artifacts and secret-bearing temporary logs, and left no PM3 owner.
- Everyday launch mode (2026-09-17): `dotnet run --project RidesBridge/RidesBridge.csproj -- --everyday` now forces Bonjour-capable wildcard IPv4 binding, selects the first bindable port in 5080...5179, retains durable ApplicationData identity/verifier paths (and existing explicit path overrides), and forces PM3 auto-discovery while clearing any fixed PM3 port. The no-flag safe-loopback/configured path is unchanged. Kestrel and the hosted lifecycle now start before PIN/QR output, so a raced port or startup failure cannot display unusable pairing material; the pre-bind port check remains a documented TOCTOU and is never retried into inconsistent advertised state. Strict parsing/help, occupied gaps/exhaustion, real socket cleanup, process startup, configuration precedence, and selected-port Bonjour propagation have deterministic tests. Focused launch tests passed 18/18, bridge tests passed 150/150, and the warnings-as-errors solution build passed with zero warnings/errors. A full non-integration run had one unrelated nondeterministic PM3 garbage-bit scanner failure; its immediate focused rerun passed 1/1.
- Local Forget reset (2026-09-17): the operator-facing Forget button no longer attempts `/api/v1/pair/revoke` or depends on the old server being reachable. It immediately invalidates/cancels Bonjour browsing, quiescence, explicit and automatic relocation, proof/status work, and stale callback ownership; clears the in-memory bearer, Keychain credential, candidates, reads, and write snapshots; and returns to an unpaired screen where QR scanning is enabled. Late proof/status/read/write completions cannot restore paired state. A Keychain deletion failure still drops the active in-memory bearer and permits repair with an actionable local message. Server revocation remains only as best-effort transactional cleanup for a newly issued bearer that could not be saved. Focused tests passed 47/47, the full simulator suite passed 148/148, and the unsigned Release simulator build passed.
- Truthful connection state (2026-09-17): automatic launch discovery now publishes `Searching for saved bridge…`, not `Connected`, until token-bound proof plus authenticated status succeeds. A physical follow-up exposed that the first repair covered only automatic candidate removal: a live/manual browser or an empty `.ready` callback could still display a searching discovery message beside Connected. The invariant now applies to every active browse: starting discovery, receiving empty results, or losing the advertised candidate clears connection proof and shows Searching while retaining the credential. Stop/denial/failure return the unverified credential to `Saved pairing restored`; only a newly authenticated candidate restores Connected. Re-advertisement is a new lifecycle event and must pass proof/status again. Focused Bonjour tests passed 28/28, the full simulator suite passed 151/151, Release simulator build and `git diff --check` passed.
- Assumptions: Slice 1B software and physical acceptance are complete. Direct IP remains available throughout. The prior physical pairing store remains under the temporary acceptance directory; the first everyday-mode launch using durable default state will require one fresh QR pairing unless that state is deliberately migrated. Continue with Slice 3 codec/fixture generalization.

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

Only after Mercury is proven, replace the Mercury-only Swift path with a registered structural model matching production C# for all currently registered sequences:

```text
mercury, venus, earth, pluto, mars,
jupiter, saturn, uranus, neptune,
charon, nix
```

The earlier nine-family wording predated Charon/Nix discovery. Slice 3 must cover all eleven entries in `EncodingSequences.All`.

## TDD todos — shared fixtures and codec

- [x] Expand the shared fixture format/version to include sequence metadata `(name, zeroBlock, rotation, min/max)` and all `0...500` encodings for all eleven sequences.
- [x] Make C# tests validate the shared fixture against `EncodingSequences`; fixture drift must fail visibly.
- [x] Add selected hardware-observed boundary vectors and malformed blocks independent of generated round trips.
- [x] Add Swift tests for every fixture entry before changing the network workflow.
- [x] Generalize the Mercury codec into a small sequence/counter model matching `RideCounterCodec` and `EncodingSequence`:
  - exact rotation handling including 0 and 4;
  - nine-bit counter structure;
  - application range `0...500`;
  - exact structural round-trip validation.
- [x] Add registry validation for duplicate friendly names and encoded collisions.
- [x] Prove no self/cross collisions over `0...500`; retain `0...511` as diagnostic parity where useful.
- [x] Match C# full-registry decoding and ambiguity behavior; do not guess families from visible high words.
- [x] Replace the preliminary Swift multi-family implementation or refactor it into the proven model; remove temporary Mercury-only production code once equivalent tests pass.
- [x] Preserve `FakeProxmark` samples and tests through the migration.

## TDD todos — network workflow

- [x] Rename Mercury-specific ride endpoints/types to sequence-agnostic
  `GET /api/v1/hardware/page0/mirrors` and `POST /api/v1/hardware/page0/mutations`.
- [x] Add mutually exclusive `--fake-pm3` launch mode with deterministic mirrored blocks for iPad smoke without USB hardware.
- [x] Update ride reads to report the decoded sequence and preserve the sequence selected by the authoritative mirror block.
- [x] Encode desired rides using that same sequence; never default a known non-Mercury token to Mercury.
- [x] Keep bridge contracts sequence-agnostic and raw-block based. The Mac must not become the ride-family authority.
- [x] Add endpoint/client tests showing identical bridge behavior regardless of sequence.
- [x] Add regression cases across `7/8`, `127/128`, `255/256`, and `383/384` for both rotation layouts.
- [x] Defer real Proxmark3 card read/write acceptance to plan-end hardware validation. iPad Wi-Fi smoke against `--fake-pm3` is the Slice 3 physical network checkpoint.

## Acceptance

- All eleven registered families have shared C#/Swift fixture parity and exhaustive Swift tests.
- Network ride reads/writes preserve the source sequence.
- No identity/reset concerns are mixed into the ride codec.
- Renamed page0 mirror/mutation APIs work with the physical iPad over Wi-Fi against `--fake-pm3`.

## Agent notes / assumptions

- Notes (2026-09-18 handoff decisions):
  - Cover all eleven registered sequences, including Charon and Nix.
  - Rename Mercury-specific bridge ride endpoints now to sequence-agnostic
    `GET /api/v1/hardware/page0/mirrors` and `POST /api/v1/hardware/page0/mutations`
    (and rename matching Swift/client/error identifiers). Keep the Mac raw-block based.
  - Add an explicit non-default `--fake-pm3` launch mode for deterministic mirrored
    blocks so iPad Wi-Fi smoke can proceed without USB hardware. Everyday mode remains
    real PM3 USB auto-discovery and must stay mutually exclusive with `--fake-pm3`.
  - Defer real PM3 hardware acceptance to the end of the overall plan. Unit/contract
    tests first; physical iPad-on-router smoke against `--fake-pm3` is allowed after
    the software checkpoint.
- Assumptions: Fixture/oracle generalization and endpoint rename can complete without
  a connected Proxmark3. Existing Mercury physical evidence remains valid historical
  Slice 2 proof and is not re-run as part of Slice 3 software work.
- Notes (2026-09-18, Slice 3 chunk 1 — shared fixtures + C# oracle/resolver drift tests):
  - Added `TestFixtures/RideEncoding/ride-encoding-v2.json` (`schemaVersion: 2`,
    `fixtureId: ride-encoding-v2`) as the single live oracle for all eleven registered
    sequences. Kept `mercury-v1.json` as a historical Slice 2 compatibility reference only;
    removed Mercury-only C# fixture tests.
  - Fixture shape: shared `boundaries` `[0,1,7,8,127,128,255,256,383,384,500]`;
    per-sequence metadata + `501` encodings each (`5511` total); `121` rejected
    application-range entries (`501...511` per sequence); `7` structural malformed blocks;
    `9` cross-family `mirrorCases` for `RideBlockResolver`.
  - Shared models/support: `TestFixtures/RideEncoding/RideEncodingFixtureModels.cs`,
    `RideEncodingFixtureSupport.cs`; generator at
    `Tokens.Tests/RideEncodingFixtureGenerator.cs` (explicit regen test).
  - C# drift tests: `Tokens.Tests/RideEncodingFixtureOracleTests.cs` (schema, per-sequence
    oracle parity, uniqueness, cross-sequence collision guard, rejected/malformed validation,
    generator parity); `RidesCli.Tests/RideEncodingFixtureResolverTests.cs` (all encodings,
    rejected/malformed, mirror cases against full registry).
  - Focused tests:

    ```bash
    dotnet test Tokens.Tests/Tokens.Tests.csproj --filter FullyQualifiedName~RideEncodingFixture
    dotnet test RidesCli.Tests/RidesCli.Tests.csproj --filter FullyQualifiedName~RideEncodingFixture
    ```

    Results: Tokens `5/5` passed; RidesCli `4/4` passed.
  - Broader suites:

    ```bash
    dotnet test Tokens.Tests/Tokens.Tests.csproj
    dotnet test RidesCli.Tests/RidesCli.Tests.csproj
    ```

    Results: Tokens `140/140` passed (explicit generator skipped); RidesCli `193/193` passed.
  - Follow-up for next chunk: Swift fixture parity tests + codec generalization; bridge
    endpoint rename and `--fake-pm3` remain unchecked network-workflow todos.
- Notes (2026-09-18, Slice 3 chunk 2 — Swift codec generalization + call-site migration):
  - Generalized production codec in `RidesTablet/Domain/RideEncoding.swift`:
    `RideCounterCodec`, `RideSequence`, `RideSequenceRegistry`, `RideBlockResolver`,
    and `RideRead` (with `sequence` metadata). Deleted `MercuryRideCodec.swift` and
    retired `MercuryRideCodecTests.swift`; live Swift oracle is `ride-encoding-v2` only
    via `RideEncodingFixtureTests.swift` (`mercury-v1.json` remains on disk as historical
    reference, unused by tests).
  - Migrated bridge call sites to the generalized API:
    `BridgeConnectionModel` and `BridgePhysicalAcceptanceCoordinator` now resolve mirrors
    through `RideBlockResolver` and encode desired rides with the resolved `RideSequence`
    from the authoritative mirror (never Mercury-by-default). `TokenDecoder` now uses
    `RideSequenceRegistry.tryDecode`.
  - Xcode project: removed Mercury-only sources; added `RideEncodingFixtureTests.swift`.
  - Focused Swift tests:

    ```bash
    xcodebuild test \
      -project RidesTablet/RidesTablet.xcodeproj \
      -scheme RidesTablet \
      -destination 'platform=iOS Simulator,name=RidesTablet iPad Air 4' \
      -only-testing:RidesTabletTests/RideEncodingFixtureTests \
      -only-testing:RidesTabletTests/RideEncodingTests \
      -only-testing:RidesTabletTests/FakeProxmarkTests \
      -only-testing:RidesTabletTests/ResetSequenceTests \
      -only-testing:RidesTabletTests/MercuryBridgeWorkflowTests \
      -only-testing:RidesTabletTests/BridgePhysicalAcceptanceCoordinatorTests
    ```

    Results: `37/37` passed.
  - Full Swift simulator suite:

    ```bash
    xcodebuild test \
      -project RidesTablet/RidesTablet.xcodeproj \
      -scheme RidesTablet \
      -destination 'platform=iOS Simulator,name=RidesTablet iPad Air 4'
    ```

    Results: `157/157` passed (net `+12` fixture tests, `-6` retired Mercury-only tests).
  - Follow-up for next chunk: bridge endpoint rename to page0 mirrors/mutations and
    `--fake-pm3` launch mode remain unchecked network-workflow todos.
- Notes (2026-09-18, Slice 3 chunk 3a — .NET page0 mirror/mutation rename):
  - Renamed bridge ride endpoints and contracts from Mercury-specific names to
    sequence-agnostic page0 identifiers. No Swift changes in this chunk.
  - `GET /api/v1/hardware/page0/mirrors`, `POST /api/v1/hardware/page0/mutations`;
    `Page0MirrorReadResponse`, `Page0MutationRequest`, `Page0ConditionalWriter`,
    `page0_block_not_allowed`, `ReadPage0MirrorAsync`, etc.
  - Renamed `MercuryBridgeOperations.cs` → `Page0BridgeOperations.cs`;
    `MercuryBridgeTests.cs` → `Page0BridgeTests.cs`.
  - `--fake-pm3` launch mode remains unchecked for chunk 3b.
  - Focused tests:

    ```bash
    dotnet test RidesBridge.Tests/RidesBridge.Tests.csproj --filter "FullyQualifiedName~Page0|FullyQualifiedName~Mercury|FullyQualifiedName~Mirror|FullyQualifiedName~Mutation"
    ```

    Results: `22/22` passed.
  - Full suite:

    ```bash
    dotnet test RidesBridge.Tests/RidesBridge.Tests.csproj
    ```

    Results: `150/150` passed.
  - Follow-up for chunk 3b: `--fake-pm3` launch mode and Swift client path updates.
- Notes (2026-09-18, Slice 3 chunk 3b — `--fake-pm3` launch mode):
  - Added mutually exclusive `--fake-pm3` launch mode with the same strict single-flag parsing
    rules as `--everyday`. It reuses everyday port selection (`0.0.0.0`, 5080..5179),
    terminal URL reporting, and best-effort Bonjour, but injects `FakePm3Device` and never
    opens USB or auto-discovers serial.
  - Seeded mirrors for iPad smoke: block5/block6 `BBC7FD03` (Venus, 180 rides remaining).
  - New files: `RidesBridge/FakePm3Device.cs`, `RidesBridge.Tests/FakePm3LaunchTests.cs`.
  - Updated: `EverydayLaunchMode.cs` (`BridgeLaunchOptions`, `FakePm3LaunchMode`),
    `Program.cs`, `BridgeOptions.cs`, `README.md`.
  - Focused tests:

    ```bash
    dotnet test RidesBridge.Tests/RidesBridge.Tests.csproj --filter FullyQualifiedName~FakePm3LaunchTests
    dotnet test RidesBridge.Tests/RidesBridge.Tests.csproj --filter FullyQualifiedName~EverydayLaunchTests
    ```

    Results: `11/11` fake-pm3 + `20/20` everyday passed.
  - Full suite:

    ```bash
    dotnet test RidesBridge.Tests/RidesBridge.Tests.csproj
    ```

    Results: `163/163` passed.
  - Follow-up completed in chunk 3c: Swift client path updates for page0 mirrors/mutations.
- Notes (2026-09-18, Slice 3 chunk 3c — Swift page0 client rename):
  - Renamed Swift bridge ride mirror/mutation DTOs, client methods, connection-model
    diagnostics, physical-acceptance coordinator, and workflow tests from Mercury-specific
    names to sequence-agnostic page0 identifiers. Paths are now
    `GET/POST /api/v1/hardware/page0/mirrors|mutations`.
  - Encoding for set still uses resolved `RideRead.sequence` (no Mercury default).
  - Renamed `MercuryBridgeWorkflowTests.swift` → `Page0BridgeWorkflowTests.swift` and added
    Venus fake-pm3 seed sequence-preservation workflow coverage (`BBC7FD03` / 180 rides).
  - No RidesBridge/.NET changes in this chunk.
  - Focused tests: BridgeClientTests + Page0BridgeWorkflowTests + BridgePhysicalAcceptanceCoordinatorTests → `36/36` passed.
  - Full Swift simulator suite → `158/158` passed.

---


- Notes (2026-09-18, Slice 3 physical iPad Wi-Fi smoke against `--fake-pm3`):
  - Bridge launched with `dotnet run --project RidesBridge/RidesBridge.csproj -- --fake-pm3`,
    bound `http://0.0.0.0:5080`, reachable `http://192.168.0.163:5080/`, seeded Venus mirrors
    `BBC7FD03`/`BBC7FD03` (180 rides). No USB/PM3 port opened.
  - Freshly signed `com.itgeorge.RidesTablet` Debug build (team `TJY5296P7S`, profile
    `a4165047-bd83-4552-8612-6756111ff847`) installed on physical iPad Air 4
    (`00008101-001A68EE21F0001E` / CoreDevice `494BBD09-0DEB-5B41-B915-3B4258F1DBA2`).
  - App launched with `RIDES_BRIDGE_ADDRESS_OVERRIDE=192.168.0.163:5080` and
    `RIDES_PHASE2_PHYSICAL_ACCEPTANCE=1`. Existing Keychain credential revalidated via
    authenticated `GET /api/v1/pair/status` (HTTP 200); no new PIN required.
  - Physical acceptance runner exercised renamed page0 APIs only: mirrors read → write →
    alreadyApplied → stale conflict → restore write → final mirrors read. All requests
    returned HTTP 200. Sequence-preserving encode used the resolved Venus family from the
    fake seed (not Mercury-default).
  - Real Proxmark3 card acceptance remains deferred to plan-end hardware validation.

# Slice 4 — Targeted token details, explicit-profile reset, and unknown dumps

## Goal

Complete the non-UI operator behavior while preserving the low-read-pressure rule.

### Read strategy

- Known-flow scan: detect/tune as needed, then read block 4 and mirrors 5/6.
- Decode mirrors on iPad.
- If registered: stop; do not read a full page.
- If unknown: read only missing blocks `0,1,2,3,7` (block 4/5/6 are already available), assemble exactly eight blocks, and save the big-endian dump on iPad.

## TDD todos — targeted scan and unknown path

- [x] Define/test raw targeted-read contracts without introducing a generic unaudited command endpoint.
- [x] Test known scans touch only blocks 4/5/6 and return signal strength plus raw values.
- [x] Test no-chip/tune/read failures remain distinguishable and do not trigger write or dump requests.
- [x] Test unknown decode triggers reads of only the missing five blocks exactly once.
- [x] Test page assembly preserves block order and writes exactly 32 bytes, big-endian, with the existing filename convention.
- [x] Preserve `Unknown, logged` only after successful local iPad persistence; log failure remains an error.
- [x] Test interruption during missing-block reads produces no falsely successful dump.

## TDD todos — identity/reset parity

- [x] Add shared C#/Swift identity-profile fixture parity for all canonical and recognition-only profiles.
- [x] Validate canonical reset images against the embedded C# big-endian images, including block 4 and Neptune block 7 metadata.
- [x] Keep reset selection explicit and empty by default.
- [x] Build reset mutations only for blocks that actually need change:
  - first read candidate target blocks 1...6;
  - if identity 1...4 already matches and mirrors belong to the selected sequence, target only 5/6;
  - otherwise target changed blocks in 1...6;
  - never include 0/7.
- [x] Use the same conditional mutation primitive from Slice 2; do not add a blind overwrite endpoint.
- [x] Test full preflight-before-write, desired-state retry, per-block verification, rollback, and incomplete rollback reporting across 1...6.
- [x] Confirm server continues reset verification/rollback after client disconnect.
- [x] Return verified current block values after success so the iPad state reflects hardware, not optimistic assumptions.
- [x] Preserve block 4 as Apt # during normal ride writes; explicit profile reset may overwrite it by design.

## Temporary-screen acceptance

- [x] Add block 4 display, signal value, and unknown-log result to the diagnostic workflow (scan path only; reset picker deferred to identity/reset chunk).
- [x] Keep controls functional rather than visually polishing Concept A in this slice.
- [x] Add view-model tests for scan/unknown success, persistence failure, cancellation, and no dump on known tokens (reset/conflict/rollback deferred).

## Physical acceptance — black card

- [x] Deferred to plan-end Proxmark3 hardware validation. Slice 4 physical network checkpoint is iPad Wi-Fi smoke against `--fake-pm3` only (no USB card writes).
- [x] Record read-only blocks 1...6 before reset testing; do not read 0/7 unless the unknown-dump test specifically requires the complete image.
- [x] Exercise one explicit reset profile through the conditional path only after confirming restoration values are available.
- [x] Verify only expected target blocks changed and the app reports verified hardware state.
- [x] Restore the original 1...6 values when safe/required and verify each target block.
- [x] Never write blocks 0 or 7.
- [x] Keep the number of repeated reads/writes minimal and record any antenna instability.

## Acceptance

- Known scans avoid full dumps.
- Unknown scans produce the required exact dump with no redundant reads.
- Reset is explicit, conditional, verified, rollback-capable, and block-safe.
- C#/Swift profile/reset parity is test-backed.
- iPad Wi-Fi smoke against `--fake-pm3` covers scan/unknown/reset network paths without real PM3 hardware.

## Agent notes / assumptions

- Notes (2026-09-18 handoff into Slice 4):
  - Slice 3 software + iPad `--fake-pm3` smoke are complete; real Proxmark3 card work remains deferred to plan end.
  - Continue with targeted scan / unknown-dump contracts first, then identity-profile fixtures and extended conditional reset mutations (blocks 1...6), then diagnostic-screen wiring and fake-pm3 iPad smoke.
- Assumptions: `--fake-pm3` can be extended to seed block 4 / missing page-0 blocks / resettable profiles for deterministic smoke without USB.
- Notes (2026-09-18, Slice 4 chunk 1 — targeted scan + unknown missing-block dump path):
  - Bridge contracts (authenticated, no generic block endpoint):
    - `GET /api/v1/hardware/page0/scan` → `{ version, block4, block5, block6, signalMillivolts }` (uppercase hex; LF tune + detect + reads 4/5/6 only).
    - `GET /api/v1/hardware/page0/missing` → `{ version, blocks: [{ block, value }, ...] }` with fixed allowlist `0,1,2,3,7` exactly once per request.
  - Hardware errors: `no_chip` (409), `lf_tune_failed` (503), `page0_read_failed` (502); interruption returns HTTP 499 without a success body.
  - `IBridgePm3Device` gained `ScanPage0Async` / `ReadPage0MissingBlocksAsync`; real adapter tunes then reads; `FakePm3Device` seeds:
    - known Venus: `CreateKnownVenusSeeded()` / default `--fake-pm3` — block4 `D6D1C733`, mirrors `BBC7FD03`, signal `420` mV, full page0 for missing reads;
    - unknown mirrors: `CreateUnknownMirrorsSeeded()` — block4 `00000004`, mirrors `DEADBEEF`/`FACECAFE`, deterministic missing blocks for dump assembly tests.
  - Swift: `BridgePage0ScanResponse`, `BridgePage0MissingBlocksResponse`, `Page0ScanWorkflow.assemblePage0`, `BridgeClient.scanPage0()` / `readPage0MissingBlocks()`, `BridgeConnectionModel.scanPage0Token()` orchestration + diagnostic UI section.
  - Focused tests:

    ```bash
    dotnet test RidesBridge.Tests/RidesBridge.Tests.csproj --filter FullyQualifiedName~Page0ScanBridgeTests
    xcodebuild test \
      -project RidesTablet/RidesTablet.xcodeproj \
      -scheme RidesTablet \
      -destination 'platform=iOS Simulator,name=RidesTablet iPad Air 4' \
      -only-testing:RidesTabletTests/Page0ScanWorkflowTests
    ```

    Results: bridge `8/8` passed; Swift `6/6` passed.
  - Broader suites:

    ```bash
    dotnet test RidesBridge.Tests/RidesBridge.Tests.csproj
    xcodebuild test \
      -project RidesTablet/RidesTablet.xcodeproj \
      -scheme RidesTablet \
      -destination 'platform=iOS Simulator,name=RidesTablet iPad Air 4'
    ```

    Results: bridge `171/171` passed; Swift `164/164` passed.
  - Follow-up for chunk 2: identity-profile fixtures, conditional reset mutations (blocks 1…6), reset picker UI, and `--fake-pm3` resettable profile smoke.
- Notes (2026-09-18, Slice 4 chunk 2 — identity fixtures + conditional reset mutations 1…6):
  - Shared fixture: `TestFixtures/IdentityProfiles/identity-profiles-v1.json` (13 profiles: 11 resettable + venus21ff + earth-a457; full 8-block reset images from embedded C# `.bin` files; Mercury mirrors encode 500 rides).
  - Bridge contracts:
    - `GET /api/v1/hardware/page0/blocks1to6` → `{ version, blocks: [{ block, value }, ...] }` fixed allowlist `1..6` exactly once (reset planning only; does not touch 0/7).
    - `POST /api/v1/hardware/page0/mutations` expanded to **1..6** targets, **1..6** mutations per request; still rejects 0/7 and duplicates. Ride set path on iPad still emits only blocks 5/6.
  - `IBridgePm3Device` gained `ReadPage0Block1To6Async` / `WritePage0Block1To6Async` / `ReadPage0Blocks1To6Async`; real adapter + `FakePm3Device` implement all three. Default `--fake-pm3` Venus seed unchanged; added `CreateVenusMirrorsOnlyResetSeeded()` (alias of known Venus) and `CreateVenusIdentityMismatchSeeded()` (block1 `21FF0031`).
  - Swift: `ResetSequence.resetRideCount` (Mercury=500); `Page0ResetPlanningWorkflow`, `BridgePage0Blocks1To6Response`, `BridgeConnectionModel.confirmResetProfile()` + diagnostic reset picker in `BridgeConnectionView` (empty selection by default; verified results update mirror state).
  - Focused tests:

    ```bash
    dotnet test RidesCli.Tests/RidesCli.Tests.csproj --filter FullyQualifiedName~IdentityProfileFixture
    dotnet test RidesBridge.Tests/RidesBridge.Tests.csproj --filter FullyQualifiedName~Page0BridgeTests
    xcodebuild test \
      -project RidesTablet/RidesTablet.xcodeproj \
      -scheme RidesTablet \
      -destination 'platform=iOS Simulator,name=RidesTablet iPad Air 4' \
      -only-testing:RidesTabletTests/IdentityProfileFixtureTests \
      -only-testing:RidesTabletTests/Page0ResetPlanningWorkflowTests \
      -only-testing:RidesTabletTests/Page0ResetWorkflowTests
    ```

    Results: C# fixture `2/2`; bridge Page0 `22/22`; Swift focused `13/13` passed.
  - Broader suites:

    ```bash
    dotnet test RidesBridge.Tests/RidesBridge.Tests.csproj
    xcodebuild test \
      -project RidesTablet/RidesTablet.xcodeproj \
      -scheme RidesTablet \
      -destination 'platform=iOS Simulator,name=RidesTablet iPad Air 4'
    ```

    Results: bridge `171/171` passed; Swift `176/176` passed.
  - Follow-up: iPad Wi-Fi smoke against `--fake-pm3` for scan + reset (mirrors-only and identity+mismatch seeds); real Proxmark3 card work remains deferred.
- Notes (2026-09-18, Slice 4 physical iPad Wi-Fi smoke against `--fake-pm3` — scan + Venus mirrors-only reset):
  - Added DEBUG-only `BridgeSlice4PhysicalAcceptanceCoordinator` + launch flag `RIDES_PHASE4_PHYSICAL_ACCEPTANCE=1` (Slice 2 flag `RIDES_PHASE2_PHYSICAL_ACCEPTANCE` unchanged). Focused tests: `BridgeSlice4PhysicalAcceptanceCoordinatorTests` `4/4` passed; full Swift simulator suite `180/180` passed; bridge `171/171` passed.
  - Bridge launched: `dotnet run --project RidesBridge/RidesBridge.csproj -- --fake-pm3`, bound `http://0.0.0.0:5080`, reachable `http://192.168.0.163:5080/`. No USB/PM3 serial opened.
  - Debug build installed on physical iPad Air 4 (`00008101-001A68EE21F0001E` / CoreDevice `494BBD09-0DEB-5B41-B915-3B4258F1DBA2`) with team `TJY5296P7S`, profile `a4165047-bd83-4552-8612-6756111ff847`, bundle `com.itgeorge.RidesTablet`.
  - App launched:

    ```bash
    xcrun devicectl device process launch \
      --device 494BBD09-0DEB-5B41-B915-3B4258F1DBA2 \
      -e '{"RIDES_BRIDGE_ADDRESS_OVERRIDE":"192.168.0.163:5080","RIDES_PHASE4_PHYSICAL_ACCEPTANCE":"1"}' \
      com.itgeorge.RidesTablet
    ```

    Existing Keychain credential revalidated via authenticated `GET /api/v1/pair/status` (HTTP 200); no new PIN required.
  - Acceptance HTTP sequence (all HTTP 200): `GET /api/v1/hardware/page0/scan` → `GET /api/v1/hardware/page0/blocks1to6` → `POST /api/v1/hardware/page0/mutations` (Venus reset, mirrors-only blocks 5/6 to `48C74948`) → `POST /api/v1/hardware/page0/mutations` (restore to `BBC7FD03`) → `GET /api/v1/hardware/page0/mirrors` (final verify).
  - Observed scan seed: block4 `D6D1C733`, mirrors `BBC7FD03`/`BBC7FD03` (Venus 180 rides), signal `420` mV. Post-restore scan confirmed seed restored. Mutation bodies were 140 bytes (two block-5/6 mutations only; blocks 0/7 untouched).
  - Identity+mismatch seed (`CreateVenusIdentityMismatchSeeded`) smoke remains a Slice 5+ follow-up; real Proxmark3 black-card acceptance remains deferred to plan end.

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

- [x] Add characterization tests for current Concept A behavior before changing the device boundary.
- [x] Refactor/rename `ProxmarkDevice` to a hardware-neutral boundary without coupling views to URLSession, HTTP status, bearer tokens, or PM3 command names.
- [x] Adapt `FakeProxmark` (renaming only if valuable) and keep all fake scenarios deterministic.
- [x] Implement `NetworkRideTokenDevice` as a translation layer from bridge raw results into domain outcomes.
- [x] Keep connection/pairing state separate from token state:
  - bridge unconfigured;
  - discovering/connecting;
  - pairing required;
  - connected/ready;
  - Mac reachable but PM3 unavailable;
  - connection lost while idle;
  - connection lost during read;
  - ambiguous/disconnected write requiring refresh.
- [x] Integrate connection setup into Concept A with large/simple operator behavior; diagnostics may remain behind a small developer/settings affordance.
- [x] Route Detect through the targeted scan path and preserve signal display, block 4 Apt #, current/pending rides, EUR, and unknown logging.
- [x] Route Charge through conditional mirror mutations and only update current rides from verified server results.
- [x] Route Reset through explicit-profile conditional mutations; confirmation remains disabled until selection.
- [x] Preserve exact user-facing requirements already tested in `RidesViewModel`.
- [x] Remove the temporary root/diagnostic-only workflow once Concept A covers the proven operations; retain useful connection diagnostics without duplicate business logic.
- [ ] Update `RidesTablet/README.md` and screenshots only after physical Concept A review.

## Validation

- [x] Run full Swift XCTest suite in simulator with fakes and network stubs.
- [x] Run full non-integration .NET suite including `RidesBridge.Tests`.
- [x] Run physical iPad smoke matrix over Wi-Fi against `--fake-pm3` (real Proxmark3/black-card steps deferred to plan end).
- [x] Run physical iPad + real PM3 matrix over Wi-Fi against `--everyday` (2026-09-18; see Final validation notes):
  - [ ] first pairing/manual or QR (existing Keychain credential reused; no fresh PIN/QR this run);
  - [x] reconnect (`GET /api/v1/pair/status` HTTP 200);
  - [x] known read (bridge `page0/scan`; **nix** token, not Venus fake seed);
  - [x] ride adjustment/write and verified reread (bridge mutations 500→490→0→500 with restore);
  - [x] stale expected conflict (`status=conflict`, no write);
  - [x] no chip — card removed from antenna (2026-09-18; see Final validation notes);
  - [ ] unknown dump — **skipped** (known nix token on antenna);
  - [x] explicit reset/cancel (Concept A smoke reset-cancel + nix mirrors-only reset via parameterized coordinator);
  - [ ] bridge/PM3 unavailable;
  - [ ] network loss before a write;
  - [ ] network loss after server accepted a write, followed by required refresh.
- [x] Confirm known read/write paths did not issue a full dump.
- [x] Deferred: confirm the black card's final state on real Proxmark3 hardware (2026-09-18; nix @ 500 rides — see Final validation notes; blocks 1..6 restored).
- [ ] Capture Concept A screenshots on the physical iPad or agreed iPad Air 4 simulator after fake-pm3 smoke.

## Acceptance

- Concept A is the normal app workflow using the Mac bridge.
- Fake mode remains available for deterministic UI work.
- Transport can later be replaced without rewriting operator/domain logic.
- No Concept B code or documentation is restored.
- Real Proxmark3 black-card acceptance remains deferred to plan-end hardware validation.

## Agent notes / assumptions

- Notes (2026-09-18 handoff into Slice 5):
  - Slices 3–4 committed on `ipad-rides` as `8bfa432`.
  - Continue Concept A integration with `--fake-pm3` / network stubs; keep real PM3 deferred.
- Assumptions: Diagnostic `BridgeConnectionView` can become a connection/settings affordance while Concept A becomes the root once `NetworkRideTokenDevice` covers scan/charge/reset/unknown.
- Notes (2026-09-18, Slice 5 — Concept A + hardware-neutral boundary; left uncommitted for orchestrator review):
  - Architecture:
    - Domain boundary `RideTokenDevice` (`scan` / `writeRideMirrors` / `reset`) with domain outcomes only.
    - `FakeProxmark` still deterministic for simulator/UI and still implements low-level `ProxmarkDevice` for legacy domain tests.
    - `NetworkRideTokenDevice` translates bridge `page0/scan` (+ `missing` only when unknown), conditional mutations 5/6 for charge, and `blocks1to6` + planned mutations 1…6 for reset. Conflicts/timeouts return `requiresRefresh` (no blind retry).
    - Connection stays in `BridgeConnectionModel`; Concept A token state stays in `RidesViewModel`.
    - App root is `RidesRootView`: connection gate → Concept A once ready; diagnostics sheet retains pairing/Bonjour + block-5 connectivity probe only (scan/charge/reset UI removed to avoid duplicating business logic). DEBUG “Use simulator fake reader” remains for UI work without a Mac.
  - Characterization first: `ConceptACharacterizationTests` (8) locked Detect/unknown-log/charge/reset semantics against FakeProxmark before the boundary change.
  - Focused tests:

    ```bash
    xcodebuild test \
      -project RidesTablet/RidesTablet.xcodeproj \
      -scheme RidesTablet \
      -destination 'platform=iOS Simulator,name=RidesTablet iPad Air 4' \
      -only-testing:RidesTabletTests/ConceptACharacterizationTests \
      -only-testing:RidesTabletTests/NetworkRideTokenDeviceTests \
      -only-testing:RidesTabletTests/DomainStateTests \
      -only-testing:RidesTabletTests/FakeProxmarkTests
    ```

    Result: `31/31` passed.
  - Full suites:

    ```bash
    xcodebuild test \
      -project RidesTablet/RidesTablet.xcodeproj \
      -scheme RidesTablet \
      -destination 'platform=iOS Simulator,name=RidesTablet iPad Air 4'
    dotnet test ElevatorTokens.sln --filter 'Category!=Integration&Category!=IntegrationParity'
    ```

    Results: Swift `196/196` passed; .NET non-integration green including `RidesBridge.Tests` `171/171` (and Tokens/RidesCli/Pm3UsbApi/RideCapture/LfElevatorCapture suites).
  - Follow-up checklist (this turn deferred smoke as too heavy):
    - Physical iPad Wi-Fi smoke against `dotnet run --project RidesBridge -- --fake-pm3` for the full Concept A matrix above.
    - Confirm known paths still never full-dump over the air.
    - README/screenshots after that smoke.
    - Real Proxmark3/black-card acceptance remains deferred to plan-end hardware validation.
    - Optional: identity-mismatch reset seed smoke carried from Slice 4.
- Notes (2026-09-18, Slice 5 physical iPad Wi-Fi smoke against `--fake-pm3` — Concept A via `NetworkRideTokenDevice`):
  - Preconditions: `xcrun devicectl list devices` showed `itgeorge iPad Air 4th Gen` `available (paired)` (`494BBD09-0DEB-5B41-B915-3B4258F1DBA2` / UDID `00008101-001A68EE21F0001E`). Port `5080` was free before bridge start. App launch path: `RidesTabletApp` → `RidesRootView` (connection gate → Concept A once `BridgeConnectionModel.isReadyForOperatorWorkflow`).
  - Added DEBUG-only `ConceptAPhysicalSmokeCoordinator` + launch flag `RIDES_SLICE5_CONCEPTA_SMOKE=1` to exercise Concept A through `RidesViewModel` + `NetworkRideTokenDevice` on physical iPad without UI automation. Production operator flow unchanged; flag is DEBUG-only like Slice 2/4 acceptance coordinators.
  - Bridge:

    ```bash
    dotnet run --project RidesBridge/RidesBridge.csproj -- --fake-pm3
    ```

    Bound `http://0.0.0.0:5080`; reachable `http://192.168.0.163:5080/` (also `http://10.2.0.2:5080/` on a secondary interface). No USB/PM3 serial opened.
  - iPad build/install (signing overrides required because repo `pbxproj` ships blank team / example bundle id):

    ```bash
    xcodebuild -project RidesTablet/RidesTablet.xcodeproj -scheme RidesTablet \
      -configuration Debug \
      -destination 'platform=iOS,id=00008101-001A68EE21F0001E' \
      -derivedDataPath /tmp/RidesTabletSlice5Derived \
      DEVELOPMENT_TEAM=TJY5296P7S PRODUCT_BUNDLE_IDENTIFIER=com.itgeorge.RidesTablet build
    xcrun devicectl device install app --device 494BBD09-0DEB-5B41-B915-3B4258F1DBA2 \
      /tmp/RidesTabletSlice5Derived/Build/Products/Debug-iphoneos/RidesTablet.app
    ```

    Profile `a4165047-bd83-4552-8612-6756111ff847`, team `TJY5296P7S`.
  - Concept A smoke launch:

    ```bash
    xcrun devicectl device process launch \
      --device 494BBD09-0DEB-5B41-B915-3B4258F1DBA2 \
      --terminate-existing \
      -e '{"RIDES_BRIDGE_ADDRESS_OVERRIDE":"192.168.0.163:5080","RIDES_SLICE5_CONCEPTA_SMOKE":"1"}' \
      com.itgeorge.RidesTablet
    ```

    Existing Keychain credential revalidated via authenticated `GET /api/v1/pair/status` (HTTP 200); no new PIN required.
  - Normal Concept A launch (no smoke flag) also verified reconnect-only path into `RidesRootView` → Concept A (`GET /api/v1/pair/status` HTTP 200).
  - Observed authenticated HTTP sequence during smoke (all HTTP 200): `GET /api/v1/pair/status` → `GET /api/v1/hardware/page0/scan` (Detect known Venus seed: block4 `D6D1C733`, mirrors `BBC7FD03`/`BBC7FD03`, 180 rides, signal `420` mV) → `POST /api/v1/hardware/page0/mutations` (charge 180→170) → `POST /api/v1/hardware/page0/mutations` (`alreadyApplied` at 170) → `POST /api/v1/hardware/page0/mutations` (stale-expected conflict) → `GET /api/v1/hardware/page0/scan` (refresh) → `GET /api/v1/hardware/page0/blocks1to6` → `POST /api/v1/hardware/page0/mutations` (Venus mirrors-only reset to 0 rides) → `POST /api/v1/hardware/page0/mutations` (restore to 180 rides). Reset-cancel semantics verified in coordinator before confirm.
  - Known-path negative checks: **no** `/api/v1/hardware/page0/missing`, **no** legacy `/mirrors` or `/block5` reads, **no** full page-0 dump. Only targeted `scan`, `blocks1to6` (reset plan), and conditional `mutations` on blocks 5/6 for charge/reset/restore.
  - Deferred on `--fake-pm3` without seed switching: no-chip, unknown-dump, bridge-unavailable messaging, network-loss mid-write / post-write refresh, identity-mismatch reset seed, fresh QR/manual pairing, screenshots, README refresh.
  - Post-smoke focused simulator re-check (`ConceptACharacterizationTests` + `NetworkRideTokenDeviceTests` + launch-config test): green.
  - Left uncommitted for orchestrator: Slice 5 integration plus DEBUG smoke coordinator; prefer single clean Slice 5 commit after orchestrator review.

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

- Notes (2026-09-18, plan-end real Proxmark3 hardware validation — user away, card left on antenna):
  - **Card identity mismatch (material):** Physical token on `/dev/cu.usbmodem1301` is registered **nix** profile `1BFE002A-F100C605-82045966-82045966` @ **500** rides (`FEC63352` mirrors), **not** the `--fake-pm3` / `ConceptAPhysicalSmokeCoordinator` Venus seed (`D6D1C733` / `BBC7FD03` @ 180). `RidesCli read` confirmed `sequence: nix`, `rides remaining: 500`. Safe restore of blocks 1..6 was still guaranteed for this token.
  - **Preflight snapshot (blocks 1..6, read-only via Pm3Cli then bridge):**
    - 1 `1BFE002A` · 2 `F100C605` · 3 `82045966` · 4 `82045966` · 5 `FEC63352` · 6 `FEC63352`
  - **Post-restore snapshot (Pm3Cli):** identical to preflight.
  - **Serial / bridge startup:**
    - Port free before launch (`lsof` empty on `/dev/cu.usbmodem1301`).
    - Everyday bridge: `dotnet run --project RidesBridge/RidesBridge.csproj -- --everyday`
    - Bound `http://0.0.0.0:5080`; reachable `http://192.168.0.163:5080/` (also `http://10.2.0.2:5080/`).
    - API `v1` / bridge `1.0.0`; real USB opened on first hardware request (`RidesBridge` held `/dev/cu.usbmodem1301`, not `--fake-pm3`).
  - **Bridge authenticated mutation matrix (localhost bearer via one-time PIN; blocks 0/7 never touched):**
    - charge 500→490 (`FEC63352`→`FEC62DB3`) → `written`
    - `alreadyApplied` at 490
    - stale-expected conflict (`expected` `11111111`/`22222222`) → `conflict`, mirrors unchanged at `FEC62DB3`
    - reset to 0 (`FEC62DB3`→`0DC7C70D`) → `written`
    - restore to 500 (`0DC7C70D`→`FEC63352`) → `written`
  - **iPad Concept A smoke (`RIDES_SLICE5_CONCEPTA_SMOKE=1`, address override `192.168.0.163:5080`, pre-parameterization run):** Debug build installed (`/tmp/RidesTabletFinalValDerived`, team `TJY5296P7S`, bundle `com.itgeorge.RidesTablet`). Observed HTTP: `GET /api/v1/pair/status` → `GET /api/v1/hardware/page0/scan` only — **no mutations** (smoke failed at detect: coordinator expected Venus/180/`D6D1C733`). **No** `/missing`, `/mirrors`, `/block5`, or full dump on known path.
  - **iPad Concept A smoke rerun (2026-09-18, parameterized coordinator):** Same Debug build path/team/bundle; bridge `dotnet run --project RidesBridge/RidesBridge.csproj -- --everyday` on `http://192.168.0.163:5080/`. Authenticated HTTP sequence: `GET /api/v1/pair/status` → `GET /api/v1/hardware/page0/scan` (nix @ 500) → `POST /api/v1/hardware/page0/mutations` (charge 500→490) → `POST /api/v1/hardware/page0/mutations` (`alreadyApplied` at 490) → `POST /api/v1/hardware/page0/mutations` (stale-expected conflict) → `GET /api/v1/hardware/page0/scan` (refresh) → `GET /api/v1/hardware/page0/blocks1to6` (reset plan) → `POST /api/v1/hardware/page0/mutations` (nix reset to 0) → `POST /api/v1/hardware/page0/mutations` (restore to 500). **No** `/missing`. Postflight Pm3Cli blocks 1..6 identical to preflight; `RidesCli read` confirmed `sequence: nix`, `rides remaining: 500`.
  - **Antenna instability:** `page0/scan` / `Pm3Cli tune` reported peak ~`46354`–`46483` mV on real PM3 vs fake-pm3 doc seed `420` mV — tune value out of simulator range; reads/writes still succeeded on both bridge-direct and parameterized Concept A smoke runs.
  - **Skipped (user away):** unknown-dump, network-loss, bridge-unavailable, fresh pairing/QR, identity-mismatch reset seed on real hardware.
  - **No-chip validation (2026-09-18, card removed from PM3 antenna):**
    - Preconditions: `/dev/cu.usbmodem1301` free before launch; token physically absent from reader; **no writes**.
    - Bridge: `dotnet run --project RidesBridge/RidesBridge.csproj -- --everyday` on `http://192.168.0.163:5080/`.
    - Authenticated localhost scan (`POST /api/v1/pair` one-time PIN → bearer): `GET /api/v1/hardware/page0/scan` → HTTP **409** `{"code":"no_chip","message":"No supported T55xx chip is present."}`; **no** `/api/v1/hardware/page0/missing` or `/api/v1/hardware/page0/mutations` in bridge logs.
    - Unauthenticated scan: HTTP **401**.
    - iPad Concept A (`RIDES_NOCHIP_DETECT=1`, `RIDES_BRIDGE_ADDRESS_OVERRIDE=192.168.0.163:5080`, existing Keychain credential): Debug build `/tmp/RidesTabletNoChipDerived`, team `TJY5296P7S`, bundle `com.itgeorge.RidesTablet`. Observed HTTP from iPad: `GET /api/v1/pair/status` (200) → `GET /api/v1/hardware/page0/scan` (409) only — **no** `/missing`, **no** `/mutations`, **no** charge/reset attempts. DEBUG one-shot helper calls `RidesViewModel.detect()` via `NetworkRideTokenDevice`; expected `.noChip` / `RidesViewModel.noChipMessage`.
  - **Fake PM3 no-chip profile (2026-09-18):** `RIDES_FAKE_PM3_PROFILE=no-chip dotnet run --project RidesBridge/RidesBridge.csproj -- --fake-pm3` reproduces the physical no-chip scan contract without USB (`FakePm3Device.CreateNoChip()` → HTTP 409 `no_chip`; `RidesBridge.Tests` host coverage in `Page0ScanBridgeTests.FakePm3NoChipScanReturns409NoChip`).
  - **Fake PM3 tune-failed profile:** `RIDES_FAKE_PM3_PROFILE=tune-failed dotnet run --project RidesBridge/RidesBridge.csproj -- --fake-pm3` reproduces LF tune failure without exotic hardware (`FakePm3Device.CreateTuneFailed()` → HTTP 503 `lf_tune_failed`; host coverage in `Page0ScanBridgeTests.FakePm3TuneFailedScanReturns503LfTuneFailed`).
  - **Shutdown:** `kill -TERM` on bridge released TCP 5080 and `/dev/cu.usbmodem1301`.
- Notes (2026-09-18, Slice 5 Concept A smoke parameterization — left uncommitted):
  - `ConceptAPhysicalSmokeCoordinator` no longer asserts Venus/`--fake-pm3` constants. On Detect it snapshots any `.known` registered token (sequence, rides, block4, mirrors 5/6, signal), charges by ±10 within 0…500, exercises already-applied/conflict/reset-cancel/reset/restore, and resets using the detected sequence. Venus seed values remain as optional `fakePm3*` documentation only.
  - Unit coverage: `ConceptAPhysicalSmokeCoordinatorTests` adds deterministic `ConceptAPhysicalSmokeFakeDevice` path for **nix** @ 500 and low-ride +10 charge branch.
  - Physical rerun: `--everyday` bridge + `RIDES_SLICE5_CONCEPTA_SMOKE=1` on paired iPad against nix @ 500 (see Final validation notes below).
- Assumptions: Orchestrator will run deterministic suites and commit; this session updated coordinator/tests/plan only (no commit).

---

# Follow-up work explicitly outside this plan

- [ ] Design and implement the future BLE custom reader hardware and `BluetoothRideTokenDevice` connector.
- [ ] Decide whether any Mac bridge path should remain as a supported backup after BLE hardware exists.
- [ ] Add public-internet/overlay networking only if a real operational need appears; do not expose this HTTP bridge directly to the internet.
- [ ] Evaluate Android direct-USB PM3 support separately if desired.
- [ ] Consider packaging the bridge as a signed menu-bar app or launch agent only after the command-line bridge proves reliable.
