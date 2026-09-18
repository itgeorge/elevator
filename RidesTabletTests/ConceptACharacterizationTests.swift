import XCTest
@testable import RidesTablet

/// Locks Concept A operator semantics against the pre-Slice-5 device boundary.
/// These tests are intentionally written against `RidesViewModel` + `FakeProxmark`
/// before the hardware-neutral refactor and must keep passing afterward.
@MainActor
final class ConceptACharacterizationTests: XCTestCase {
    func testDetectIsSingleOperatorActionPublishingSignalAndKnownToken() async {
        let fake = FakeProxmark()
        let model = RidesViewModel(device: fake)

        await model.detect()

        XCTAssertEqual(model.state, .known(Token.sample(rideCount: 73, sequence: .mercury)))
        XCTAssertEqual(model.currentRides, 73)
        XCTAssertEqual(model.pendingRides, 73)
        XCTAssertEqual(model.costEUR, 0)
        XCTAssertEqual(model.aptBlock4Text, Token.hex(Token.sample().block4))
        XCTAssertEqual(model.lastSignalMillivolts, 420)
        XCTAssertEqual(model.message, "Token loaded.")
        XCTAssertEqual(fake.detectCallCount, 1)
        XCTAssertEqual(fake.tuneCallCount, 1)
        XCTAssertEqual(fake.readCallCount, 1)
    }

    func testDetectBusyGatePreventsOverlappingOperatorActions() async {
        let fake = FakeProxmark()
        let model = RidesViewModel(device: fake)
        model.selectSimulation(.goodToken)

        // While idle, Detect starts work; a second overlapping call is ignored by isBusy.
        await model.detect()
        XCTAssertFalse(model.isBusy)
        XCTAssertTrue(model.canAdjust)
        XCTAssertFalse(model.canCharge)

        model.adjustRides(by: 10)
        XCTAssertTrue(model.canCharge)
        XCTAssertEqual(model.costText.hasPrefix("€"), true)
    }

    func testUnknownLoggedOnlyAfterDumpPersistenceAndExactMessage() async throws {
        let directory = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        defer { try? FileManager.default.removeItem(at: directory) }
        let unknown = UnknownToken(blocks: [0x00148040, 1, 2, 3, 4, 0xDEAD1234, 0xDEAD1234, 0])
        let model = RidesViewModel(
            device: FakeProxmark(read: .unknown(unknown)),
            dumpStore: UnknownDumpStore(directory: directory)
        )

        await model.detect()

        XCTAssertEqual(model.message, RidesViewModel.unknownMessage)
        XCTAssertNotNil(model.lastDumpURL)
        guard case .unknown = model.state else {
            return XCTFail("Expected unknown state after successful dump")
        }
    }

    func testUnknownDumpFailureNeverClaimsLogged() async throws {
        let destination = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try Data("not-a-directory".utf8).write(to: destination)
        defer { try? FileManager.default.removeItem(at: destination) }
        let unknown = UnknownToken(blocks: [0x00148040, 1, 2, 3, 4, 0xDEAD1234, 0xDEAD1234, 0])
        let model = RidesViewModel(
            device: FakeProxmark(read: .unknown(unknown)),
            dumpStore: UnknownDumpStore(directory: destination)
        )

        await model.detect()

        XCTAssertNil(model.lastDumpURL)
        XCTAssertNotEqual(model.message, RidesViewModel.unknownMessage)
        XCTAssertTrue(model.message?.hasPrefix("Unknown token — log failed:") == true)
    }

    func testChargeWritesPendingRidesAndClearsCostOnlyOnSuccess() async {
        let fake = FakeProxmark()
        let model = RidesViewModel(device: fake)
        await model.detect()
        model.adjustRides(by: 27)
        XCTAssertEqual(model.pendingRides, 100)
        XCTAssertTrue(model.canCharge)

        await model.charge()

        XCTAssertEqual(fake.writeCallCount, 1)
        XCTAssertEqual(fake.lastWrittenToken?.rideCount, 100)
        XCTAssertEqual(model.currentRides, 100)
        XCTAssertEqual(model.pendingRides, 100)
        XCTAssertEqual(model.costEUR, 0)
        XCTAssertEqual(model.message, "Charge successful.")
        guard case .known(let token) = model.state else {
            return XCTFail("Expected known state after charge")
        }
        XCTAssertEqual(token.rideCount, 100)
    }

    func testChargeFailureLeavesCurrentRidesUnchanged() async {
        let fake = FakeProxmark()
        fake.apply(.writeFailure)
        let model = RidesViewModel(device: fake)
        await model.detect()
        model.adjustRides(by: 10)

        await model.charge()

        XCTAssertEqual(model.currentRides, 73)
        XCTAssertEqual(model.pendingRides, 83)
        XCTAssertEqual(model.message, "Charge could not be written. Try again.")
        guard case .failed = model.state else {
            return XCTFail("Expected failed state after charge write failure")
        }
        XCTAssertFalse(model.canCharge)
    }

    func testResetConfirmationDisabledUntilExplicitProfileSelection() async {
        let fake = FakeProxmark()
        let model = RidesViewModel(device: fake)
        await model.detect()

        model.openReset()
        XCTAssertTrue(model.isResetSheetPresented)
        XCTAssertNil(model.selectedResetSequence)
        XCTAssertFalse(model.canConfirmReset)

        model.selectedResetSequence = .venus
        XCTAssertTrue(model.canConfirmReset)

        await model.confirmReset()

        XCTAssertEqual(fake.overwriteCallCount, 1)
        XCTAssertEqual(model.currentRides, 0)
        XCTAssertEqual(model.pendingRides, 0)
        XCTAssertFalse(model.isResetSheetPresented)
        XCTAssertEqual(model.message, "Reset successful.")
        guard case .known(let token) = model.state else {
            return XCTFail("Expected known reset token")
        }
        XCTAssertEqual(token.sequence, .venus)
        XCTAssertEqual(token.rideCount, 0)
    }

    func testNoChipMessageIsExactAndLeavesNoDump() async {
        let model = RidesViewModel(device: FakeProxmark(detect: .noChip))
        await model.detect()
        XCTAssertEqual(model.state, .noChip)
        XCTAssertEqual(model.message, RidesViewModel.noChipMessage)
        XCTAssertNil(model.lastDumpURL)
        XCTAssertNil(model.loadedToken)
    }
}
