import XCTest
@testable import RidesTablet

@MainActor
final class DomainStateTests: XCTestCase {
    func testAutoScanCadenceStartsFastAndSlowsAfterKnownOrUnknownConfirmation() async throws {
        let fast: TimeInterval = 0.75
        let slow: TimeInterval = 3
        let knownModel = RidesViewModel(device: FakeProxmark(), periodicConfirmInterval: 0)
        XCTAssertEqual(knownModel.currentAutoScanInterval, fast)
        knownModel.toggleAutoScanning()
        await knownModel.runAutoScanCycleForTesting()
        for _ in 0..<500 where knownModel.currentAutoScanInterval == fast { await Task.yield() }
        XCTAssertEqual(knownModel.currentAutoScanInterval, slow)
        knownModel.stopAutoScanning()

        let directory = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        defer { try? FileManager.default.removeItem(at: directory) }
        let unknown = UnknownToken(blocks: [0x00148040, 1, 2, 3, 4, 0xDEAD1234, 0xDEAD1234, 0])
        let unknownModel = RidesViewModel(
            device: FakeProxmark(read: .unknown(unknown)),
            dumpStore: UnknownDumpStore(directory: directory),
            periodicConfirmInterval: 0
        )
        unknownModel.toggleAutoScanning()
        await unknownModel.runAutoScanCycleForTesting()
        for _ in 0..<500 where unknownModel.currentAutoScanInterval == fast { await Task.yield() }
        XCTAssertNil(unknownModel.loadedToken)
        XCTAssertEqual(unknownModel.currentAutoScanInterval, slow)
        unknownModel.stopAutoScanning()
    }

    func testAutoScanCadenceUsesInjectedIntervalsAndConfirmedRemovalReturnsFast() async {
        let fake = FakeProxmark()
        let model = RidesViewModel(device: fake, autoScanInterval: 0.2, occupiedAutoScanInterval: 1.7, periodicConfirmInterval: 0)
        XCTAssertEqual(model.currentAutoScanInterval, 0.2)
        model.toggleAutoScanning()
        await model.runAutoScanCycleForTesting()
        for _ in 0..<500 where model.currentAutoScanInterval == 0.2 { await Task.yield() }
        XCTAssertEqual(model.currentAutoScanInterval, 1.7)

        fake.set(read: .noChip)
        await model.runAutoScanCycleForTesting()
        for _ in 0..<500 where model.currentAutoScanInterval == 1.7 { await Task.yield() }
        XCTAssertEqual(model.currentAutoScanInterval, 0.2)
        model.stopAutoScanning()
    }

    func testAutoScanWorksWithoutCalibrationAndPreservesPendingForSameSnapshot() async {
        let fake = FakeProxmark()
        let model = RidesViewModel(device: fake, autoScanInterval: 60, periodicConfirmInterval: 0)
        model.toggleAutoScanning()
        for _ in 0..<100 where fake.scanCallCount == 0 { await Task.yield() }
        while model.isAutoScanCycleActive { await Task.yield() }
        XCTAssertEqual(fake.scanCallCount, 1)
        XCTAssertEqual(model.calibrationStep, .noToken)
        XCTAssertEqual(model.loadedToken?.rideCount, 73)

        model.adjustRides(by: 20)
        XCTAssertEqual(model.pendingRides, 93)
        await model.runAutoScanCycleForTesting()
        XCTAssertEqual(model.currentRides, 73)
        XCTAssertEqual(model.pendingRides, 93)
        XCTAssertEqual(fake.scanCallCount, 2)

        model.stopAutoScanning()
        await model.detect()
        XCTAssertFalse(model.isAutoScanning)
        XCTAssertEqual(model.pendingRides, 73)
    }

