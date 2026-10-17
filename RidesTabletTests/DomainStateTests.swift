import XCTest
@testable import RidesTablet

@MainActor
final class DomainStateTests: XCTestCase {
    func testManualKnownAndUnknownBaselinesDoNotPeriodicallyReread() async throws {
        let knownFake = FakeProxmark()
        let known = RidesViewModel(device: knownFake, autoScanInterval: 60)
        await calibrateFake(known, knownFake)
        await known.detect()
        known.toggleAutoScanning()
        await waitForAutoCycle(known, fake: knownFake)
        for _ in 0..<8 { await known.runAutoScanCycleForTesting() }
        XCTAssertEqual(knownFake.scanCallCount, 1, "Manual Detect establishes the known-token baseline for Auto")
        known.stopAutoScanning()

        let directory = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        defer { try? FileManager.default.removeItem(at: directory) }
        let unknownToken = UnknownToken(blocks: [0x00148040, 1, 2, 3, 4, 0xDEAD1234, 0xDEAD1234, 0])
        let unknownFake = FakeProxmark(read: .unknown(unknownToken))
        let unknown = RidesViewModel(
            device: unknownFake,
            dumpStore: UnknownDumpStore(directory: directory),
            autoScanInterval: 60,
        )
        await calibrateFake(unknown, unknownFake)
        await unknown.detect()
        unknown.toggleAutoScanning()
        await waitForAutoCycle(unknown, fake: unknownFake)
        for _ in 0..<8 { await unknown.runAutoScanCycleForTesting() }
        XCTAssertEqual(unknownFake.scanCallCount, 1, "Stable unknown presence must remain tune-only")
        unknown.stopAutoScanning()
    }

    func testKnownAutoConfirmationUsesWorseningProbeBaselineAndIgnoresImprovement() async {
        let fake = FakeProxmark()
        let model = RidesViewModel(device: fake, autoScanInterval: 60)
        await calibrateFake(model, fake)
        await model.detect()
        model.adjustRides(by: 20)
        XCTAssertEqual(model.pendingRides, 93)

        model.toggleAutoScanning()
        await waitForAutoCycle(model, fake: fake)
        fake.set(tune: .measured(millivolts: 4_000))
        await model.runAutoScanCycleForTesting()
        XCTAssertEqual(fake.scanCallCount, 1, "A single worsening probe is not enough")
        await model.runAutoScanCycleForTesting()
        XCTAssertEqual(fake.scanCallCount, 2, "The second worsening probe confirms once")
        XCTAssertEqual(model.pendingRides, 93, "A matching confirmation preserves pending rides")

        for _ in 0..<5 { await model.runAutoScanCycleForTesting() }
        XCTAssertEqual(fake.scanCallCount, 2, "The triggering 3000 mV probe becomes the new baseline")
        fake.set(tune: .measured(millivolts: 420))
        for _ in 0..<5 { await model.runAutoScanCycleForTesting() }
        XCTAssertEqual(fake.scanCallCount, 2, "Improvement must not trigger a full read")
        model.stopAutoScanning()
    }

    func testCalibratedOppositeSignalDirectionConfirmsWorseningOnly() async {
        let fake = FakeProxmark(detect: .noChip, tune: .measured(millivolts: 1_000))
        let model = RidesViewModel(device: fake, autoScanInterval: 60)
        await model.performPrimaryAction() // empty = 1000
        fake.set(detect: .detected, tune: .measured(millivolts: 5_000))
        await model.performPrimaryAction() // present = 5000 (raw increase means stronger)
        XCTAssertEqual(model.calibrationStep, .ready)
        await model.detect() // baseline = 5000

        model.toggleAutoScanning()
        await waitForAutoCycle(model, fake: fake)
        fake.set(tune: .measured(millivolts: 2_500)) // worsening remains above the quality gate
        await model.runAutoScanCycleForTesting()
        XCTAssertEqual(fake.scanCallCount, 1)
        await model.runAutoScanCycleForTesting()
        XCTAssertEqual(fake.scanCallCount, 2)
        for _ in 0..<5 { await model.runAutoScanCycleForTesting() }
        XCTAssertEqual(fake.scanCallCount, 2, "The worse 2000 mV probe is the adopted baseline")
        model.stopAutoScanning()
    }

