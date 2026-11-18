import CryptoKit
import XCTest
@testable import RidesTablet

@MainActor
final class ApartmentModelTests: XCTestCase {
    private let secret = "apartment-secret"

    func testRequestChargePromptsForMissingApartmentWithoutWriting() async throws {
        let context = try await makeModel(secret: secret)
        defer { try? FileManager.default.removeItem(at: context.directory) }
        context.model.adjustRides(by: 10)

        await context.model.requestCharge()

        XCTAssertTrue(context.model.isApartmentSheetPresented)
        XCTAssertEqual(context.fake.writeCallCount, 0)
        XCTAssertEqual(context.fake.writeRideMirrorsCallCount, 0)
        XCTAssertTrue(context.model.completedCharges.isEmpty)
    }

    func testCancelPreservesPendingRidesAndDoesNotWrite() async throws {
        let context = try await makeModel(secret: secret)
        defer { try? FileManager.default.removeItem(at: context.directory) }
        context.model.adjustRides(by: 10)
        let pending = context.model.pendingRides
        await context.model.requestCharge()
        await context.model.cancelApartmentEntry()

        XCTAssertFalse(context.model.isApartmentSheetPresented)
        XCTAssertEqual(context.model.pendingRides, pending)
        XCTAssertEqual(context.fake.writeCallCount, 0)
        XCTAssertTrue(context.model.completedCharges.isEmpty)
    }

    func testOutsideDismissCancelsEditAndSkipsChargeExactlyOnce() async throws {
        let edit = try await makeModel(secret: secret, apartment: 17)
        defer { try? FileManager.default.removeItem(at: edit.directory) }
        edit.model.adjustRides(by: 10)
        let pending = edit.model.pendingRides
        await edit.model.editApartment()
        await edit.model.dismissApartmentEntry()
        await edit.model.dismissApartmentEntry()
        XCTAssertEqual(edit.model.pendingRides, pending)
        XCTAssertTrue(edit.model.completedCharges.isEmpty)
        XCTAssertEqual(edit.fake.writeCallCount, 0)

        let charge = try await makeModel(secret: secret)
        defer { try? FileManager.default.removeItem(at: charge.directory) }
        charge.model.adjustRides(by: 10)
        await charge.model.requestCharge()
        await charge.model.dismissApartmentEntry()
        await charge.model.dismissApartmentEntry()
        XCTAssertEqual(charge.fake.writeRideMirrorsCallCount, 1)
        XCTAssertEqual(charge.model.completedCharges.count, 1)
    }

    func testSkippingApartmentPerformsOneChargeAndPersistsOneHistoryEntry() async throws {
        let context = try await makeModel(secret: secret)
        defer { try? FileManager.default.removeItem(at: context.directory) }
        context.model.adjustRides(by: 10)
        await context.model.requestCharge()
        await context.model.skipApartmentEntry()

        XCTAssertEqual(context.fake.writeRideMirrorsCallCount, 1)
        XCTAssertEqual(context.fake.writeCallCount, 1)
        XCTAssertEqual(context.fake.lastWrittenToken?.rideCount, context.model.currentRides)
        XCTAssertEqual(context.model.completedCharges.count, 1)
        XCTAssertNil(context.model.completedCharges.first?.apartmentNumber)
        XCTAssertEqual(try XCTUnwrap(context.model.activeChargeSession).charges.count, 1)
        XCTAssertEqual(try context.sessionStore.loadActiveSession().charges.count, 1)
        XCTAssertNil(context.model.apartmentNumber)
    }

    func testNormalChargeStoresKnownApartmentSnapshot() async throws {
        let context = try await makeModel(secret: secret, apartment: 17)
        defer { try? FileManager.default.removeItem(at: context.directory) }
        context.model.adjustRides(by: 10)
        await context.model.requestCharge()
        XCTAssertFalse(context.model.isApartmentSheetPresented)
        XCTAssertEqual(context.model.completedCharges.first?.apartmentNumber, 17)
        XCTAssertEqual(context.fake.writeRideMirrorsCallCount, 1)
    }

