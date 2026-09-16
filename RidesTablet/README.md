# RidesTablet

An iPad-only SwiftUI simulator prototype for non-technical token operators. It targets iPad Air 4 and uses **FakeProxmark only**; no hardware or production write path is included.

## Operator flow

1. Concept B is the default on a normal launch; use the small `Concept A / B` picker in the navigation bar to compare the prototypes.
2. Optionally open `SIMULATION` to choose a fake reader scenario.
3. Put a token on the reader and tap **Detect token**. Detect, tune, and read run as one flow. Signal is shown in millivolts so the token can be repositioned.
4. On a known read, the merged panel shows Current rides, the noneditable **PENDING** total, and EUR. The three minus controls on the left and plus controls on the right adjust pending rides by 1, 10, and 100; values stay within 0–500.
5. Concept A additionally shows a muted live equation inside the blue PENDING card: the signed delta followed by `=` (for example, `+ 150 =`; decreases use `− 20 =`). Concept B shows only the final PENDING total. The change is priced at €0.03 per ride. EUR is adjusted with the aligned `−€1.50` and `+€1.50` buttons; the round control on the EUR row rounds the signed delta to the nearest 50 rides (€1.50), with midpoints away from zero.
6. Tap **Charge token** only after a known token has changed. A successful fake write makes pending the new current count.
7. `RESET` is in the merged panel for known and unknown tokens and requires an explicit profile selection. The confirmation button is disabled until a profile is selected. Idle, working, no-chip, and failure states replace the controls with centered status content; unknown saves report `Unknown, logged`, while log failures remain errors.

The compact Apt # control row keeps disabled Cancel on the left and disabled Save on the right, with the read-only Block 4 value in the middle.

## Concepts

A and B share `RidesViewModel`, the responsive one-screen layout, and merged status/count panel:

- **A:** default layout plus the accessible live signed delta equation inside the blue PENDING card.
- **B (default):** the same layout, showing only the final PENDING total. Portrait uses the seamless Detect/signal strip, centered Current → PENDING → EUR controls, Apt row, and large Charge action. Landscape switches to a two-column reader/Apt column and count/status/Charge column.

## Simulation scenarios

`SIMULATION` is prototype tooling and is deliberately small: Good token, Weak signal, No chip, Unknown family, and Write failure. Unknown family reports exactly `Unknown, logged` only after the page-0 dump saves successfully. If logging fails, it reports `Unknown token — log failed` with the save error. No chip reports exactly `No T55xx chip detected. Place the token on the reader and try again.`

Unknown page-0 images are written as 32-byte big-endian `.bin` files under the app's `Library/Application Support/RidesTablet/UnknownDumps` directory.

## Build and test

From the repository root, run the full XCTest suite:

```sh
xcodebuild test \
  -project RidesTablet/RidesTablet.xcodeproj \
  -scheme RidesTablet \
  -destination 'platform=iOS Simulator,name=RidesTablet iPad Air 4'
```

For a local UI review, launch the app on the same iPad Air (4th generation) simulator, tap both concept picker options, and exercise the `SIMULATION` scenarios. Screenshots, when captured, belong in `RidesTablet/Screenshots/` as `concept-a.png` and `concept-b.png`.