    func testBlockedAutoSignalNeverReadsAndRecoveryRequiresFreshRead() async {
        let fake = FakeProxmark()
        let model = RidesViewModel(device: fake, autoScanInterval: 60)
        await calibrateFake(model, fake)
        fake.set(tune: .measured(millivolts: 18_000)) // calibrated empty/high raw signal
        model.toggleAutoScanning()
        await model.runAutoScanCycleForTesting()
        XCTAssertTrue(model.isAutoSignalBlocked)
        XCTAssertFalse(model.isAutoDetecting)
        XCTAssertEqual(fake.scanCallCount, 0)
        for _ in 0..<4 { await model.runAutoScanCycleForTesting() }
        XCTAssertEqual(fake.scanCallCount, 0, "Blocked periodic probes must remain tune-only")

        // Quality 0.15 is blocked; the 0.15–0.20 band stays blocked until the release edge.
        fake.set(tune: .measured(millivolts: 15_363))
        await model.runAutoScanCycleForTesting()
        XCTAssertTrue(model.isAutoSignalBlocked, "The 15% quality boundary remains blocked")
        XCTAssertEqual(fake.scanCallCount, 0)
        fake.set(tune: .measured(millivolts: 14_924)) // quality approximately 0.175
        await model.runAutoScanCycleForTesting()
        XCTAssertTrue(model.isAutoSignalBlocked, "The hysteresis band must not chatter")
        XCTAssertEqual(fake.scanCallCount, 0)
        fake.set(tune: .measured(millivolts: 9_000)) // better-half recovery candidate
        await model.runAutoScanCycleForTesting()
        XCTAssertFalse(model.isAutoSignalBlocked)
        XCTAssertEqual(fake.scanCallCount, 0, "Recovery starts settling instead of reading immediately")
        for _ in 0..<2 { await model.runAutoScanCycleForTesting() }
        XCTAssertEqual(fake.scanCallCount, 1, "Three stable eligible probes confirm after recovery")
        XCTAssertFalse(model.canCharge)
        model.stopAutoScanning()
    }

    func testWeakSignalKeepsPendingButRequiresFreshReadAndManualDetectBypassesGate() async {
        let fake = FakeProxmark()
        let model = RidesViewModel(device: fake, autoScanInterval: 60)
        await calibrateFake(model, fake)
        await model.detect()
        model.adjustRides(by: 10)
        model.toggleAutoScanning()
        await waitForAutoCycle(model, fake: fake)
        let originalReads = fake.scanCallCount

        fake.set(tune: .measured(millivolts: 18_000))
        await model.runAutoScanCycleForTesting()
        XCTAssertTrue(model.isAutoSignalBlocked)
        XCTAssertFalse(model.canCharge)
        XCTAssertEqual(model.pendingRides, 83)
        XCTAssertEqual(model.currentAutoScanInterval, 60)
        XCTAssertEqual(fake.scanCallCount, originalReads)

        fake.set(tune: .measured(millivolts: 420))
        await model.runAutoScanCycleForTesting()
        XCTAssertFalse(model.isAutoSignalBlocked)
        XCTAssertEqual(fake.scanCallCount, originalReads, "Recovery must settle before confirming")
        for _ in 0..<4 { await model.runAutoScanCycleForTesting() }
        XCTAssertEqual(fake.scanCallCount, originalReads + 1)
        XCTAssertEqual(model.pendingRides, 83)
        XCTAssertTrue(model.canCharge)

        fake.set(tune: .measured(millivolts: 18_000))
        await model.runAutoScanCycleForTesting()
        await model.performPrimaryAction()
        XCTAssertFalse(model.isAutoScanning)
        XCTAssertFalse(model.isAutoSignalBlocked)
        XCTAssertEqual(fake.scanCallCount, originalReads + 2)
        XCTAssertEqual(model.currentRides, 73)
        XCTAssertEqual(model.pendingRides, 73)
    }