    func testSavingApartmentAndChargeUsesOnePhysicalWriteAndOneHistoryEntry() async throws {
        let context = try await makeModel(secret: secret)
        defer { try? FileManager.default.removeItem(at: context.directory) }
        context.model.adjustRides(by: 10)
        await context.model.requestCharge()
        context.model.apartmentInput = "42"
        await context.model.saveApartmentEntry()

        XCTAssertEqual(context.fake.writeCallCount, 1)
        XCTAssertEqual(context.fake.writeRideMirrorsCallCount, 0)
        XCTAssertEqual(context.fake.lastWrittenToken?.rideCount, context.model.currentRides)
        XCTAssertEqual(context.model.apartmentNumber, 42)
        XCTAssertEqual(context.model.completedCharges.first?.apartmentNumber, 42)
        XCTAssertEqual(context.model.completedCharges.count, 1)
        XCTAssertEqual(try context.sessionStore.loadActiveSession().charges.count, 1)
        XCTAssertEqual(ApartmentBlockCodec.decode(secret: secret,
                                                   block3: try XCTUnwrap(context.model.loadedToken).blocks[3],
                                                   block4: try XCTUnwrap(context.model.loadedToken).block4)?.apartment, 42)
    }

    func testHeldApartmentSaveDismissesAndBlocksDuplicateActions() async throws {
        let directory = FileManager.default.temporaryDirectory.appendingPathComponent("ApartmentHeldWrite-\(UUID().uuidString)")
        defer { try? FileManager.default.removeItem(at: directory) }
        let token = Token.sample(rideCount: 73)
        let device = HeldApartmentWriteDevice(token: token)
        let model = RidesViewModel(device: device, sessionStore: FileChargeSessionStore(directory: directory),
                                   apartmentSecretStore: ApartmentSecretStore(secret: secret))
        await model.performPrimaryAction()
        await model.performPrimaryAction()
        XCTAssertTrue(model.signalCalibrationReady)
        await model.detect()
        model.adjustRides(by: 10)
        model.toggleAutoScanning()
        XCTAssertTrue(model.isAutoScanning)
        await model.requestCharge()
        model.apartmentInput = "42"

        let saveTask = Task { await model.saveApartmentEntry() }
        await device.waitUntilWriteStarts()
        XCTAssertFalse(model.isApartmentSheetPresented)
        XCTAssertTrue(model.isBusy)
        XCTAssertFalse(model.isAutoScanning)

        await model.saveApartmentEntry()
        await model.dismissApartmentEntry()
        await model.cancelApartmentEntry()
        let writesWhileHeld = await device.writeCount
        XCTAssertEqual(writesWhileHeld, 1)
        XCTAssertFalse(model.isAutoScanning)
        XCTAssertTrue(model.completedCharges.isEmpty)

        await device.completeWrite()
        await saveTask.value
        XCTAssertTrue(model.isAutoScanning)
        XCTAssertFalse(model.isBusy)
        XCTAssertEqual(model.completedCharges.count, 1)
        XCTAssertEqual(model.completedCharges.first?.apartmentNumber, 42)
        XCTAssertEqual(model.activeChargeSession?.charges.count, 1)
        XCTAssertEqual(try XCTUnwrap(model.loadedToken).rideCount, model.currentRides)
        let finalWrites = await device.writeCount
        XCTAssertEqual(finalWrites, 1)
        model.stopAutoScanning()
    }

    func testExplicitAbsentPresenceBeforeSaveInvalidatesPopupAndPreventsWrite() async throws {
        let context = try await makeModel(secret: secret, apartment: 17)
        defer { try? FileManager.default.removeItem(at: context.directory) }
        await context.model.editApartment()
        context.model.apartmentInput = "18"
        context.fake.set(detect: .noChip)
        for _ in 0..<100 where context.model.isApartmentSheetPresented { await Task.yield() }
        try await Task.sleep(for: .milliseconds(300))
        XCTAssertFalse(context.model.isApartmentSheetPresented)
        await context.model.saveApartmentEntry()
        XCTAssertEqual(context.fake.writeCallCount, 0)
        XCTAssertNil(context.model.loadedToken)
        XCTAssertTrue(context.model.completedCharges.isEmpty)
    }

