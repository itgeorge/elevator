# Auto-detect prototype — 2026-10-17

## Operator behavior

- The reader strip has equal-width, flat **Detect** and **Auto** controls with a shared vertical divider. Detect remains blue; Auto is neutral-colored, with a straight right edge and divider where it meets the signal panel. Only the exposed outer leading corners of the control group are rounded. Auto is off initially. A hollow diode means off, yellow means an active scan cycle, and green means enabled between cycles. Auto now matches Detect’s title-sized font. The diode body is 18 pt with a softly blurred glow approximately 27 pt across at its largest transition pulse. Colors fade over roughly 0.22 seconds; only the diode/glow animates, with no changing Auto icons or button-scale/background pulses. Reduce Motion suppresses expansion and uses shorter color fades.
- Detect turns Auto off, cancels/drains its in-flight work, and does a single read. Initial manual calibration steps remain available before a token has been loaded. Auto can start only after both calibration steps succeed. Auto’s voltage eligibility cutoff is separate from token decoding; forced manual Detect is not restricted by it.
- Auto first shows **Hold token steady / Waiting for signal to settle…**, then **Detecting… / Reading token…** only during the actual full read. Known controls remain visible during background quality checks.
- Auto preserves pending edits when a full read confirms the same observed snapshot (block 4, mirrors 5/6, decoded sequence/count). A changed snapshot is adopted as a new read. Read-confirmed absence clears the loaded token and pending count.
- Unknown snapshots still use the normal full-dump workflow, but an unchanged unknown presentation is not repeatedly saved on every confirmation.
- A failed full confirmation marks the loaded snapshot stale and disables charging until it is confirmed again; pending edits are retained. Poll results owned by a stopped/older generation cannot update the workflow.
- If Auto was on, Charge/Reset pauses/drains polling before the explicit mutation, performs one full refresh after a successful write, then resumes. Explicitly stopping Auto during that operation prevents resumption. There are no automatic write retries.
- Auto stops when the view disappears or the app leaves the active state. Turn it on again when returning.
- The sliders icon on the right of the signal panel restarts calibration: stop/drain Auto, clear measured bounds and the current/pending view, preserve completed-charge history, and return to the empty-reader step. It is disabled during busy operations and performs no token writes. The completed calibration state now explicitly says **Calibration complete / Tap Detect or enable Auto**.

## Polling and PM3 timing

The bridge adds authenticated `GET /api/v1/hardware/signal`, an eight-sample LF tune with a one-second tune bound. It performs no T55 detection, block read, or write, and uses the existing hardware gate and adapter lock. The native mutation-session optimization is unchanged.

The client targets **100 ms start-to-start** while searching/positioning or after read-confirmed absence, and **3 seconds** after a known or unknown token has been read-confirmed. Signal-blocked polling also uses the fast interval. Cycles never overlap or queue catch-up polls. A failed confirmation preserves the last confirmed presence instead of assuming removal. For initial/unconfirmed placement, eligible probes accumulate across the whole candidate episode. The implementation retains a running count/minimum/maximum, so earlier movement remains part of the spread without unbounded raw-sample storage. At least three samples and a spread of at most 10% of the calibration span are required for an early settled read. There is no last-three sliding window. A five-second fallback arms only when quality first reaches at least 0.5 (the better half), stays latched through eligible movement, and can retry an unsuccessful/unconfirmed attempt. It never bypasses the high cutoff. Detection attempts reset the accumulated sequence; high-cutoff, Auto stop and calibration restart also clear timing/cooldown state. Confirmed known and unknown tokens still have no periodic full reads; their existing significant-worsening policy remains. The edge threshold is 15% of the calibration span with a 1,000 mV minimum, or 1,500 mV without calibration. Worsening uses the calibrated direction, or a learned empty/present direction, with the native-reader default that higher raw millivolts means weaker coupling. A successful degradation check adopts the triggering worse probe value as baseline; stable weak placement therefore does not cause repeated reads. Manual detection and post-write refresh explicitly re-establish the baseline. Before any Auto full scan (including initial, retry, or empty fallback), calibrated signal quality is checked. Quality is `(signal - empty) / (present - empty)`: reads block at quality ≤ 0.15 and resume at ≥ 0.20. For the normal PM3 direction this is the upper 15% raw-voltage exclusion band, with recovery below the upper 20% boundary. The same quality calculation supports reversed calibration directions. For example, empty 70,000 mV and placed 63,000 mV block at ≥68,950 mV and resume at ≤68,600 mV.

