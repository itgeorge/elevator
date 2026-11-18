# Rides iPad UI follow-up — completed implementation record

## Final status

All requested changes below are implemented and committed. Final integration is `347813b`; normal Debug app build/install/launch succeeded with no test flags or calibration bypass active. All Luna agents are settled. Parent visually accepted the real iPad's full-frame landscape-first charge/edit popup captures, saved-secret hash row, opaque confirmations, and final landscape history with every EUR value visible.

Evidence: `.docs/rides-ui-e2e-2026-11-19.{md,json}` and `RidesTablet/Screenshots/ui-followup-2026-11-19/`. Final unit run: 276/276 passed. Simulator UI suite: 14 passed, 2 expected physical skips; focused rotation, confirmation/Auto preservation, and landscape EUR-bounds checks also passed. Physical acceptance passed; the subsequent latest-source read-only landscape/history test passed. Its fresh Detect observed 0 rides and apartment43. It did not independently inspect raw profile/mirror blocks. Session-file audit verified charge-time apartments, first-charge-derived start time, and no test secret in persisted sessions.

The test token's apartment is now sealed under test secret `rides-ui-followup-test-2026-11-19`; the secret must be reentered after app restart. Bridge remains running, iPad pairing unchanged, no new Mac client credential was created. Watch10 remains active at the 10% session threshold. No implementation work remains; old checkpoint details below are historical context.

## Usage pause policy and completed delegation

After compaction the user explicitly authorized implementation, retaining Luna delegation and the <=10% usage pause rule. Watch **10** (`codexusage session 10`) is active; the old watch 9 was stopped to avoid duplicate notifications. If watch 10 triggers, stop starting work and let already-running children finish; preserve their commits for later integration.

Three bounded Luna implementation tasks were launched from main `527b64c`:
- UI/keypad/confirmations/history table and UI test updates: `vigil-3b2ab0b4-c858-4f2f-9d4f-558c02e423d7`, branch `ui/rides-followup`, `.worktrees/rides-followup-ui`. Owns `ApartmentEntrySheet.swift`, `ContentView.swift`, UI tests and screenshot fixtures. Owns simulator lock, but instructed **not to run tests yet**; return implementation first, then run one integrated suite after parent integrates other commits.
- Model/session/history metadata and apartment mutation/dismissal semantics: `vigil-eb025839-baa8-4544-a130-609205b76fbc`, branch `model/rides-followup`, `.worktrees/rides-followup-model`. Owns `RidesViewModel.swift`, `ChargeSessionStore.swift`, model/unit tests.
- Secret settings/hash plus read-only latency investigation: `vigil-6f83434b-ba52-4b9d-8b3c-1825b6b63117`, branch `ui/rides-followup-settings`, `.worktrees/rides-followup-settings`. Owns `BridgeConnectionView.swift`, `ApartmentSecretStore.swift`, secret unit tests and `.docs/rides-apartment-edit-latency-followup.md`. No hardware changes or optimization without evidence.

Agreed API contracts: `CompletedCharge.apartmentNumber: UInt8?` (default nil, old JSON compatible), `ChargeSession.startedAt: Date` (first charge, provisional creation date only while empty), `RidesViewModel.canStartNewSession: Bool`, `await model.dismissApartmentEntry()` (outside-only: edit Cancel, charge Skip). Existing explicit Cancel/Skip/Save retain meanings. Model agent handles close-before-await common loading; UI agent must not duplicate dismissal actions. All tasks explicitly prohibit hardware mutations and interactive Git editors.

## Historical integration checkpoint (main `a9dab0f`, superseded by final status above)

All requested source changes are integrated. **276 unit tests passed** on final main-equivalent app code (`/tmp/rides-ui-followup-simulator/xcodebuild-unit-final.log`); **16 simulator UI tests: 14 passed, 2 expected physical skips**, plus focused rotation-evidence and Auto-preservation-on-confirmation-Cancel tests passed. An earlier combined suite failed a newly added held-write fixture because Auto was not calibrated; model owner corrected its distinct empty/present probes in main `96841f5`; the final 276-unit rerun passed. Do not report the earlier failed combined bundle as green. Final UI screenshot-a11y fix `8f15810`; physical test secret override/strong preflight `01ad114`; confirmation Auto-preservation regression `a9dab0f`.

Parent reviewed portrait keypad (good: 0 adjacent rows, large actions, blank input), secret fingerprint/settings (good), opaque New confirmation (good). **Landscape simulator screenshots are NOT accepted**: normalized copies are incorrectly portrait-shaped and raw 2360x1640 captures themselves show a rotated/cropped UI. Artifact analysis could not restore missing content. Passing automated frame/hittability checks are not sufficient visual proof.