    func testEditingPrefilledApartmentSavesOnlyApartmentAndPreservesPendingRides() async throws {
        let context = try await makeModel(secret: secret, apartment: 17)
        defer { try? FileManager.default.removeItem(at: context.directory) }
        context.model.adjustRides(by: 10)
        let pending = context.model.pendingRides
        await context.model.editApartment()

        XCTAssertTrue(context.model.isEditingApartment)
        XCTAssertEqual(context.model.apartmentInput, "17")
        context.model.apartmentInput = "18"
        await context.model.saveApartmentEntry()

        XCTAssertEqual(context.fake.writeCallCount, 1)
        XCTAssertEqual(context.fake.writeRideMirrorsCallCount, 0)
        XCTAssertEqual(context.model.pendingRides, pending)
        XCTAssertEqual(context.model.currentRides, 73)
        XCTAssertEqual(context.model.apartmentNumber, 18)
        XCTAssertTrue(context.model.completedCharges.isEmpty)
        XCTAssertTrue(try context.sessionStore.loadActiveSession().charges.isEmpty)
    }

    func testWrongOrClearedSecretHidesApartmentNumber() async throws {
        let context = try await makeModel(secret: secret, apartment: 9)
        defer { try? FileManager.default.removeItem(at: context.directory) }
        XCTAssertEqual(context.model.apartmentNumber, 9)

        context.secretStore.setSecret("wrong-secret")
        XCTAssertNil(context.model.apartmentNumber)
        context.secretStore.clear()
        XCTAssertNil(context.model.apartmentNumber)

        context.secretStore.setSecret(secret)
        XCTAssertEqual(context.model.apartmentNumber, 9)

        let noSecret = try await makeModel(secret: nil, apartment: 9)
        defer { try? FileManager.default.removeItem(at: noSecret.directory) }
        XCTAssertNil(noSecret.model.apartmentNumber)
    }

    func testClearAfterRepeatedIncreasesReturnsToCurrentWithZeroCostAndNoWrite() async throws {
        let context = try await makeModel(secret: nil, rides: 200)
        defer { try? FileManager.default.removeItem(at: context.directory) }
        context.model.adjustRides(by: 10)
        context.model.adjustRides(by: 10)
        context.model.adjustRides(by: 10)
        XCTAssertEqual(context.model.pendingRides, 230)
        context.model.clearPendingRides()

        XCTAssertEqual(context.model.pendingRides, 200)
        XCTAssertEqual(context.model.costEUR, .zero)
        await context.model.charge()
        XCTAssertEqual(context.fake.writeCallCount, 0)
        XCTAssertTrue(context.model.completedCharges.isEmpty)
    }

    func testApartmentCodecMatchesDeterministicVectorAndSupportsByteBounds() throws {
        let block3: UInt32 = 0x12345678
        let vector = ApartmentBlockCodec.encode(secret: secret, block3: block3, apartment: 255)
        XCTAssertEqual(vector, 0x01C74CC2)
        XCTAssertEqual(ApartmentBlockCodec.decode(secret: secret, block3: block3, block4: vector)?.apartment, 255)

        let bounds: [UInt8] = [0, 255]
        for apartment in bounds {
            let encoded = ApartmentBlockCodec.encode(secret: secret, block3: block3, apartment: apartment)
            XCTAssertEqual(ApartmentBlockCodec.decode(secret: secret, block3: block3, block4: encoded)?.apartment, apartment)
        }
    }

    func testSecretFingerprintIsStableAndHashesExactUTF8Bytes() {
        let value = " secret with spaces "
        let store = ApartmentSecretStore(secret: value)
        let independentlyComputed = SHA256.hash(data: Data(value.utf8))
            .map { String(format: "%02x", $0) }.joined()

        XCTAssertEqual(store.sha256Fingerprint, String(independentlyComputed.prefix(16)))
        XCTAssertEqual(store.sha256Fingerprint?.count, 16)
        XCTAssertEqual(store.sha256Fingerprint, ApartmentSecretStore(secret: value).sha256Fingerprint)
        XCTAssertNotEqual(store.sha256Fingerprint, ApartmentSecretStore(secret: value.trimmingCharacters(in: .whitespaces)).sha256Fingerprint)
    }

    func testSecretFingerprintClearsWithSecret() {
        let store = ApartmentSecretStore()
        XCTAssertNil(store.sha256Fingerprint)
        store.setSecret("\tsecret\n")
        XCTAssertNotNil(store.sha256Fingerprint)
        store.clear()
        XCTAssertNil(store.secret)
        XCTAssertNil(store.sha256Fingerprint)
    }