    func testAutoConfirmationDeduplicatesRepeatedUnknownDump() async throws {
        let directory = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        defer { try? FileManager.default.removeItem(at: directory) }
        let unknown = UnknownToken(blocks: [0x00148040, 1, 2, 3, 4, 0xDEAD1234, 0xDEAD1234, 0])
        let fake = FakeProxmark(read: .unknown(unknown))
        let model = RidesViewModel(device: fake, dumpStore: UnknownDumpStore(directory: directory), autoScanInterval: 60, periodicConfirmInterval: 0)

        model.toggleAutoScanning()
        for _ in 0..<500 where fake.scanCallCount == 0 { await Task.yield() }
        while model.isAutoScanCycleActive { await Task.yield() }
        await model.runAutoScanCycleForTesting()

        let files = try FileManager.default.contentsOfDirectory(atPath: directory.path)
        XCTAssertEqual(files.count, 1)
        guard case .unknown(let loaded) = model.state else { return XCTFail("Expected confirmed unknown state") }
        XCTAssertEqual(loaded.blocks, unknown.blocks)
        model.stopAutoScanning()
        await model.detect()
    }

    func testConfirmedNoChipClearsLoadedTokenAndUsesSignalAsBaseline() async {
        let fake = FakeProxmark(detect: .noChip, tune: .measured(millivolts: 420))
        let model = RidesViewModel(device: fake, autoScanInterval: 60, periodicConfirmInterval: 60)
        model.toggleAutoScanning()
        for _ in 0..<500 where fake.scanCallCount == 0 { await Task.yield() }
        while model.isAutoScanCycleActive { await Task.yield() }
        XCTAssertNil(model.loadedToken)
        XCTAssertEqual(model.state, .noChip)
        let scansAfterNoChip = fake.scanCallCount

        fake.set(detect: .detected)
        await model.runAutoScanCycleForTesting()
        XCTAssertEqual(fake.scanCallCount, scansAfterNoChip, "Stable signal at the no-chip baseline should not trigger repeated confirms")
        model.stopAutoScanning()
    }

    func testFailedAutoConfirmationBlocksChargeUntilSameSnapshotIsConfirmed() async {
        let fake = FakeProxmark()
        let model = RidesViewModel(device: fake, autoScanInterval: 60, periodicConfirmInterval: 0)
        await model.detect()
        model.adjustRides(by: 10)
        model.toggleAutoScanning()
        for _ in 0..<500 where fake.scanCallCount < 2 { await Task.yield() }
        while model.isAutoScanCycleActive { await Task.yield() }

        fake.set(read: .failure("temporary read error"))
        await model.runAutoScanCycleForTesting()
        XCTAssertFalse(model.canCharge)
        XCTAssertEqual(model.pendingRides, 83)
        XCTAssertEqual(model.currentRides, 73)

        fake.set(read: .known(.sample(rideCount: 73)))
        await model.runAutoScanCycleForTesting()
        XCTAssertTrue(model.canCharge)
        XCTAssertEqual(model.pendingRides, 83)
        model.stopAutoScanning()
    }

    func testExplicitStopDuringChargePreventsAutoResumeButKeepsPostWriteRead() async {
        let fake = FakeProxmark()
        let model = RidesViewModel(device: fake, autoScanInterval: 60, periodicConfirmInterval: 60)
        await model.detect()
        model.toggleAutoScanning()
        for _ in 0..<500 where fake.scanCallCount < 2 { await Task.yield() }
        while model.isAutoScanCycleActive { await Task.yield() }
        model.adjustRides(by: 10)
        let scansBeforeCharge = fake.scanCallCount

        let chargeTask = Task { await model.charge() }
        while !model.isBusy { await Task.yield() }
        model.stopAutoScanning()
        await chargeTask.value

        XCTAssertFalse(model.isAutoScanning)
        XCTAssertEqual(fake.scanCallCount, scansBeforeCharge + 1, "Auto-enabled writes still get exactly one immediate confirmation read")
        XCTAssertEqual(model.currentRides, 83)
    }

