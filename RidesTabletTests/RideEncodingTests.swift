import XCTest
@testable import RidesTablet

final class RideEncodingTests: XCTestCase {
    func testMercuryZeroAndMaximumMatchCliConvention() {
        XCTAssertEqual(RideSequence.mercury.encode(0), 0xCCC749CC)
        XCTAssertEqual(RideSequence.mercury.encode(500), 0x3FC6BD93)
        XCTAssertEqual(RideSequence.neptune.encode(0), 0x8F1249B0)
    }

    func testDecodeRejectsUnknownAndOutOfRangeBlocks() {
        XCTAssertEqual(RideSequence.saturn.decode(0x8B12C970), 128)
        XCTAssertNil(RideSequence.mercury.decode(0xDEAD1234))
        XCTAssertNil(RideSequence.mercury.decode(0x3FC6BC83))
    }
}
