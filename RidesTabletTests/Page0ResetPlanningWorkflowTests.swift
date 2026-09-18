import XCTest
@testable import RidesTablet

final class Page0ResetPlanningWorkflowTests: XCTestCase {
    func testMirrorsOnlyResetTargetsOnlyChangedFiveAndSix() throws {
        let venus = ResetSequence.for(.venus)
        let image = venus.resetImage()
        let current = try blocks(
            b1: venus.block1,
            b2: venus.block2,
            b3: venus.block3,
            b4: venus.block4,
            b5: 0xBBC7FD03,
            b6: 0xBBC7FD03
        )

        let planned = try Page0ResetPlanningWorkflow.planMutations(currentBlocks: current, profile: venus)
        XCTAssertEqual(planned.map(\.block), [5, 6])
        XCTAssertEqual(planned.map(\.expected), ["BBC7FD03", "BBC7FD03"])
        XCTAssertEqual(planned.map(\.desired), [format(image[5]), format(image[6])])
    }

    func testIdentityMismatchTargetsChangedBlocksAmongOneThroughSix() throws {
        let venus = ResetSequence.for(.venus)
        let image = venus.resetImage()
        let current = try blocks(
            b1: 0x21FF0031,
            b2: venus.block2,
            b3: venus.block3,
            b4: venus.block4,
            b5: 0xBBC7FD03,
            b6: 0xBBC7FD03
        )

        let planned = try Page0ResetPlanningWorkflow.planMutations(currentBlocks: current, profile: venus)
        XCTAssertEqual(planned.map(\.block), [1, 5, 6])
        XCTAssertEqual(planned.first?.desired, format(image[1]))
    }

    func testAlreadyAppliedProfileProducesNoMutations() throws {
        let venus = ResetSequence.for(.venus)
        let image = venus.resetImage()
        let current = try blocks(
            b1: image[1], b2: image[2], b3: image[3], b4: image[4],
            b5: image[5], b6: image[6]
        )

        let planned = try Page0ResetPlanningWorkflow.planMutations(currentBlocks: current, profile: venus)
        XCTAssertTrue(planned.isEmpty)
    }

    private func blocks(
        b1: UInt32, b2: UInt32, b3: UInt32, b4: UInt32, b5: UInt32, b6: UInt32
    ) throws -> [BridgePage0BlockValue] {
        try BridgePage0Blocks1To6Response(blocks: [
            try makeBlock(1, b1),
            try makeBlock(2, b2),
            try makeBlock(3, b3),
            try makeBlock(4, b4),
            try makeBlock(5, b5),
            try makeBlock(6, b6),
        ]).blocks
    }

    private func makeBlock(_ block: Int, _ word: UInt32) throws -> BridgePage0BlockValue {
        let json = #"{"block":\#(block),"value":"\#(format(word))"}"#
        return try JSONDecoder().decode(BridgePage0BlockValue.self, from: Data(json.utf8))
    }

    private func format(_ word: UInt32) -> String {
        String(format: "%08X", word)
    }
}
