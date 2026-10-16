# Fast PM3 signal probes — 2026-10-17

The original eight-sample signal benchmark took approximately 297 ms. About 250 ms was an RF-settle wait applied to every command after tune, including the next tune or connection-health/version request.

The native executor now skips that wait only for pure tune/version operations. The tune-active flag survives those operations; the next T55 detect/read/write/dump or other non-whitelisted command still consumes the existing RF-settle guard. Native write settling, internal readback verification and mutation transaction boundaries are unchanged.

Cancellation/error cleanup was also tightened: tune always attempts RF shutdown in `finally` after initialization. Only shutdown uses an uncanceled token, still bounded by the existing one-second transport timeout; cleanup failure does not hide the original error.

## Physical read-only benchmark

25 eight-sample adapter signal probes, paced at a 100 ms target:

- Median probe duration: **50.605 ms**.
- 95th-percentile duration: **52.208 ms**.
- Maximum duration: **52.951 ms**.
- Median actual start-to-start interval: **100.541 ms**.
- Signal range: **70,391–70,442 mV**.

No readable token was present: the diagnostic full scans before and after the measurements both returned `NoChip`. No write or reset APIs were called. This validates the unloaded-reader timing, not physical placement/removal behavior. See the [machine-readable receipt](pm3-fast-signal-benchmark-2026-10-17.json).

This makes a non-overlapping 100 ms client search/positioning target practical on this Mac/PM3. Network latency or a full confirmation scan may stretch a cycle; the scheduler must not queue catch-up polls. Occupied polling remains separately paced.

## Validation

`dotnet build ElevatorTokens.sln --no-restore -warnaserror` passed with zero warnings/errors. The non-hardware .NET suite passed **736 tests**, with **1 skipped** and no failures. New tests cover the tune/version whitelist, retained guards for T55/other commands, cancellation shutdown and preservation of the original failure when cleanup also fails.