    func testAutoDetectingIsPublishedDuringInitialDelayedScanAndClearsOnStop() async {
        let fake = FakeProxmark(scanDelay: 0.2)
        let model = RidesViewModel(device: fake, autoScanInterval: 60)
        await calibrateFake(model, fake)
        model.toggleAutoScanning()
        for _ in 0..<2 { await model.runAutoScanCycleForTesting() }
        let finalProbe = Task { await model.runAutoScanCycleForTesting() }

        let deadline = Date().addingTimeInterval(1.5)
        while !model.isAutoDetecting && Date() < deadline {
            try? await Task.sleep(nanoseconds: 10_000_000)
        }
        XCTAssertTrue(model.isAutoDetecting, "Initial automatic full read should expose Detecting…")
        model.stopAutoScanning()
        await finalProbe.value
        XCTAssertFalse(model.isAutoDetecting, "Stopping auto scan clears Detecting immediately")
    }

    func testUnconfirmedPlacementWaitsForThreeStableEligibleProbes() async {
        let fake = FakeProxmark()
        let model = RidesViewModel(device: fake, autoScanInterval: 60)
        await calibrateFake(model, fake)
        model.toggleAutoScanning()
        await waitForAutoCycle(model, fake: fake)
        XCTAssertEqual(fake.scanCallCount, 0)
        XCTAssertTrue(model.isAutoSettling)
        await model.runAutoScanCycleForTesting()
        XCTAssertEqual(fake.scanCallCount, 0)
        await model.runAutoScanCycleForTesting()
        XCTAssertEqual(fake.scanCallCount, 1)
        XCTAssertFalse(model.isAutoSettling)
        model.stopAutoScanning()
    }

    func testJitterUsesFiveSecondFallbackArmedOnlyInBetterHalfAndGateWins() async {
        let fake = FakeProxmark()
        var instant = Date(timeIntervalSince1970: 1_000)
        let model = RidesViewModel(device: fake, autoScanInterval: 60, now: { instant })
        await calibrateFake(model, fake)
        model.toggleAutoScanning()
        fake.set(tune: .measured(millivolts: 17_000)) // quality below .5; timer must not arm
        await model.runAutoScanCycleForTesting()
        instant.addTimeInterval(10)
        await model.runAutoScanCycleForTesting()
        XCTAssertEqual(fake.scanCallCount, 0)

        fake.set(tune: .measured(millivolts: 9_000))
        await model.runAutoScanCycleForTesting()
        fake.set(tune: .measured(millivolts: 7_000))
        await model.runAutoScanCycleForTesting()
        fake.set(tune: .measured(millivolts: 9_000))
        await model.runAutoScanCycleForTesting()
        XCTAssertEqual(fake.scanCallCount, 0, "Jitter wider than 10% must not settle")
        instant.addTimeInterval(4.9)
        fake.set(tune: .measured(millivolts: 9_000))
        await model.runAutoScanCycleForTesting()
        XCTAssertEqual(fake.scanCallCount, 0, "The backup must not fire before five seconds")
        instant.addTimeInterval(0.1)
        await model.runAutoScanCycleForTesting()
        XCTAssertEqual(fake.scanCallCount, 1, "Five-second backup confirms persistent jitter")
        model.stopAutoScanning()
    }

    func testEarlyOutlierRemainsInWholeEpisodeSpread() async {
        let fake = FakeProxmark()
        let instant = Date(timeIntervalSince1970: 1_500)
        let model = RidesViewModel(device: fake, autoScanInterval: 60, now: { instant })
        await calibrateFake(model, fake)
        model.toggleAutoScanning()
        for value in [7_000, 9_000, 9_000, 9_000, 9_000] {
            fake.set(tune: .measured(millivolts: value))
            await model.runAutoScanCycleForTesting()
        }
        XCTAssertEqual(fake.scanCallCount, 0, "An early >10% outlier is not evicted by three later flat samples")
        model.stopAutoScanning()
    }

    func testExactlyTenPercentWholeEpisodeSpreadSettlesAfterThreeSamples() async {
        let fake = FakeProxmark()
        let instant = Date(timeIntervalSince1970: 2_400)
        let model = RidesViewModel(device: fake, autoScanInterval: 60, now: { instant })
        await calibrateFake(model, fake)
        model.toggleAutoScanning()
        for value in [10_000, 11_758, 10_000] { // calibration span is 17,580 mV
            fake.set(tune: .measured(millivolts: value))
            await model.runAutoScanCycleForTesting()
        }
        XCTAssertEqual(fake.scanCallCount, 1, "Exactly 10% spread is accepted at the three-sample minimum")
        model.stopAutoScanning()
    }