    func testAutoResetPerformsImmediatePostWriteFullRead() async {
        let fake = FakeProxmark()
        let model = RidesViewModel(device: fake, autoScanInterval: 60, periodicConfirmInterval: 60)
        await model.detect()
        model.toggleAutoScanning()
        for _ in 0..<500 where fake.scanCallCount < 2 { await Task.yield() }
        while model.isAutoScanCycleActive { await Task.yield() }
        let readsBeforeReset = fake.readCallCount

        model.openReset()
        model.selectedResetSequence = .neptune
        await model.confirmReset()

        XCTAssertEqual(fake.readCallCount, readsBeforeReset + 1)
        XCTAssertEqual(model.loadedToken?.sequence, .neptune)
        XCTAssertTrue(model.isAutoScanning)
        model.stopAutoScanning()
    }

    func testManualPrimaryWhileAutoLoadedRunsDetectAndStopsAuto() async {
        let fake = FakeProxmark()
        let model = RidesViewModel(device: fake, autoScanInterval: 60, periodicConfirmInterval: 60)
        XCTAssertEqual(model.primaryActionTitle, "Calibrate: NO token")
        model.toggleAutoScanning()
        for _ in 0..<500 where fake.scanCallCount == 0 { await Task.yield() }
        while model.isAutoScanCycleActive { await Task.yield() }
        XCTAssertEqual(model.primaryActionTitle, "Detect")
        XCTAssertEqual(model.loadedToken?.rideCount, 73)

        model.adjustRides(by: 10)
        let scansBeforeManualAction = fake.scanCallCount
        await model.performPrimaryAction()

        XCTAssertFalse(model.isAutoScanning)
        XCTAssertEqual(fake.scanCallCount, scansBeforeManualAction + 1)
        XCTAssertEqual(model.calibrationStep, .noToken)
        XCTAssertEqual(model.primaryActionTitle, "Detect")
        XCTAssertEqual(model.currentRides, 73)
        XCTAssertEqual(model.pendingRides, 73)
    }

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

    func testCalibrationNoTokenThenTokenAndDecreasingSignalMargin() async {
        let fake = FakeProxmark(detect: .noChip, tune: .measured(millivolts: 18_000))
        let model = RidesViewModel(device: fake)
        await model.performPrimaryAction()
        XCTAssertEqual(model.calibrationStep, .placeToken)
        XCTAssertEqual(model.noTokenSignalMillivolts, 18_000)
        XCTAssertEqual(model.primaryActionTitle, "Calibrate: PLACE token")

        fake.set(detect: .detected, tune: .measured(millivolts: 12_000))
        await model.performPrimaryAction()
        XCTAssertEqual(model.calibrationStep, .ready)
        XCTAssertEqual(model.signalCalibrationReady, true)
        fake.set(tune: .measured(millivolts: 18_000))
        await model.performPrimaryAction()
        XCTAssertEqual(model.signalNormalizedFraction ?? -1, 0.1, accuracy: 0.0001)
        fake.set(tune: .measured(millivolts: 12_000))
        await model.performPrimaryAction()
        XCTAssertEqual(model.signalNormalizedFraction ?? -1, 0.9, accuracy: 0.0001)
    }

