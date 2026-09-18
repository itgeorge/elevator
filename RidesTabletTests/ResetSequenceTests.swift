import XCTest
@testable import RidesTablet

final class ResetSequenceTests: XCTestCase {
    func testMercuryResetUsesFiveHundredRidesInMirrors() {
        let image = ResetSequence.for(.mercury).resetImage()
        XCTAssertEqual(image[5], RideSequence.mercury.encode(500))
        XCTAssertEqual(image[6], RideSequence.mercury.encode(500))
    }

    func testNeptuneResetIncludesBlock4AndWritesRideMirrors() {
        let image = ResetSequence.for(.neptune).resetImage()
        XCTAssertEqual(image, [
            0x00148040, 0x8BFE002A, 0xF100C6A2, 0x95D15917,
            0x95D15917, 0x8F1249B0, 0x8F1249B0, 0x57F674C3
        ])
        XCTAssertEqual(Array(ResetSequence.for(.neptune).writableBlocks), [1, 2, 3, 4, 5, 6])
    }
}