    func testSpreadAboveTenPercentDoesNotSettleAtThreeSamples() async {
        let fake = FakeProxmark()
        let instant = Date(timeIntervalSince1970: 2_450)
        let model = RidesViewModel(device: fake, autoScanInterval: 60, now: { instant })
        await calibrateFake(model, fake)
        model.toggleAutoScanning()
        for value in [10_000, 11_759, 10_000] {
            fake.set(tune: .measured(millivolts: value))
            await model.runAutoScanCycleForTesting()
        }
        XCTAssertEqual(fake.scanCallCount, 0, "Spread just above 10% must not settle")
        model.stopAutoScanning()
    }

    func testLowerHalfEligibleProbeContributesToWholeEpisodeSpread() async {
        let fake = FakeProxmark()
        let instant = Date(timeIntervalSince1970: 2_500)
        let model = RidesViewModel(device: fake, autoScanInterval: 60, now: { instant })
        await calibrateFake(model, fake)
        model.toggleAutoScanning()
        for value in [9_000, 12_700, 9_000] {
            fake.set(tune: .measured(millivolts: value))
            await model.runAutoScanCycleForTesting()
        }
        XCTAssertEqual(fake.scanCallCount, 0, "A gate-eligible lower-half outlier remains in the episode spread")
        model.stopAutoScanning()
    }

    func testThreeStableEligibleSamplesCanSettleBelowBetterHalfWithoutTimer() async {
        let fake = FakeProxmark()
        let instant = Date(timeIntervalSince1970: 2_600)
        let model = RidesViewModel(device: fake, autoScanInterval: 60, now: { instant })
        await calibrateFake(model, fake)
        model.toggleAutoScanning()
        for _ in 0..<3 {
            fake.set(tune: .measured(millivolts: 12_000)) // quality .34, above the high-signal cutoff
            await model.runAutoScanCycleForTesting()
        }
        XCTAssertEqual(fake.scanCallCount, 1, "Three stable gate-eligible samples settle without arming fallback")
        model.stopAutoScanning()
    }

    func testNoChipRetriesEveryFiveSecondsAfterQualityFallsBelowBetterHalf() async {
        let fake = FakeProxmark(detect: .noChip)
        var instant = Date(timeIntervalSince1970: 2_700)
        let model = RidesViewModel(device: fake, autoScanInterval: 60, now: { instant })
        await calibrateFake(model, fake)
        fake.set(detect: .noChip)
        model.toggleAutoScanning()
        fake.set(tune: .measured(millivolts: 9_000))
        await model.runAutoScanCycleForTesting() // arms deadline
        instant.addTimeInterval(5)
        fake.set(tune: .measured(millivolts: 12_700)) // fallback reads at q=.3
        await model.runAutoScanCycleForTesting()
        XCTAssertEqual(fake.scanCallCount, 1)
        await model.runAutoScanCycleForTesting()
        XCTAssertEqual(fake.scanCallCount, 1, "No-chip result must not cause immediate probe-driven retries")
        instant.addTimeInterval(5)
        for _ in 0..<10 where fake.scanCallCount < 2 {
            await model.runAutoScanCycleForTesting()
            await Task.yield()
        }
        XCTAssertEqual(fake.scanCallCount, 2, "The armed backup continues every five seconds below q=.5; status: \(model.autoScanStatus), settling: \(model.isAutoSettling)")
        model.stopAutoScanning()
    }

    func testHighCutoffResetsRetryCooldownAndRequiresThreeFreshProbes() async {
        let fake = FakeProxmark(detect: .noChip)
        let instant = Date(timeIntervalSince1970: 2_900)
        let model = RidesViewModel(device: fake, autoScanInterval: 60, now: { instant })
        await calibrateFake(model, fake)
        fake.set(detect: .noChip)
        model.toggleAutoScanning()
        for _ in 0..<3 { await model.runAutoScanCycleForTesting() }
        XCTAssertEqual(fake.scanCallCount, 1)
        fake.set(tune: .measured(millivolts: 18_000))
        await model.runAutoScanCycleForTesting()
        XCTAssertTrue(model.isAutoSignalBlocked)
        fake.set(tune: .measured(millivolts: 9_000))
        for _ in 0..<2 {
            await model.runAutoScanCycleForTesting()
            XCTAssertEqual(fake.scanCallCount, 1)
        }
        await model.runAutoScanCycleForTesting()
        XCTAssertEqual(fake.scanCallCount, 2, "A blocked interval resets cooldown and starts a fresh three-probe episode")
        model.stopAutoScanning()
    }

