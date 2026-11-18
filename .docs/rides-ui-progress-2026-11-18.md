# Rides UI work in progress — 18 Nov 2026

## Deployed hotfix (separate from feature work)

- User requested an X-only deployment before pausing.
- Branch `fix/pending-reset-only`, commit `c9576d1`, worktree `.worktrees/rides-pending-hotfix`, based on pre-feature `d22141d`.
- Runtime change: `clearPendingRides()` restores `currentRides`, plus corrected accessibility label. Tests and README updated.
- Native Swift check against actual model/fake reader passed: 200 + 30 → 200 + 0; negative deltas also clear; zero EUR; no writes; unloaded no-op.
- Signed iPad build passed; installed and launched `com.itgeorge.RidesTablet` on iPad `494BBD09-0DEB-5B41-B915-3B4258F1DBA2` successfully at 18:45. Build `/tmp/RidesTabletPendingHotfix`, log `/tmp/rides-pending-hotfix-build.log`.
- This deployment does NOT include the apartment/session features. Running bridge was not stopped, restarted, or modified. No real token mutation was performed.

## Larger feature work (NOT yet accepted/deployed)

- User decisions: apartments 0–255; secret memory-only; apartment editing writes apartment only and preserves pending; reset preserves/reseals apartment; no old-history migration; no 100-entry cap; reopen active session; session picker date/time + count + EUR.
- Main branch includes initial implementations and tests from Luna worktrees. Session files live under app Application Support/RidesTablet/ChargeSessions.
- Main unit suite has NOT yet been run after integration. The unchanged pre-feature baseline passed 244 tests.
- Luna UI agent (`vigil-cdd54ab7-423e-4948-a48f-5a01c215e4f1`) is still working in `.worktrees/rides-ui` and owns the simulator lock. Do not run another simulator check concurrently.
- UI branch merged master through `02447a4`; parent has later fixes/test registrations through `a48ed60`. Merge latest main before final validation.
- UI branch has uncommitted View/root/test fixes and an iOS-safe ChargeSessionStore fallback (parent already committed same fallback on main). Commit/cherry-pick remaining UI corrections before testing main.
- First UI run: `/tmp/rides-ui-review/test.log`, result `/tmp/rides-ui-visual-build/Logs/Test/Test-RidesTablet-2026.11.18_18-38-35-+0200.xcresult`. 11 tests, one physical-only skipped, three failures (confirmation Cancel selector, secret fields offscreen, ambiguous Restart calibration selector). Agent iterating.

## Screenshot review so far

Parent exported first-run attachments to `/tmp/rides-ui-first-review` and inspected:
- `CA94E673-CE37-43B5-8BB0-9027EC94475E.png`: apartment 52 displayed gray at top-right; main portrait layout looks reasonable.
- `A998FD65-3139-46CD-A5F7-1CD8694D0F9A.png` and `7D06714A-95FA-4FA3-8145-FDC76F244B6C.png`: keypad screenshots are clipped! Apartment title and Cancel/Skip/Save row not visible in captured sheet. Need adequate fixed/minimum presentation sizing and fresh captures, not acceptance based on UI accessibility alone.
- `9F37634F-F8A9-4B77-89B1-E8A06631343C.png`: landscape captured during rotation (sideways/clipped), not usable layout evidence. Wait for settled landscape geometry before capture. Its history fixture also lacks apartment, so require an apartment-visible landscape screenshot.
- Need final screenshots of secret settings, session picker, both confirmations, and unclipped charge/edit keypad with visible actions.

## Next steps

1. Finish UI agent iteration; send above visual feedback when agent settles (Vigil send only continues a waiting child).
2. Integrate its View/root/test corrections, merge latest main, run all unit tests and focused UI tests serially.
3. Confirm codec vectors, combined apartment+charge, apt-only edit preserving pending, reset resealing, file-session save/reload, and Auto restoration after popup.
4. Do not deploy larger features until user is ready; installed iPad must remain on X-only hotfix for now.