**Active bounded physical operations task:** same settings/ops Luna `vigil-6f83434b-ba52-4b9d-8b3c-1825b6b63117` now owns iPad and `PhysicalRidesUITests.swift`; instructed to merge current main, extend real test to open charge AND edit popups after switching to landscape (not just rotate an existing popup), capture/review native full-frame screenshots, run live E2E with a fresh test secret, independently verify token/session metadata, and only then normal-build/install/launch without flags. It may finish this already-started task if quota watch triggers, but parent must stop starting work at <=10%.

Fresh physical test secret: `rides-ui-followup-test-2026-11-19`; pass as `TEST_RUNNER_RIDES_UI_PHYSICAL_SECRET=...` to xcodebuild so runner sees `RIDES_UI_PHYSICAL_SECRET`. Previous token Apt43 under the old secret is unknown under this fresh secret (offline snapshot checked; live UI preflight still mandatory before writes). Strengthened test rejects ANY already-decoded apartment before starting mutation cases, preventing blindly rerunning a partially completed test. Expected final token again Mercury0/Apt43 but sealed under the NEW secret; do not assert that until verified. No automatic mutation retry after failure. Existing Mac test bearer was revoked; ops can locally pair a fresh private Mac client for final independent read, revoke only that client afterward. Never expose credentials or revoke iPad pairing.

Planned physical evidence paths: `/tmp/rides-ui-followup-physical-2026-11-19*.log/.xcresult`, tracked `RidesTablet/Screenshots/ui-followup-2026-11-19/`, `.docs/rides-ui-followup-e2e-2026-11-19.json`, `.docs/rides-ui-followup-progress-2026-11-19.md`. Ops must not claim clipping fixed if real iPad screenshots also fail; report blocker for a UI correction. No follow-up physical result or normal redeployment has been received yet at this checkpoint. Simulator lock is released; other two Luna agents are settled.

Next: wait for ops result; inspect real landscape-first screenshots before signoff. Integrate its physical-test/docs/evidence commit if usage permits; otherwise leave its branch intact and pause. The old Nov18 receipts remain historical and should not be overwritten. `.docs/rides-apartment-edit-latency-followup.md` describes fresh detect, preflight reads of blocks3/4, block3 compare-only guard, one verified block4 write, and optional Auto drain/postscan. It expressly does NOT claim the old unidentified two-target requests measure apt-only latency. Preserve unrelated untracked debug work.

## Requested modifications (implemented)

1. Apartment popup **Cancel / Skip / Save** actions should be as large as the keypad buttons.
2. Keep **0** together with the other digits, with the same normal grid spacing (no separated-out bottom row).
3. Remove the **Backspace** button. Add a **circled X inside the number input** that clears the entire value; show it only when there is a value.
4. Remove the popup's **close X**. Close through Cancel or an outside tap. **Outside tap means Cancel for apartment editing, but Skip for the charge-token flow.** Handle modal dismissal explicitly so this neither drops nor duplicates a charge.
5. In the **charge-token apartment entry flow**, dismiss the popup once number validation passes and perform the combined charge + apartment payload under the normal shared Charge Token loading state. Do not leave the keypad visible while writing.
6. Investigate/explain why **apartment-only editing is slow**: identify actual preflight reads, writes, verification/readbacks, and refreshes. User asks what happens beyond writing the apartment block. Inspect/measure the real path before optimizing; preserve necessary conditional-write/verification safety.
7. **New-session** and **calibration** confirmations should be **opaque modals**, not the current faded/translucent popovers.
8. Disable **New** when the active session has no completed charges.
9. Session **start time = timestamp of its first charge**, not when New was tapped. A provisional New timestamp is acceptable until the first charge exists. Ensure picker/file/model semantics agree when reopening and appending to older sessions.
10. Add **Apt #** as a completed-charges table column **before Initial**. Historical charge entries should store their apartment at charge time, rather than deriving it from the currently loaded token. Missing apartments should render appropriately; decide backward compatibility for existing prototype session JSON.
11. Settings after an apartment secret is entered: **hide both password fields and Save**. Instead show the **secret hash** and a **right-aligned Clear button side-by-side**. Secret itself remains memory-only; do not show/persist plaintext. Choose/document a sensible hash presentation if not specified by user.

12. **Landscape apartment-popup layout bug:** the popup does not fit properly when it is first opened **after the app is already in landscape**. Opening the popup in portrait and then rotating to landscape flows correctly. Reproduce and test both presentation sequences separately; screenshots taken only by rotating an already-open popup do not cover this bug. Cover both charge and edit entry as appropriate.

## Existing app / deployed state