    func testSettlingDeadlineRemainsLatchedWhenQualityFallsBelowBetterHalf() async {
        let fake = FakeProxmark()
        var instant = Date(timeIntervalSince1970: 3_000)
        let model = RidesViewModel(device: fake, autoScanInterval: 60, now: { instant })
        await calibrateFake(model, fake)
        model.toggleAutoScanning()
        fake.set(tune: .measured(millivolts: 9_000))
        await model.runAutoScanCycleForTesting() // arms at quality > .5
        fake.set(tune: .measured(millivolts: 12_700)) // quality .3: still above the 15% cutoff
        await model.runAutoScanCycleForTesting()
        instant.addTimeInterval(5)
        await model.runAutoScanCycleForTesting()
        XCTAssertEqual(fake.scanCallCount, 1, "The armed deadline remains active below quality .5")
        model.stopAutoScanning()
    }

    func testBlockedGateCancelsAnArmedSettlingDeadline() async {
        let fake = FakeProxmark()
        var instant = Date(timeIntervalSince1970: 2_000)
        let model = RidesViewModel(device: fake, autoScanInterval: 60, now: { instant })
        await calibrateFake(model, fake)
        model.toggleAutoScanning()
        fake.set(tune: .measured(millivolts: 9_000))
        await model.runAutoScanCycleForTesting()
        fake.set(tune: .measured(millivolts: 18_000))
        await model.runAutoScanCycleForTesting()
        XCTAssertTrue(model.isAutoSignalBlocked)
        instant.addTimeInterval(6)
        await model.runAutoScanCycleForTesting()
        XCTAssertEqual(fake.scanCallCount, 0, "A blocked gate cancels the settling deadline")
        fake.set(tune: .measured(millivolts: 9_000))
        await model.runAutoScanCycleForTesting()
        XCTAssertEqual(fake.scanCallCount, 0, "Recovery must build a fresh settling window")
        await model.runAutoScanCycleForTesting()
        XCTAssertEqual(fake.scanCallCount, 0, "The cutoff discarded the previous episode's samples")
        await model.runAutoScanCycleForTesting()
        XCTAssertEqual(fake.scanCallCount, 1, "Three fresh probes settle after recovery")
        model.stopAutoScanning()
    }

    private func waitForAutoCycle(_ model: RidesViewModel, fake: FakeProxmark) async {
        try? await Task.sleep(nanoseconds: 10_000_000)
        let deadline = Date().addingTimeInterval(1.5)
        while model.isAutoScanCycleActive && Date() < deadline { await Task.yield() }
        XCTAssertFalse(model.isAutoScanCycleActive, "Timed out waiting for the automatic signal cycle")
    }

    func testAutoScanCadenceStartsFastAndSlowsAfterKnownOrUnknownConfirmation() async throws {
        let fast: TimeInterval = 0.1
        let slow: TimeInterval = 3
        let knownModelFake = FakeProxmark()
        let knownModel = RidesViewModel(device: knownModelFake)
        XCTAssertEqual(knownModel.currentAutoScanInterval, fast)
        await calibrateFake(knownModel, knownModelFake)
        knownModel.toggleAutoScanning()
        for _ in 0..<3 { await knownModel.runAutoScanCycleForTesting() }
        for _ in 0..<500 where knownModel.currentAutoScanInterval == fast { await Task.yield() }
        XCTAssertEqual(knownModel.currentAutoScanInterval, slow)
        knownModel.stopAutoScanning()

        let directory = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        defer { try? FileManager.default.removeItem(at: directory) }
        let unknown = UnknownToken(blocks: [0x00148040, 1, 2, 3, 4, 0xDEAD1234, 0xDEAD1234, 0])
        let unknownModelFake = FakeProxmark(read: .unknown(unknown))
        let unknownModel = RidesViewModel(
            device: unknownModelFake,
            dumpStore: UnknownDumpStore(directory: directory),
        )
        await calibrateFake(unknownModel, unknownModelFake)
        unknownModel.toggleAutoScanning()
        for _ in 0..<3 { await unknownModel.runAutoScanCycleForTesting() }
        for _ in 0..<500 where unknownModel.currentAutoScanInterval == fast { await Task.yield() }
        XCTAssertNil(unknownModel.loadedToken)
        XCTAssertEqual(unknownModel.currentAutoScanInterval, slow)
        unknownModel.stopAutoScanning()
    }

