import XCTest
@testable import RidesTablet

@MainActor
final class DomainStateTests: XCTestCase {
    func testReadKnownTokenPublishesKnownStateIncludingBlock4() async {
        let token = Token.sample(rideCount: 73, sequence: .neptune)
        let model = RidesViewModel(device: FakeProxmark(read: .known(token)))

        await model.read()

        guard case .known(let loaded) = model.state else {
            return XCTFail("Expected known state")
        }
        XCTAssertEqual(loaded.rideCount, 73)
        XCTAssertEqual(loaded.block4, 0x95D15917)
    }

    func testNoChipIsDistinctFromUnknown() async {
        let model = RidesViewModel(device: FakeProxmark(read: .noChip))

        await model.read()

        XCTAssertEqual(model.state, .noChip)
        XCTAssertNil(model.lastDumpURL)
    }

    func testUnknownReadSavesEightBlockDump() async throws {
        let directory = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        let unknown = UnknownToken(blocks: [0x00148040, 1, 2, 3, 4, 0xDEAD1234, 0xDEAD1234, 0])
        let model = RidesViewModel(device: FakeProxmark(read: .unknown(unknown)), dumpStore: UnknownDumpStore(directory: directory))
        defer { try? FileManager.default.removeItem(at: directory) }

        await model.read()

        guard case .unknown = model.state else { return XCTFail("Expected unknown state") }
        let url = try XCTUnwrap(model.lastDumpURL)
        XCTAssertEqual(try Data(contentsOf: url).count, 32)
        XCTAssertTrue(url.path.contains("--rides-UNKNOWN.bin"))
    }

    func testDetectIsOneFlowAndKnownTokenResetsPendingAndCost() async {
        let fake = FakeProxmark()
        let model = RidesViewModel(device: fake)

        await model.detect()
        model.adjustRides(by: 10)
        XCTAssertEqual(model.currentRides, 73)
        XCTAssertEqual(model.pendingRides, 83)
        XCTAssertEqual(model.costEUR, 0.30)

        await model.detect()

        XCTAssertEqual(model.pendingRides, 73)
        XCTAssertEqual(model.costEUR, 0)
        XCTAssertEqual(model.lastSignalMillivolts, 420)
        XCTAssertEqual(fake.detectCallCount, 2)
        XCTAssertEqual(fake.tuneCallCount, 2)
        XCTAssertEqual(fake.readCallCount, 2)
    }

    func testDetectReportsExactNoChipAndUnknownLogged() async throws {
        let noChip = RidesViewModel(device: FakeProxmark(detect: .noChip))
        await noChip.detect()
        XCTAssertEqual(noChip.message, RidesViewModel.noChipMessage)
        XCTAssertEqual(noChip.state, .noChip)

        let directory = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        defer { try? FileManager.default.removeItem(at: directory) }
        let unknown = UnknownToken(blocks: [0x00148040, 1, 2, 3, 4, 0xDEAD1234, 0xDEAD1234, 0])
        let fake = FakeProxmark(read: .unknown(unknown))
        let model = RidesViewModel(device: fake, dumpStore: UnknownDumpStore(directory: directory))
        await model.detect()
        XCTAssertEqual(model.message, RidesViewModel.unknownMessage)
        XCTAssertNotNil(model.lastDumpURL)
    }

    func testUnknownReadReportsLogFailureWhenDumpDestinationIsUnwritable() async throws {
        let destination = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try Data("not a directory".utf8).write(to: destination)
        defer { try? FileManager.default.removeItem(at: destination) }

        let unknown = UnknownToken(blocks: [0x00148040, 1, 2, 3, 4, 0xDEAD1234, 0xDEAD1234, 0])
        let model = RidesViewModel(
            device: FakeProxmark(read: .unknown(unknown)),
            dumpStore: UnknownDumpStore(directory: destination)
        )

        await model.read()

        XCTAssertNil(model.lastDumpURL)
        XCTAssertTrue(model.message?.hasPrefix("Unknown token — log failed:") == true)
        guard case .failed(let detail) = model.state else {
            return XCTFail("Expected a failed state when the unknown dump cannot be saved")
        }
        XCTAssertTrue(detail.hasPrefix("Unknown token — log failed:"))
        XCTAssertNotEqual(model.message, RidesViewModel.unknownMessage)
    }

    func testUnloadedRideDefaultsRemainZeroForModelState() {
        let model = RidesViewModel(device: FakeProxmark())
        XCTAssertNil(model.loadedToken)
        XCTAssertEqual(model.currentRides, 0)
        XCTAssertEqual(model.pendingRides, 0)
    }

    func testAdjustmentsClampRoundAndCharge() async {
        let fake = FakeProxmark()
        let model = RidesViewModel(device: fake)
        await model.detect()

        model.adjustRides(by: 1000)
        XCTAssertEqual(model.pendingRides, 500)
        model.adjustRides(by: -1000)
        XCTAssertEqual(model.pendingRides, 0)
        model.adjustCost(by: 1.50)
        XCTAssertEqual(model.pendingRides, 50)
        model.round(.pendingRides)
        XCTAssertEqual(model.pendingRides, 50)

        await model.charge()
        XCTAssertEqual(fake.writeCallCount, 1)
        XCTAssertEqual(model.currentRides, 50)
        XCTAssertEqual(model.costEUR, 0)
        XCTAssertEqual(fake.lastWrittenToken?.rideCount, 50)

        let subsequentRead = await fake.read()
        XCTAssertEqual(subsequentRead, .known(fake.lastWrittenToken!))
    }

    func testCostRoundingRoundsSignedDeltaToNearestFiftyRides() async {
        let model = RidesViewModel(device: FakeProxmark())
        await model.detect()

        model.adjustRides(by: -40)
        XCTAssertEqual(model.pendingRides, 33)
        model.round(.costDelta)
        XCTAssertEqual(model.pendingRides, 23)

        model.adjustRides(by: 90)
        XCTAssertEqual(model.pendingRides, 113)
        model.round(.costDelta)
        XCTAssertEqual(model.pendingRides, 123)
    }

    func testResetRequiresSelectionAndSuccessfulOverwriteClearsRides() async {
        let fake = FakeProxmark()
        let model = RidesViewModel(device: fake)
        await model.detect()
        model.openReset()
        XCTAssertNil(model.selectedResetSequence)
        XCTAssertFalse(model.canConfirmReset)

        model.selectedResetSequence = .neptune
        XCTAssertTrue(model.canConfirmReset)
        await model.confirmReset()

        XCTAssertEqual(fake.overwriteCallCount, 1)
        XCTAssertEqual(model.currentRides, 0)
        XCTAssertEqual(model.pendingRides, 0)
        XCTAssertEqual(model.costEUR, 0)

        await model.detect()
        guard case .known(let resetToken) = model.state else {
            return XCTFail("Expected the reset token to be readable")
        }
        XCTAssertEqual(resetToken.sequence, .neptune)
        XCTAssertEqual(resetToken.rideCount, 0)
    }
}
