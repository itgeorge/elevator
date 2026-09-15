# RidesTablet

An iPad-only SwiftUI simulator prototype for non-technical token operators. It targets iPad Air 4 and uses **FakeProxmark only**; no hardware or production write path is included.

## Operator flow

1. Concept B is the default on a normal launch; use the small `Concept A / B / C` picker in the navigation bar to compare the prototypes.
2. Optionally open `SIMULATION` to choose a fake reader scenario.
3. Put a token on the reader and tap **Detect token**. Detect, tune, and read run as one flow. Signal is shown in millivolts so the token can be repositioned.
4. Adjust **Pending rides** with the three large minus controls on the left or plus controls on the right: 1, 10, and 100. The current ride count is read-only.
5. The change is priced at €0.03 per ride. The €1.50 buttons change 50 rides. A negative amount is labeled `Refund / decrease`.
6. Use the two separate round controls: the icon immediately below Pending rides rounds the target to the nearest 50 rides; the icon immediately below the EUR cost rounds the signed delta to the nearest 50 rides (€1.50). Midpoints round away from zero, and all results clamp to 0–500.
7. Tap **Charge token** only after a known token has changed. A successful fake write makes pending the new current count.
8. `RESET` requires an explicit profile selection. The confirmation button is disabled until a profile is selected.

The compact Apt # control row keeps disabled Cancel on the left and disabled Save on the right, with the read-only Block 4 value in the middle.

## Concepts

A, B, and C share `RidesViewModel` and all controls; they only change visual hierarchy:

- **A:** status first, then Detect beside the ride editor.
- **B (default):** a seamless Detect/signal strip, centered Current-over-Pending ride editing area, centered Apt row, and full-width Charge action below.
- **C:** split reader/status column beside the large ride editor.

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

For a local UI review, launch the app on the same iPad Air (4th generation) simulator, tap each concept picker option, and exercise the `SIMULATION` scenarios. Screenshots, when captured, belong in `RidesTablet/Screenshots/` as `concept-a.png`, `concept-b.png`, and `concept-c.png`.