    func testAutoScanCadenceUsesInjectedIntervalsAndConfirmedRemovalReturnsFast() async {
        let fake = FakeProxmark()
        let model = RidesViewModel(device: fake, autoScanInterval: 0.2, occupiedAutoScanInterval: 1.7)
        XCTAssertEqual(model.currentAutoScanInterval, 0.2)
        await calibrateFake(model, fake)
        model.toggleAutoScanning()
        for _ in 0..<3 { await model.runAutoScanCycleForTesting() }
        for _ in 0..<500 where model.currentAutoScanInterval == 0.2 { await Task.yield() }
        XCTAssertEqual(model.currentAutoScanInterval, 1.7)

        fake.apply(.noChip)
        fake.set(tune: .measured(millivolts: 14_484)) // admissible low-quality edge, unlike calibrated empty
        await model.runAutoScanCycleForTesting()
        XCTAssertEqual(model.currentAutoScanInterval, 1.7, "One worsening probe is not enough to confirm removal")
        await model.runAutoScanCycleForTesting()
        for _ in 0..<500 where model.currentAutoScanInterval == 1.7 { await Task.yield() }
        XCTAssertEqual(model.currentAutoScanInterval, 0.2)
        model.stopAutoScanning()
    }

    func testAutoDisabledBeforeBothCalibrationSteps() async {
        let fake = FakeProxmark()
        let model = RidesViewModel(device: fake, autoScanInterval: 60)
        model.toggleAutoScanning()
        XCTAssertFalse(model.isAutoScanning)
        XCTAssertFalse(model.canEnableAutoScanning)
        XCTAssertEqual(fake.scanCallCount, 0)
        XCTAssertEqual(model.calibrationStep, .noToken)
    }

    func testAutoConfirmationDeduplicatesRepeatedUnknownDump() async throws {
        let directory = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        defer { try? FileManager.default.removeItem(at: directory) }
        let unknown = UnknownToken(blocks: [0x00148040, 1, 2, 3, 4, 0xDEAD1234, 0xDEAD1234, 0])
        let fake = FakeProxmark(read: .unknown(unknown))
        let model = RidesViewModel(device: fake, dumpStore: UnknownDumpStore(directory: directory), autoScanInterval: 60)

        await calibrateFake(model, fake)
        model.toggleAutoScanning()
        for _ in 0..<3 { await model.runAutoScanCycleForTesting() }
        for _ in 0..<5 { await model.runAutoScanCycleForTesting() }
        XCTAssertEqual(fake.scanCallCount, 1, "Stable unknown presence must not cause periodic full reads")

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
        await calibrateFake(model, fake)
        fake.set(detect: .noChip, tune: .measured(millivolts: 420))
        model.toggleAutoScanning()
        for _ in 0..<3 { await model.runAutoScanCycleForTesting() }
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
        var instant = Date(timeIntervalSince1970: 4_000)
        let model = RidesViewModel(device: fake, autoScanInterval: 60, now: { instant })
        await calibrateFake(model, fake)
        await model.detect()
        model.adjustRides(by: 10)
        model.toggleAutoScanning()

        // Confirmations now require a worsening signal, not a periodic timer.
        fake.set(tune: .measured(millivolts: 4_000), read: .failure("temporary read error"))
        await model.runAutoScanCycleForTesting()
        await model.runAutoScanCycleForTesting()
        XCTAssertFalse(model.canCharge)
        XCTAssertEqual(model.pendingRides, 83)
        XCTAssertEqual(model.currentRides, 73)

        fake.set(read: .known(.sample(rideCount: 73)))
        await model.runAutoScanCycleForTesting()
        instant.addTimeInterval(5)
        for _ in 0..<4 { await model.runAutoScanCycleForTesting() }
        XCTAssertTrue(model.canCharge)
        XCTAssertEqual(model.pendingRides, 83)
        model.stopAutoScanning()
    }