- Repo `/Users/itgeorge/elevator`, main branch `master`; last main commit at this handoff: `527b64c` (`docs: add final simulator UI evidence`), ahead of origin by 28 commits. All prior feature work committed; unrelated pre-existing untracked debug artifacts must remain untouched.
- Current features: calibration confirmation, file-backed unlimited charge sessions, active-session reopening, New/Load UI, memory-only apartment secret with confirmation, apt-v1 codec compatible with C#, decoded gray Apt display, combined charge/apartment and apartment-only editing, preservation/resealing on reset, X clears pending delta rather than setting total to zero.
- Apartment range 0–255. Input initially blank; entering 256 shows a small validation error and disables Save. No range placeholder.
- Secret changes on an already-loaded token trigger a targeted fresh apartment-capable snapshot where needed; matching block 4/5/6 retains pending changes, changed token resets them.
- Normal full Debug app is installed/launched on the real iPad with **no test flags or calibration bypass enabled**.
- Physical test token was intentionally changed with explicit user authorization: **Mercury, 0 rides, apartment 43**. Test secret: `rides-ui-e2e-test-2026`. This is a test secret, not a real operator secret; it must be reentered after app restart.
- Real bridge is left running on Mac via `dotnet run --project RidesBridge/RidesBridge.csproj -- --everyday`; listener `0.0.0.0:5080`, LAN URL `http://192.168.0.163:5080/`. Parent-owned launch PIDs were 45227/45237; recheck ownership/state before operations rather than trusting old PIDs.
- PM3 `/dev/cu.usbmodem11301` was connected; iPad unlocked during prior testing. User authorized treating this as a test token, unrestricted token mutations, test secret, and temporary unattended test calibration bypass. Recheck availability after compaction.
- Temporary Mac test bearer was revoked and `/tmp/rides-ui-physical-client.json` removed. iPad pairing retained. Never print/commit bearer credentials.

## Relevant implementation files

- `RidesTablet/Views/ApartmentEntrySheet.swift`: keypad, current Cancel/Skip/Save footer, current close X and Backspace, session loader. Currently `.interactiveDismissDisabled()`; follow-up requires replacing that behavior with explicit outside-dismiss semantics.
- `RidesTablet/Views/ContentView.swift`: charge action, current Apt display, history table, calibration and New confirmations (currently confirmationDialog).
- `RidesTablet/Views/BridgeConnectionView.swift`: secret-entry settings section; currently retains fields after entry plus Clear.
- `RidesTablet/Features/RidesViewModel.swift`: `CompletedCharge`, apartment popup/mutation state, Auto pause/drain/resume, session model integration.
- `RidesTablet/Features/ChargeSessionStore.swift`: `ChargeSession` (createdAt currently immutable), per-session atomic JSON plus active-session pointer. Storage directory `Library/Application Support/RidesTablet/ChargeSessions`.
- `RidesTablet/Domain/ApartmentSecretStore.swift`: memory-only exact UTF-8 secret; preserves entered whitespace.
- `RidesTablet/Domain/ApartmentBlockCodec.swift`: CryptoKit HMAC-SHA256 apt-v1 codec, bound to block 3.
- `RidesTablet/Bridge/NetworkRideTokenDevice.swift`: apartment-capable scans, apartment-only/combined conditional mutations, reset resealing. Apartment-only request currently includes block-3 expected=desired guard and block-4 write, and verifies response actual values.
- `RidesBridge/Page0BridgeOperations.cs`, `RidesBridge/Page0MutationSession.cs`, `RidesBridge/Pm3BridgeDeviceAdapter.cs`: investigate timing and preflight/native verification behavior here.
- `RidesTablet/Views/RidesRootView.swift`: fake screenshot fixtures and DEBUG-only physical bootstrap.
- `RidesTablet/Bridge/RidesUIPhysicalAcceptanceCoordinator.swift`: opt-in DEBUG bootstrap/direct real Detect (no fabricated calibration samples) and programmatic fallback acceptance. Neither runs on normal launches.
- Xcode project uses explicit file registration: `RidesTablet/RidesTablet.xcodeproj/project.pbxproj`.

## Tests / screenshot evidence from completed work

