# Apartment fast path — completed; physical removal check deferred

## Final status

Implementation and the authorized hardware happy path are complete, integrated through `7cb0043`. All Luna agents are settled. Parent reviewed the actual after-Save and separate fresh-Detect screenshots: Apt44, rides0, pending0, unchanged three-charge history totaling EUR0.09.

- Real iPad → new native bridge → PM3 edit **Apt43 → Apt44** succeeded once, without retry.
- One native-verified block4 write; bridge POST completed in **835.193 ms** (native command816ms). No separate detect/preflight/read command in the write interval. Background presence checks are separate; native SDK write verification remains enabled.
- Ordinary Detect AFTER Save independently read Apt44/0 rides. That is external TEST verification, not an extra read in the production Save path.
- Normal Debug app was rebuilt, installed and launched with NO physical-test flags/environment. New bridge remains healthy/running as PID62387. Pairing/bridge identity were preserved.
- **Physical token removal/reappearance is deferred by the user until they return.** Do not claim it was tested on hardware. Controlled unit/simulator cancellation tests passed.

## Current behavior

Apartment-only (`desiredRides == nil`) edits use the narrow authenticated fast endpoint; combined apartment+charge, ordinary charge and reset conditional paths remain unchanged. Detect already supplies the block3/4 snapshot. The fast lane reuses warmed detected state and trusts one native PM3 verified block4 write; no application-level fresh detect, preflight, duplicate readback, or forced post-save scan. Errors invalidate state and require Detect, without blind retry.

While either apartment popup is open, explicit background presence probes monitor the token regardless of Auto/calibration. Observed absence/failure/changed snapshot cancels the entire pending operation, never Skip into a charge. Save closes the popup, claims the operation and drains any existing probe before writing; generation/secret/lifecycle guards prevent stale callbacks or delayed absence from writing. An unobserved quick physical swap remains an explicitly user-accepted limitation.

## Validation and evidence

- Bridge tests: **221 passed**.
- SDK non-integration tests: **134 passed, 1 skipped**. Full solution run was blocked by unrelated test-host dependency errors; only the named suites are claimed green.
- Combined simulator suite: **285 unit +20 UI =303 passed, 0 failed, 2 expected physical skips**. Controlled absence/reappearance and race cases are simulator/unit fixtures, not physical removal evidence.
- Focused physical happy-path UI test passed. No charging/reset acceptance was rerun for this fast-path change.
- Receipt: `.docs/rides-apartment-fastpath-deployment-2026-11-19.md` and `.json`.
- Reviewed screenshots: `RidesTablet/Screenshots/apartment-fastpath-2026-11-19/`.
- Simulator result/log: `/tmp/rides-apt-fastpath-simulator/combined-after-709a1d2.{xcresult,log}`.
- Device result/log: `/tmp/rides-apt-fastpath-device/apartment-edit-physical.{xcresult,log}`.
- Normal final build log: `/tmp/rides-apt-fastpath-device/final-normal-debug-build.log`.
- PM3 session transcript: `/var/folders/ty/cs9d984d4s926vpqm0df5_sc0000gn/T/elevator/pm3-62387-20261119004813-session.log`. Packet-level native tracing was disabled; command/session and bridge HTTP logs are the evidence. Do not expose sensitive payloads.

## Remaining work when user returns

Arrange a real token removal/replacement while the popup is open, verify immediate cancellation/no write/no charge, then explicitly Detect and reopen on the replacement. This is the only deferred hardware check. The token is currently Apt44, rides0 under test secret `rides-ui-followup-test-2026-11-19`; memory-only secret must be reentered after normal restart. Read live state before further mutations.

## Usage policy

Watch11: session10%. Watch12: weekly3%. Both remain active. If either threshold triggers, stop starting work and report state, letting already-running bounded tasks finish. No agent is running now. Prefer Luna for subsequent implementation/operations and serialize simulator access.

## Reusable operations details

- Bridge loopback health: `curl --noproxy '*' http://127.0.0.1:5080/api/v1/health` (HTTP200). Previous403 from a different request was not an outage. LAN192.168.0.163:5080, native `--everyday`, durable identity/client records preserved.
- Authenticated GET `/api/v1/hardware/presence` → `{version:"v1",present:true|false}`; probes chip presence without wrapper block reads.
- Authenticated POST `/api/v1/hardware/apartment/block4/write-verified`, body `{version:"v1",desired:"XXXXXXXX"}` → `{version:"v1",block:4,verifiedValue:"XXXXXXXX"}`.
- **Signing teamTJY5296P7S**, not certificate CN suffixJ32GFNHFN7. Cached profile UUID5eb5c27f-34db-4bae-b733-c3934d6b8fbc, valid through2026-11-25T15:07:31Z, AppIDTJY5296P7S.com.itgeorge.RidesTablet. Code-sign identity SHA1D295CE338727FB69924B1597409283BB4EBC4513. Cached profiles under `/tmp/RidesTabletFinalUI/Build/Products/Debug-iphoneos/RidesTablet.app/embedded.mobileprovision` and `/tmp/RidesTabletPhysicalUITests/Build/Products/Debug-iphoneos/` (app AND UI runner). Public cached profiles/manual signing resolved account warnings; no Apple account/key changes were needed. UI runner requires its own profile. Always use explicit Git messages/`--no-edit`.
- iPad CoreDevice494BBD09-0DEB-5B41-B915-3B4258F1DBA2, XcodeUDID00008101-001A68EE21F0001E, appcom.itgeorge.RidesTablet. PM3 `/dev/cu.usbmodem11301`. Simulator3F9DD641-D0EF-432D-ACEB-F8BE555DEBBA.
- Physical tests require explicit DEBUG physical-write compile flag and runner opt-in; normal final launch must omit test flags/environment. Calibration bypass is DEBUG opt-in real Detect only, never fabricated calibration samples.

## Luna branches / completed sessions

- Backend/network: `vigil-35cbbf45-f748-4783-bced-e5535c31aeb8`, `.worktrees/rides-apt-fastpath`, branchfeature/rides-apt-fastpath.
- Model/presence: `vigil-502f5d20-ff22-4322-ac14-5dac2425e11c`, `.worktrees/rides-apt-presence`, branchfeature/rides-apt-presence.
- Validation/deployment: `vigil-57d40985-c612-402d-9b9f-8c9a05f2fb36`, `.worktrees/rides-apt-fastpath-e2e`, branchtest/rides-apt-fastpath-e2e. Final physical evidence sourcecommita449fdd integratedmain7cb0043; all relevant earlier commits integrated.

Unrelated untracked debug artifacts and `post-ride-captures.log` remain untouched. No raw signing key, bearer or PIN was committed.