    func testExplicitStopDuringChargePreventsAutoResumeButKeepsPostWriteRead() async {
        let fake = FakeProxmark()
        let model = RidesViewModel(device: fake, autoScanInterval: 60, periodicConfirmInterval: 60)
        await calibrateFake(model, fake)
        await model.detect()
        model.toggleAutoScanning()
        model.adjustRides(by: 10)
        let scansBeforeCharge = fake.scanCallCount

        let chargeTask = Task { await model.charge() }
        let chargeDeadline = Date().addingTimeInterval(0.5)
        while !model.isBusy && Date() < chargeDeadline { await Task.yield() }
        XCTAssertTrue(model.isBusy, "A known token with pending rides should enter the charge operation")
        model.stopAutoScanning()
        await chargeTask.value

        XCTAssertFalse(model.isAutoScanning)
        XCTAssertEqual(fake.scanCallCount, scansBeforeCharge + 1, "Auto-enabled writes still get exactly one immediate confirmation read")
        XCTAssertEqual(model.currentRides, 83)
    }

    func testAutoResetPerformsImmediatePostWriteFullRead() async {
        let fake = FakeProxmark()
        let model = RidesViewModel(device: fake, autoScanInterval: 60, periodicConfirmInterval: 60)
        await calibrateFake(model, fake)
        await model.detect()
        model.toggleAutoScanning()
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
        await calibrateFake(model, fake)
        model.toggleAutoScanning()
        for _ in 0..<3 { await model.runAutoScanCycleForTesting() }
        XCTAssertEqual(model.primaryActionTitle, "Detect")
        XCTAssertEqual(model.loadedToken?.rideCount, 73)

        model.adjustRides(by: 10)
        let scansBeforeManualAction = fake.scanCallCount
        await model.performPrimaryAction()

        XCTAssertFalse(model.isAutoScanning)
        XCTAssertEqual(fake.scanCallCount, scansBeforeManualAction + 1)
        XCTAssertEqual(model.calibrationStep, .ready)
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
        XCTAssertEqual(model.state, .calibrated)
        XCTAssertEqual(model.message, "Tap Detect or enable Auto.")
        XCTAssertEqual(model.signalCalibrationReady, true)
        fake.set(tune: .measured(millivolts: 18_000))
        await model.performPrimaryAction()
        XCTAssertEqual(model.signalNormalizedFraction ?? -1, 0.1, accuracy: 0.0001)
        fake.set(tune: .measured(millivolts: 12_000))
        await model.performPrimaryAction()
        XCTAssertEqual(model.signalNormalizedFraction ?? -1, 0.9, accuracy: 0.0001)
    }

    func testRestartCalibrationClearsCalibrationAndTokenWithoutWriting() async {
        let fake = FakeProxmark()
        let model = RidesViewModel(device: fake)
        await calibrateFake(model, fake)
        await model.detect()
        XCTAssertNotNil(model.loadedToken)
        await model.restartCalibration()
        XCTAssertEqual(model.calibrationStep, .noToken)
        XCTAssertNil(model.noTokenSignalMillivolts)
        XCTAssertNil(model.tokenSignalMillivolts)
        XCTAssertNil(model.loadedToken)
        XCTAssertEqual(model.state, .idle)
        XCTAssertEqual(model.message, "Remove all tokens, then calibrate")
        XCTAssertEqual(fake.writeCallCount, 0)
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

    private func calibrateFake(_ model: RidesViewModel, _ fake: FakeProxmark, empty: Int = 18_000, present: Int = 420) async {
        fake.set(detect: .noChip, tune: .measured(millivolts: empty))
        await model.performPrimaryAction()
        fake.set(detect: .detected, tune: .measured(millivolts: present))
        await model.performPrimaryAction()
        fake.set(detect: .detected, tune: .measured(millivolts: present))
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