- **269 unit tests passed**.
- **13 simulator UI tests executed: 11 passed, 2 expected physical-only skips, 0 failures**.
- **Physical XCTest UI E2E passed**, 141.6 seconds, real iPad → live bridge → real PM3. It verified clear pending, secret entry, Cancel/Skip/Save, apartment-only edit preserving pending, New/Load then charging loaded session, actual app restart/secret reentry/session reopening, Venus→Mercury reset resealing, and calibration-confirmation cancel/confirm.
- Copied real iPad session files verified 3 charges, +3 rides, €0.09, two empty sessions retained, no secret plaintext in files. Fresh hardware read verified final token state.
- Receipt: `.docs/rides-ui-e2e-2026-11-18.json`.
- Screenshots (parent reviewed): `RidesTablet/Screenshots/ui-2026-11-18/` with physical main portrait/landscape, charge and edit keypad, settings, session picker/reopen, calibration confirmation, plus simulator empty-input / invalid-256 images.
- Progress record: `.docs/rides-ui-progress-2026-11-18.md`.
- Physical test: `RidesTabletUITests/PhysicalRidesUITests.swift`, gated by compile flag `RIDES_PHYSICAL_UI_WRITES` AND runner opt-in `TEST_RUNNER_RIDES_UI_PHYSICAL_TEST=1`. Default simulator runs skip it. Update old Backspace / close / confirmation selectors when implementing follow-ups.
- Simulator tests: `AutoScanUITests.swift`, `ApartmentValidationUITests.swift`; model/session tests in `ApartmentModelTests.swift`, `ChargeSessionModelTests.swift`, `ChargeSessionStoreTests.swift`, `DomainStateTests.swift`, `NetworkRideTokenDeviceTests.swift`.

## Operational lessons / commands

- **Always suppress Git editors** in agent worktrees: `git -c core.editor=true merge --continue`, `git merge --no-edit`, or explicit commit messages. Previous UI agent stalled 21 minutes in vi; resolved merge work was preserved.
- Physical XCTest works now. Add `-collect-test-diagnostics never` to avoid Xcode hanging finalizing device diagnostics after failures.
- Full physical-screen screenshots: **`XCUIScreen.main.screenshot()`**, not `XCUIDevice.screenshot()` (does not exist). Some native screenshots need lossless full-image rotation to normalize landscape; retain originals, no cropping/fabrication. Physical landscape screenshot is already valid.
- Real iPad CoreDevice ID `494BBD09-0DEB-5B41-B915-3B4258F1DBA2`; Xcode UDID `00008101-001A68EE21F0001E`; signing team `TJY5296P7S`; app bundle `com.itgeorge.RidesTablet`.
- Simulator `RidesTablet iPad Air 4`, ID `3F9DD641-D0EF-432D-ACEB-F8BE555DEBBA`.
- Device app-only build: `xcodebuild -project RidesTablet/RidesTablet.xcodeproj -scheme RidesTablet -configuration Debug -destination 'platform=iOS,id=00008101-001A68EE21F0001E' -derivedDataPath /tmp/RidesTabletFinalUI -allowProvisioningUpdates DEVELOPMENT_TEAM=TJY5296P7S PRODUCT_BUNDLE_IDENTIFIER=com.itgeorge.RidesTablet build`.
- For device test builds, bundle override must distinguish targets: pass literal `PRODUCT_BUNDLE_IDENTIFIER=com.itgeorge.$(PRODUCT_NAME:rfc1034identifier)` as one shell-quoted argument. Explicit physical test compile conditions: `SWIFT_ACTIVE_COMPILATION_CONDITIONS=DEBUG RIDES_PHYSICAL_UI_WRITES`.
- DEBUG `RIDES_UI_PHYSICAL_BOOTSTRAP=1` calls real Detect after address restoration, bypassing only initial operator calibration for an unattended test. Normal launch must omit it; normal calibration remains required. No fake signal calibration measurements were used.
- Original physical token backup (private): `/tmp/rides-ui-physical-e2e/original-token.json` (Earth, 200 rides). User allowed changes, so no restoration was requested/performed.
- Logs/artifacts if still present: `/tmp/rides-ui-physical-e2e-attempt3.log`, `/tmp/rides-ui-physical-e2e-attempt3.xcresult`, `/tmp/rides-ui-physical-e2e/`, `/tmp/rides-ui-final-simulator/`, `/tmp/rides-ui-physical-bridge.log`.

## Prior Luna worktrees / sessions (all settled)

- `.worktrees/rides-ui` — UI and simulator validation; `vigil-cdd54ab7-423e-4948-a48f-5a01c215e4f1`.
- `.worktrees/rides-apartment` — codec/model/network; `vigil-0b2ef2a5-befa-4c3e-9397-715dbecacc7d`.
- `.worktrees/rides-sessions` — file storage and tests; `vigil-a0c89926-1ad7-4de3-a924-419cd16e1251`.
- `.worktrees/rides-physical-e2e` — physical test scaffolding; `vigil-38b588d7-4eeb-4de1-b304-c861056f450b`.
- `.worktrees/rides-e2e-ops` — screenshots, final hardware verification, deployment, receipts; `vigil-42db023e-2c46-458a-ad55-bb6aff1c7696`.
- `.worktrees/rides-pending-hotfix` — isolated X-only build `c9576d1`, superseded by full app deployment.

All relevant prior commits have been integrated into main. Rebase/merge from current main for new work; avoid reintroducing stale worktree app code.
