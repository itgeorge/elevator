# PM3 mutation speed-up — 2026-10-16

## Implemented

The native bridge mutation path now opens one exclusive, freshly detected session for the whole conditional mutation: preflight, verified writes, and any recovery. It ends after the request; detection is not reused from a previous Detect button press or across separate transactions.

The native writer already reads back and compares each programmed block before returning success. The bridge now consumes that verified success instead of issuing another read. This is native-only: transports without the optimized provider retain the explicit write/readback compatibility path.

Normal successful two-block Charge operation counts:

| Operation | Before | After |
|---|---:|---:|
| Fresh T55 detects | 6 | 1 |
| Preflight block reads | 2 | 2 |
| Native verified writes | 2 | 2 |
| Additional bridge verification reads | 2 | 0 |
| Connection checks | Per block operation | Once on session open |

The native verified writes still perform two internal block readbacks in total, including the native reader's existing sample/consensus behavior. Thus logical block-read operations decrease from six to four; these counts are not counts of individual RF acquisitions. Write settling delays, native verification/retries, and read sampling are unchanged.

Preflight still reads every target before any write. Conflicts and already-applied requests do not write. Errors dirty the detected state; recovery can reconnect and redetect with its independent bounded cancellation token. Disposal ends the scope and releases the adapter lock. Blocks 0 and 7 remain forbidden mutation targets.

## Measurement and checks

`Page0ConditionalWriter` logs session-open, preflight, write/verify, recovery and total/outcome timings through ILogger. Logs contain durations and counts, not raw token blocks or pairing credentials.

- `dotnet build ElevatorTokens.sln --no-restore -warnaserror`: zero warnings/errors.
- Full non-hardware .NET tests: 726 passed, 1 skipped, 0 failed.
- New tests cover optimized read/write/detection/ping counts, new detection per subsequent transaction, no duplicate raw native read/detect after a verified write, non-native fast-path rejection, conflict/already-applied behavior, malformed or mismatched verified values, and rollback using a distinct uncanceled token after the original token was canceled.
- No physical writes or timing benchmark were performed during implementation. Actual speed improvement remains to be measured with an approved test token.
- HTTP mutation contract is unchanged; the installed iPad app does not need redeployment for this optimization. The bridge was rebuilt and left stopped. Start normally with `dotnet run --project RidesBridge/RidesBridge.csproj -- --everyday`.

## Future automatic detection (not implemented in this change)

Existing reference: [`debug/PostRideCaptureCli/Program.cs`](../debug/PostRideCaptureCli/Program.cs). Its `RunAsync` / `WaitForDeltaAsync` loop records an empty-antenna tune baseline, waits for an absolute change (default 2,000 mV), settles after placement (default one second), and confirms with block-0 and mirror 5/6 reads. It then waits for another tune edge as removal, settles for 400 ms, and refreshes the empty baseline. It alternates expected edges; it is not a complete hysteresis/debounce classifier and can mistake repositioning for an edge.

For an iPad prototype, use non-overlapping client polling and an explicit empty / candidate / read-confirmed-present / removal-candidate state. Tune changes are hints; successful reads confirm a supported token. Failed reads during movement are not by themselves proof of removal. Use separate enter/exit thresholds or short stability windows based on observed signals. Unknown-but-readable tokens must still reach the unknown-dump workflow.

Keep the long-lived watcher separate from short exclusive mutation sessions. A watch ID or presence-generation number can correlate poll responses with the current placement, but must not authorize reusing detected tag configuration after removal or reconnect. A simple client-side generation/revision may be enough before adding a server watch ID.

Pause/invalidate outstanding poll results during Charge/Reset; the hardware operation gate must prevent tune from interleaving with writes. After successful Charge or Reset, immediately perform a full scan as a natural UI refresh/readback. Do not let an older in-flight poll restore pre-write counts. Keep charging/resetting explicit and never retry writes automatically after a network timeout. Polling cadence and extra post-write read latency should be measured rather than chosen as an aggressive fixed timer.
