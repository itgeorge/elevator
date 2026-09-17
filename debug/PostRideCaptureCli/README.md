# PostRideCaptureCli

Hands-free helper for capturing page-0 ride blocks after elevator tests.

It watches LF tune for a >2 kV swing (token placed), waits 1 second for seating, reads block 0, and if that matches an expected elevator config (`00148040` / `00148041` by default) it reads mirrored blocks 5 and 6. Then it waits for removal and the next placement. Results are printed in color and appended to `post-ride-captures.log`.

## Run

Leave other PM3 tools disconnected, then:

```bash
dotnet run --project debug/PostRideCaptureCli -- --port /dev/cu.usbmodem1301
```

Useful options: `--threshold-mv`, `--settle-ms`, `--expected-block0`, `--log`.

## Example session

```text
➜  ~/elevator git:(ipad-rides) ✗ dotnet run --project debug/PostRideCaptureCli -- --port /dev/cu.usbmodem1301
[18:54:15] PostRideCaptureCli — tune-gated block 5/6 capture
[18:54:15] threshold=2000 mV  settle=1s  expected blk0=[00148040, 00148041]
[18:54:15] log=/Users/itgeorge/elevator/post-ride-captures.log
[18:54:15] Ctrl-C to stop.

[18:54:15] Connecting (/dev/cu.usbmodem1301)...
[18:54:15] Connected.
[18:54:15] Baseline tune: 70159 mV — place a token when ready.
[18:54:15] Waiting for placement... (ref 70159 mV, Δ>2000)
[18:54:21] Tune edge: 70159 → 66919 mV (Δ3240)
[18:54:21] Placement edge at 66919 mV — settling 1s...
[18:54:22] Post-settle tune: 60018 mV
[18:54:22] [1] Reading block 0...
[18:54:23] [1] Block 0 OK: 00148041
[18:54:23] [1] Reading blocks 5 and 6...
[18:54:24] [1] blk5=8A124BE2  blk6=8A124BE2  mirrored=yes
[18:54:24] Waiting for removal...
[18:54:27] Tune edge: 60018 → 69927 mV (Δ9909)
[18:54:27] Removal edge at 69927 mV
[18:54:28] Empty baseline: 70142 mV — place next token.
...
[18:54:49] [3] Reading block 0...
[18:54:50] [3] Unexpected block 0: 26BBE9EF (expected 00148040|00148041)
...
[18:55:29] [7] Reading block 0...
[18:55:30] [7] Block 0 OK: 00148040
[18:55:30] [7] Reading blocks 5 and 6...
[18:55:31] [7] blk5=FEC77436  blk6=FEC77436  mirrored=yes
[18:55:58] Stopped.
```

A bad seat usually shows up as an unexpected block 0; remove and replace to retry. Ctrl-C stops cleanly.
