import XCTest
@testable import RidesTablet

@MainActor
final class ChargeSessionModelTests: XCTestCase {
    func testNewSessionPreservesOldFileAndDoesNotChangeLoadedToken() async throws {
        let directory = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        defer { try? FileManager.default.removeItem(at: directory) }
        let store = FileChargeSessionStore(directory: directory)
        let model = RidesViewModel(device: FakeProxmark(), sessionStore: store)
        await model.detect()
        model.adjustRides(by: 10)
        await model.charge()
        let originalSession = try XCTUnwrap(model.activeChargeSession)
        let token = model.loadedToken

        model.startNewChargeSession()

        XCTAssertTrue(model.completedCharges.isEmpty)
        XCTAssertNotEqual(model.activeChargeSession?.id, originalSession.id)
        XCTAssertEqual(model.loadedToken, token)
        XCTAssertEqual(try store.load(id: originalSession.id).charges.count, 1)
        XCTAssertEqual(try store.listSessions().count, 2)
    }

    func testLoadingSessionAppendsToItsFileAndReopensAfterRestart() async throws {
        let directory = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        defer { try? FileManager.default.removeItem(at: directory) }
        let store = FileChargeSessionStore(directory: directory)
        let fake = FakeProxmark()
        let model = RidesViewModel(device: fake, sessionStore: store)
        await model.detect()
        model.adjustRides(by: 10)
        await model.charge()
        let first = try XCTUnwrap(model.activeChargeSession)
        model.startNewChargeSession()
        let second = try XCTUnwrap(model.activeChargeSession)

        model.loadChargeSession(first)
        model.adjustRides(by: 10)
        await model.charge()

        XCTAssertEqual(model.completedCharges.count, 2)
        XCTAssertEqual(try store.load(id: first.id).charges.count, 2)
        XCTAssertTrue(try store.load(id: second.id).charges.isEmpty)
        let reopened = RidesViewModel(device: FakeProxmark(), sessionStore: FileChargeSessionStore(directory: directory))
        XCTAssertEqual(reopened.activeChargeSession?.id, first.id)
        XCTAssertEqual(reopened.completedCharges, model.completedCharges)
    }

    func testFileFailureAfterPhysicalChargeIsReportedWithoutRetryingCharge() async throws {
        let directory = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        defer { try? FileManager.default.removeItem(at: directory) }
        let fake = FakeProxmark()
        let model = RidesViewModel(device: fake, sessionStore: FileChargeSessionStore(directory: directory))
        await model.detect()
        model.adjustRides(by: 10)
        try FileManager.default.removeItem(at: directory)
        try Data("not a directory".utf8).write(to: directory)

        await model.charge()

        XCTAssertEqual(fake.writeCallCount, 1)
        XCTAssertEqual(model.currentRides, 83)
        XCTAssertEqual(model.completedCharges.count, 1)
        XCTAssertNotNil(model.sessionError)
        XCTAssertFalse(model.canCharge)
    }
}
