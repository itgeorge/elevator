# Auto-detect prototype — 2026-10-17

## Operator behavior

- The reader strip has **Detect** and **Auto** controls. Auto is off initially. Its blue background pulses while enabled, with stronger feedback during a cycle; Reduce Motion disables the movement.
- Detect turns Auto off, cancels/drains its in-flight work, and does a single read. Initial manual calibration steps remain available before a token has been loaded. Auto can start without calibration; calibration is a meter reference, never a token-acceptance gate.
- Auto preserves pending edits when a full read confirms the same observed snapshot (block 4, mirrors 5/6, decoded sequence/count). A changed snapshot is adopted as a new read. Read-confirmed absence clears the loaded token and pending count.
- Unknown snapshots still use the normal full-dump workflow, but an unchanged unknown presentation is not repeatedly saved on every confirmation.
- A failed full confirmation marks the loaded snapshot stale and disables charging until it is confirmed again; pending edits are retained. Poll results owned by a stopped/older generation cannot update the workflow.
- If Auto was on, Charge/Reset pauses/drains polling before the explicit mutation, performs one full refresh after a successful write, then resumes. Explicitly stopping Auto during that operation prevents resumption. There are no automatic write retries.
- Auto stops when the view disappears or the app leaves the active state. Turn it on again when returning.

## Polling and PM3 timing

The bridge adds authenticated `GET /api/v1/hardware/signal`, an eight-sample LF tune with a one-second tune bound. It performs no T55 detection, block read, or write, and uses the existing hardware gate and adapter lock. The native mutation-session optimization is unchanged.

The client targets **750 ms start-to-start** for non-overlapping signal cycles. It never queues catch-up polls. Full confirmation runs initially, after two meaningful signal-edge probes, or after an eight-second fallback. The edge threshold is 15% of the calibration span with a 1,000 mV minimum, or 1,500 mV without calibration. These are confirmation hints, not acceptance thresholds; the periodic fallback permits reads outside the calibrated range or without a strong edge. Failures back off rather than starting overlapping requests.

With a physical token present, the read-only benchmark measured median eight-sample tune time **296.966 ms** and full scan time **1,905.941 ms**. Full confirmations naturally take longer than a signal polling interval, and no-chip confirmation can take longer still. Raw timing receipt: [`pm3-auto-scan-benchmark-2026-10-16.json`](pm3-auto-scan-benchmark-2026-10-16.json).

No server-side reader/session ID is required for this prototype. Client task generations handle stale responses; the server's short exclusive hardware scopes serialize requests. A future watcher/session ID must not authorize reusing a detected tag configuration across removal or replacement.

## Validation

- Full simulator run: 222 unit tests passed; UI tests cover preserving pending edits while polling, manual Detect stopping Auto, and portrait/landscape control availability. A dedicated fake-reader UI test also confirms removal and new placement through the simulation menu. Simulator tests use only FakeProxmark and an isolated history store.
- Non-hardware .NET suite: 727 passed, 1 skipped; warnings-as-errors build succeeded with zero warnings/errors.
- Physical iPad UI automation was attempted, but its XCTest runner timed out enabling automation before test actions ran. This is not reported as a passing physical UI test.
- A separate opt-in, DEBUG-only in-app smoke checked the **real iPad model → saved-pairing bridge → native PM3** read-only path. It loaded 250 rides, adjusted pending to 251 without charging, preserved 251 over 12 seconds of polling and a periodic confirmation, then ran manual Detect, which stopped Auto and restored pending to the actual 250. Bridge logs showed 15 signal requests and three full scans, all HTTP 200, with zero mutation requests. Machine receipt: [`pm3-auto-scan-physical-smoke-2026-10-17.json`](pm3-auto-scan-physical-smoke-2026-10-17.json).
- No physical Charge, Reset, or token write was performed. The read-only benchmark snapshots remained unchanged.

The device smoke is opt-in via `RIDES_AUTO_SCAN_READ_ONLY_SMOKE=1` when launching the DEBUG app with `devicectl`. It writes `Documents/auto-scan-read-only-smoke.json`, stops Auto after the check, and never calls a mutation API. It is not enabled during ordinary app launch.

## Remaining operator checks / prototype limits

- Physically remove and replace tokens, reposition weak tokens, and try rapid swaps. Those hardware transitions could not be performed while the operator was away; their UI/state transitions were exercised with simulated presence/read results instead.
- Review the pulse style and layout on the actual iPad. Native UI test screenshots are retained in the local `.xcresult` bundles under `/tmp/RidesAutoScanDerived/Logs/Test/`.
- Different physical tokens with identical observed block-4/mirror payloads cannot be distinguished if no removal or other observable change is seen. This is snapshot reconciliation, not a globally unique token ID.
- App backgrounding intentionally stops Auto. No background reader service or push infrastructure was added.
- A client started against an older bridge without the new signal endpoint must use manual Detect or restart with the updated bridge.
