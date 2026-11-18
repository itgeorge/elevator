import Foundation
import XCTest
@testable import RidesTablet

final class ChargeSessionStoreTests: XCTestCase {
    func testRoundTripAndTotals() throws {
        let (store, directory) = makeStore()
        defer { try? FileManager.default.removeItem(at: directory) }
        let charge = makeCharge(rides: 25, amount: 0.75)
        var session = try store.createSession()
        session.charges.append(charge)
        try store.save(session)

        let loaded = try store.load(id: session.id)
        XCTAssertEqual(loaded.charges, [charge])
        XCTAssertEqual(loaded.amountTotal, Decimal(string: "0.75"))
        XCTAssertEqual(loaded.rideTotal, 25)
        XCTAssertGreaterThanOrEqual(loaded.updatedAt, session.createdAt)
    }

    func testReopensActiveSessionAndKeepsFileWhenCreatingNewSession() throws {
        let (store, directory) = makeStore()
        defer { try? FileManager.default.removeItem(at: directory) }
        var old = try store.loadActiveSession()
        old.charges = [makeCharge(rides: 10, amount: 0.30)]
        try store.save(old)

        let reopened = FileChargeSessionStore(directory: directory)
        XCTAssertEqual(try reopened.loadActiveSession().id, old.id)
        let newSession = try reopened.createSession()
        XCTAssertNotEqual(newSession.id, old.id)
        XCTAssertTrue(newSession.charges.isEmpty)
        XCTAssertEqual(try reopened.load(id: old.id).charges, old.charges)
        XCTAssertEqual(try reopened.loadActiveSession().id, newSession.id)
    }

    func testSwitchingActiveSessionUpdatesOnlySelectedFile() throws {
        let (store, directory) = makeStore()
        defer { try? FileManager.default.removeItem(at: directory) }
        let first = try store.createSession()
        let second = try store.createSession()
        try store.setActiveSession(id: first.id)

        var active = try store.loadActiveSession()
        active.charges.append(makeCharge(rides: 7, amount: 0.21))
        try store.save(active)

        XCTAssertEqual(try store.load(id: first.id).charges.count, 1)
        XCTAssertTrue(try store.load(id: second.id).charges.isEmpty)
        XCTAssertEqual(try FileChargeSessionStore(directory: directory).loadActiveSession().id, first.id)
    }

    func testRetainsMoreThanOneHundredCharges() throws {
        let (store, directory) = makeStore()
        defer { try? FileManager.default.removeItem(at: directory) }
        var session = try store.createSession()
        session.charges = (0..<125).map { _ in makeCharge(rides: 1, amount: 0.03) }
        try store.save(session)

        XCTAssertEqual(try store.load(id: session.id).charges.count, 125)
        XCTAssertEqual(try store.load(id: session.id).rideTotal, 125)
    }

    func testCorruptActiveSessionThrowsAndDoesNotReplaceFile() throws {
        let (store, directory) = makeStore()
        defer { try? FileManager.default.removeItem(at: directory) }
        let session = try store.createSession()
        let file = directory.appendingPathComponent("\(session.id.uuidString).json")
        try Data("not json".utf8).write(to: file)

        XCTAssertThrowsError(try store.loadActiveSession())
        XCTAssertEqual(try Data(contentsOf: file), Data("not json".utf8))
    }

    func testMissingRequestedSessionThrows() throws {
        let (store, directory) = makeStore()
        defer { try? FileManager.default.removeItem(at: directory) }
        XCTAssertThrowsError(try store.load(id: UUID()))
    }

    private func makeStore() -> (FileChargeSessionStore, URL) {
        let directory = FileManager.default.temporaryDirectory
            .appendingPathComponent("ChargeSessionStoreTests-\(UUID().uuidString)", isDirectory: true)
        return (FileChargeSessionStore(directory: directory), directory)
    }

    private func makeCharge(rides: Int, amount: Decimal) -> CompletedCharge {
        CompletedCharge(timestamp: Date(), sequence: .mercury, initialRides: 0,
                        addedRides: rides, totalRides: UInt(rides), amountEUR: amount)
    }
}
