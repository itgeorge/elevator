import XCTest
@testable import RidesTablet

final class ResetSequenceTests: XCTestCase {
    func testEveryOperatorResetStartsAtZeroRides() {
        for profile in ResetSequence.all {
            let image = profile.resetImage()
            XCTAssertEqual(profile.resetRideCount, 0, profile.sequence.rawValue)
            XCTAssertEqual(image[5], profile.sequence.zeroBlock, profile.sequence.rawValue)
            XCTAssertEqual(image[6], profile.sequence.zeroBlock, profile.sequence.rawValue)
            XCTAssertEqual(Array(image[1...4]), profile.identityBlocks, profile.sequence.rawValue)
            XCTAssertEqual(image[0], profile.block0, profile.sequence.rawValue)
            XCTAssertEqual(image[7], profile.block7, profile.sequence.rawValue)
        }
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
