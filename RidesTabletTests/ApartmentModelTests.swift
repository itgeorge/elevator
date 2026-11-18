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
        XCTAssertEqual(try XCTUnwrap(context.model.activeChargeSession).charges.count, 1)
        XCTAssertEqual(try context.sessionStore.loadActiveSession().charges.count, 1)
        XCTAssertNil(context.model.apartmentNumber)
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
        XCTAssertEqual(context.model.completedCharges.count, 1)
        XCTAssertEqual(try context.sessionStore.loadActiveSession().charges.count, 1)
        XCTAssertEqual(ApartmentBlockCodec.decode(secret: secret,
                                                   block3: try XCTUnwrap(context.model.loadedToken).blocks[3],
                                                   block4: try XCTUnwrap(context.model.loadedToken).block4)?.apartment, 42)
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