    func testSecretIsMemoryOnlyAndPreservesExactEnteredBytes() {
        let store = ApartmentSecretStore()
        XCTAssertNil(store.secret)
        store.setSecret(" secret with spaces ")
        XCTAssertEqual(store.secret, " secret with spaces ")
        XCTAssertNil(ApartmentSecretStore().secret)
        store.clear()
        XCTAssertNil(store.secret)
    }

    func testApartmentPromptRestoresAutoAfterCancelSkipAndSave() async throws {
        for action in ["cancel", "skip", "save"] {
            let context = try await makeModel(secret: secret)
            defer { try? FileManager.default.removeItem(at: context.directory) }
            let model = context.model
            await model.restartCalibration()
            context.fake.set(detect: .noChip, tune: .measured(millivolts: 18_000))
            await model.performPrimaryAction()
            context.fake.set(detect: .detected, tune: .measured(millivolts: 420))
            await model.performPrimaryAction()
            await model.detect()
            model.adjustRides(by: 10)
            model.toggleAutoScanning()
            XCTAssertTrue(model.isAutoScanning)
            await model.requestCharge()
            XCTAssertFalse(model.isAutoScanning)
            switch action {
            case "skip": await model.skipApartmentEntry()
            case "save":
                model.apartmentInput = "42"
                await model.saveApartmentEntry()
            default: await model.cancelApartmentEntry()
            }
            XCTAssertTrue(model.isAutoScanning, action)
            model.stopAutoScanning()
        }
    }

    private func makeModel(secret: String?, apartment: UInt8? = nil, rides: UInt = 73) async throws -> Context {
        let directory = FileManager.default.temporaryDirectory.appendingPathComponent("ApartmentModelTests-\(UUID().uuidString)")
        let sessionStore = FileChargeSessionStore(directory: directory)
        let secretStore = ApartmentSecretStore(secret: secret)
        var token = Token.sample(rideCount: rides)
        if let apartment {
            var blocks = token.blocks
            blocks[4] = ApartmentBlockCodec.encode(secret: self.secret, block3: blocks[3], apartment: apartment)
            token = Token(blocks: blocks, rideCount: rides, sequence: token.sequence)
        }
        let fake = FakeProxmark(read: .known(token))
        let model = RidesViewModel(device: fake, sessionStore: sessionStore, apartmentSecretStore: secretStore)
        await model.detect()
        return Context(model: model, fake: fake, sessionStore: sessionStore, secretStore: secretStore, directory: directory)
    }

    private struct Context {
        let model: RidesViewModel
        let fake: FakeProxmark
        let sessionStore: FileChargeSessionStore
        let secretStore: ApartmentSecretStore
        let directory: URL
    }
}

private actor HeldApartmentWriteDevice: RideTokenDevice {
    private let token: Token
    private var writeContinuation: CheckedContinuation<WriteOutcome, Never>?
    private var startedContinuation: CheckedContinuation<Void, Never>?
    private var didStart = false
    private var calibrationSamples = 0
    private(set) var writeCount = 0

    init(token: Token) { self.token = token }

    func scan() async -> ScanOutcome { .known(token, signalMillivolts: 420) }
    func measureTokenPresence() async -> TokenPresenceOutcome { .present }
    func scanForApartmentSnapshot() async -> ScanOutcome { .known(token, signalMillivolts: 420) }
    func measureCalibrationSignal() async -> CalibrationSignalOutcome {
        calibrationSamples += 1
        return calibrationSamples == 1
            ? .measured(millivolts: 18_000, chipPresent: false)
            : .measured(millivolts: 420, chipPresent: true)
    }
    func measureSignal() async -> TuneOutcome { .measured(millivolts: 420) }
    func writeRideMirrors(_ request: RideMirrorWriteRequest) async -> WriteOutcome { .failure("Unexpected ride-only write") }
    func reset(_ request: ResetMutationRequest) async -> ResetOutcome { .failure("Unexpected reset") }

    func writeApartment(_ request: ApartmentMutationRequest) async -> WriteOutcome {
        writeCount += 1
        didStart = true
        startedContinuation?.resume()
        startedContinuation = nil
        return await withCheckedContinuation { writeContinuation = $0 }
    }

    func waitUntilWriteStarts() async {
        if didStart { return }
        await withCheckedContinuation { startedContinuation = $0 }
    }

    func completeWrite() {
        writeContinuation?.resume(returning: .success)
        writeContinuation = nil
    }
}