While blocked, only lightweight signal polling continues. The panel shows **Signal too weak / Reposition the token**, not a claimed read-confirmed absence. Loaded data and pending edits are retained internally but cannot be charged until a fresh eligible read succeeds. Recovery confirms immediately before re-enabling the controls. Manual Detect bypasses this Auto-only gate.

The original physical token-present benchmark measured median eight-sample tune time **296.966 ms** and full scan time **1,905.941 ms**. A later native optimization avoids an unnecessary 250 ms RF wait between pure tune/version commands while retaining the guard before actual T55 operations. The new unloaded-reader benchmark measured median probes **50.605 ms** and actual start intervals **100.541 ms** ([receipt](pm3-fast-signal-benchmark-2026-10-17.json), [implementation/safety notes](pm3-fast-signal-probes-2026-10-17.md)). Full confirmations naturally take longer than a signal polling interval, and no-chip confirmation can take longer still. Raw timing receipt: [`pm3-auto-scan-benchmark-2026-10-16.json`](pm3-auto-scan-benchmark-2026-10-16.json).

No server-side reader/session ID is required for this prototype. Client task generations handle stale responses; the server's short exclusive hardware scopes serialize requests. A future watcher/session ID must not authorize reusing a detected tag configuration across removal or replacement.

## Validation

- Initial full simulator run: 222 unit tests passed; UI tests cover preserving pending edits while polling, manual Detect stopping Auto, and portrait/landscape control availability. A dedicated fake-reader UI test also confirms removal and new placement through the simulation menu. Simulator tests use only FakeProxmark and an isolated history store.
- Non-hardware .NET suite: 727 passed, 1 skipped; warnings-as-errors build succeeded with zero warnings/errors.
- Physical iPad UI automation was attempted, but its XCTest runner timed out enabling automation before test actions ran. This is not reported as a passing physical UI test.
- A separate opt-in, DEBUG-only in-app smoke checked the **real iPad model → saved-pairing bridge → native PM3** read-only path. It loaded 250 rides, adjusted pending to 251 without charging, preserved 251 over 12 seconds of polling and a periodic confirmation, then ran manual Detect, which stopped Auto and restored pending to the actual 250. Bridge logs showed 15 signal requests and three full scans, all HTTP 200, with zero mutation requests. Machine receipt: [`pm3-auto-scan-physical-smoke-2026-10-17.json`](pm3-auto-scan-physical-smoke-2026-10-17.json).
- No physical Charge, Reset, or token write was performed. The read-only benchmark snapshots remained unchanged.

The device smoke is opt-in via `RIDES_AUTO_SCAN_READ_ONLY_SMOKE=1` when launching the DEBUG app with `devicectl`. With the calibration requirement added, it reports a clear skip if calibration is not ready; it does not invent calibration samples or bypass the gate. The earlier physical receipts above predate this eligibility requirement. It writes `Documents/auto-scan-read-only-smoke.json`, stops Auto after the check, and never calls a mutation API. It is not enabled during ordinary app launch.

## Remaining operator checks / prototype limits

- Physically remove and replace tokens, reposition weak tokens, and try rapid swaps. Those hardware transitions could not be performed while the operator was away; their UI/state transitions were exercised with simulated presence/read results instead.
- Review the pulse style and layout on the actual iPad. Native UI test screenshots are retained in the local `.xcresult` bundles under `/tmp/RidesAutoScanDerived/Logs/Test/`.
- Different physical tokens with identical observed block-4/mirror payloads cannot be distinguished if no removal or other observable change is seen. This is snapshot reconciliation, not a globally unique token ID.
- App backgrounding intentionally stops Auto. No background reader service or push infrastructure was added.
- A client started against an older bridge without the new signal endpoint must use manual Detect or restart with the updated bridge.