    func testCalibrationWrongPresenceDoesNotAdvanceAndEqualSignalsRejectRetry() async {
        let fake = FakeProxmark(detect: .detected, tune: .measured(millivolts: 100))
        let model = RidesViewModel(device: fake)
        await model.performPrimaryAction()
        XCTAssertEqual(model.calibrationStep, .noToken)
        XCTAssertTrue(model.message?.contains("Remove token") == true)

        fake.set(detect: .noChip, tune: .measured(millivolts: 100))
        await model.performPrimaryAction()
        XCTAssertEqual(model.calibrationStep, .placeToken)
        fake.set(detect: .noChip)
        await model.performPrimaryAction()
        XCTAssertEqual(model.calibrationStep, .placeToken)
        XCTAssertTrue(model.message?.contains("No token") == true)

        fake.set(detect: .detected, tune: .measured(millivolts: 100))
        await model.performPrimaryAction()
        XCTAssertEqual(model.calibrationStep, .placeToken)
        XCTAssertTrue(model.message?.contains("equal") == true)
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

    func testClearPendingRidesSetsAbsoluteZeroWithoutChangingCurrentOrToken() async {
        let model = RidesViewModel(device: FakeProxmark())
        await model.detect()
        let tokenBefore = model.loadedToken
        model.adjustRides(by: -40)
        XCTAssertEqual(model.pendingRides, 33)
        XCTAssertLessThan(model.costEUR, 0)

        model.clearPendingRides()

        XCTAssertEqual(model.pendingRides, 0)
        XCTAssertEqual(model.currentRides, 73)
        XCTAssertEqual(model.costEUR, Decimal(-73) * model.configuration.pricePerRideEUR)
        XCTAssertEqual(model.loadedToken, tokenBefore)

        let unloaded = RidesViewModel(device: FakeProxmark())
        unloaded.clearPendingRides()
        XCTAssertEqual(unloaded.pendingRides, 0)
        XCTAssertNil(unloaded.loadedToken)
    }

    func testAdjustmentsClampRoundAndCharge() async {
        let fake = FakeProxmark()
        let (historyStore, _) = isolatedChargeHistoryStore()
        let model = RidesViewModel(device: fake, chargeHistoryStore: historyStore)
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
        XCTAssertEqual(model.completedCharges.count, 1)
        XCTAssertEqual(model.completedCharges[0].initialRides, 73)
        XCTAssertEqual(model.completedCharges[0].addedRides, -23)
        XCTAssertEqual(model.completedCharges[0].totalRides, 50)
        XCTAssertEqual(model.completedCharges[0].sequence, .mercury)
        XCTAssertEqual(model.completedCharges[0].amountEUR, Decimal(-23) * model.configuration.pricePerRideEUR)

        let subsequentRead = await fake.read()
        XCTAssertEqual(subsequentRead, .known(fake.lastWrittenToken!))
    }

    func testChargeHistoryOnlyRecordsSuccessfulWritesAndPersistsNewestFirst() async {
        let (store, _) = isolatedChargeHistoryStore()
        let fake = FakeProxmark()
        let model = RidesViewModel(device: fake, chargeHistoryStore: store)
        await model.detect()
        model.adjustRides(by: 10)
        await model.charge()
        model.adjustRides(by: 10)
        fake.set(write: .failure("write failed"))
        await model.charge()
        fake.set(write: .success)
        await model.detect()
        model.adjustRides(by: 10)
        await model.charge()

        XCTAssertEqual(model.completedCharges.count, 2)
        let reloaded = RidesViewModel(device: FakeProxmark(), chargeHistoryStore: store)
        XCTAssertEqual(reloaded.completedCharges, model.completedCharges)
        XCTAssertEqual(model.completedCharges[1].initialRides, 73)
        XCTAssertEqual(model.completedCharges[1].addedRides, 10)
        XCTAssertEqual(model.completedCharges[1].totalRides, 83)
        XCTAssertEqual(model.completedCharges[1].sequence, .mercury)
        XCTAssertEqual(model.completedCharges[0].initialRides, 83)
        XCTAssertEqual(model.completedCharges[0].addedRides, 10)
        XCTAssertEqual(model.completedCharges[0].totalRides, 93)
    }

    func testDetectAndResetDoNotCreateChargeHistory() async {
        let (store, _) = isolatedChargeHistoryStore()
        let model = RidesViewModel(device: FakeProxmark(), chargeHistoryStore: store)
        await model.detect()
        await model.detect()
        model.openReset()
        model.selectedResetSequence = .earth
        await model.confirmReset()
        XCTAssertTrue(model.completedCharges.isEmpty)
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

    private func isolatedChargeHistoryStore() -> (UserDefaultsChargeHistoryStore, UserDefaults) {
        let suiteName = "ChargeHistoryTests.\(UUID().uuidString)"
        let defaults = UserDefaults(suiteName: suiteName)!
        return (UserDefaultsChargeHistoryStore(defaults: defaults, key: "history", limit: 100), defaults)
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