## Indicator / cadence refinement validation

224 unit tests and four simulator UI tests passed (one physical-only UI test skipped). Added checks cover equal button widths, stable Auto text, fast initial/absent cadence, slower known and unknown presence, and injected interval values. Existing simulated removal/replacement and pending-preservation checks still pass. The updated app was installed and launched on the iPad; the existing bridge was left running. No token writes were performed during this refinement.

## Detecting status, probe-only occupied mode, and diode glow

228 unit tests and five simulator UI tests passed (one physical-only UI case skipped). Added checks prove no periodic known/unknown reads, worsening-only rereads, worse-baseline adoption without repeated reads, calibrated direction handling, and transient detection/cancellation. A bounded fake scan-delay fixture enables an actual UI assertion that Detecting… remains visible until the read completes.

The updated physical iPad read-only smoke loaded 442 rides, preserved pending 443 over 12 seconds, and restored 442 on forced Detect. The server observed five lightweight signal probes and exactly two full scans: initial confirmation and the final explicit Detect, with no occupied-period rereads or mutation requests. Receipt: [`pm3-auto-scan-idle-read-smoke-2026-10-17.json`](pm3-auto-scan-idle-read-smoke-2026-10-17.json). This validates the live app/model path, not physical UI automation. The updated app was deployed and the bridge left running.

## Calibration eligibility and weak-signal cutoff

Auto now requires both successful calibration steps in the current app session. It blocks the upper 15% of the normal PM3 calibrated raw-voltage range and resumes only outside the upper 20%, with direction-aware quality math as described above. No Auto full scan is issued while a probe is blocked; lightweight polling continues at the fast cadence. Weak-signal status hides stale ride controls and prevents charging. A fresh recovery read is mandatory before restoring controls, while same-snapshot pending edits survive. Forced Detect remains outside the Auto-only gate.

230 unit tests and six simulator UI tests passed (one physical-only UI case skipped). Coverage includes incomplete calibration, exact 15% cutoff, the 15–20% hysteresis band, zero reads while blocked, pending retention/recovery, forced manual bypass, and the actual two-step calibration UI. The updated iPad app was built and deployed; fresh physical Auto testing now requires the operator to perform calibration rather than bypassing it in a test hook. No token writes were performed; the existing bridge remains running.

## Accumulated settling / fast probe validation

240 unit tests and seven simulator UI tests passed (one physical-only UI case skipped). Tests cover five-sample minimums, whole-episode outlier retention, lower-half samples, the five-second clock-controlled fallback, retry/deadline latching, cutoff/reset behavior, recalibration preserving history, and the final completion wording. Simulator UI fixture changes pause Auto before opening the debug scenario menu to avoid XCTest waiting on perpetual 10 Hz visual updates; continuous weak-to-good recovery is covered at model level.

The native warnings-as-errors build passed, and the non-hardware .NET suite passed 736 tests with one skipped. The physical 100 ms timing probe was read-only and found no readable token before/after. The app was deployed and the updated bridge left running. Current physical placement/settling behavior still needs operator calibration and hands-on review; no calibration or token writes were fabricated for this test.

## Settling tolerance adjustment

Settling now accepts **three samples** with a whole-episode spread **≤10%** of calibration span, normally spanning about 200 ms at the 100 ms target cadence. Accumulation, the five-second fallback, cutoff and reset behavior are unchanged. Tests cover the inclusive 10% boundary, rejection just above it, the three-sample minimum and retention of an earlier outlier beyond three later flat samples.

242 unit tests and seven simulator UI tests passed (one physical-only UI case skipped). The first combined run hit an unrelated Bonjour reconnect timing assertion; the complete unit suite passed on rerun without changes to discovery code. The signed app was rebuilt, installed and launched on the iPad. No physical token writes were performed.
