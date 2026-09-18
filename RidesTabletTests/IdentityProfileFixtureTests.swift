import Foundation
import XCTest
@testable import RidesTablet

final class IdentityProfileFixtureTests: XCTestCase {
    func testFixtureHasStableV1Schema() throws {
        let root = try XCTUnwrap(try JSONSerialization.jsonObject(with: fixtureData()) as? [String: Any])
        XCTAssertEqual(Set(root.keys), Set(["schemaVersion", "fixtureId", "profiles"]))
        XCTAssertEqual(root["schemaVersion"] as? Int, 1)
        XCTAssertEqual(root["fixtureId"] as? String, "identity-profiles-v1")

        for item in try array(root["profiles"]) {
            let profile = try object(item)
            XCTAssertTrue(Set(profile.keys).isSuperset(of: [
                "friendlyName", "rideSequence", "tokenId",
                "block1", "block2", "block3", "block4",
                "canReset", "resetImageFileName"
            ]))
            if (profile["canReset"] as? Bool) == true {
                XCTAssertNotNil(profile["resetImage"])
            }
        }
    }

    func testFixtureMatchesResetSequenceOracle() throws {
        let fixture = try loadFixture()
        XCTAssertEqual(fixture.profiles.count, 13)

        for entry in fixture.profiles where entry.canReset {
            let sequence = try XCTUnwrap(RideSequenceRegistry.sequence(named: entry.rideSequence), entry.friendlyName)
            let oracle = ResetSequence.for(sequence)
            let image = oracle.resetImage()
            let fixtureImage = try entry.resetImage.map { blocks in
                try blocks.map { try parse($0) }
            }
            XCTAssertEqual(fixtureImage?.count, 8, entry.friendlyName)
            XCTAssertEqual(fixtureImage, image, entry.friendlyName)
            XCTAssertEqual(entry.block1, String(format: "%08X", oracle.block1), entry.friendlyName)
            XCTAssertEqual(entry.block2, String(format: "%08X", oracle.block2), entry.friendlyName)
            XCTAssertEqual(entry.block3, String(format: "%08X", oracle.block3), entry.friendlyName)
            XCTAssertEqual(entry.block4, String(format: "%08X", oracle.block4), entry.friendlyName)
            XCTAssertEqual(image[0], oracle.block0, entry.friendlyName)
            XCTAssertEqual(image[7], oracle.block7, entry.friendlyName)
        }

        let venus21ff = fixture.profiles.first { $0.friendlyName == "venus21ff" }!
        XCTAssertFalse(venus21ff.canReset)
        XCTAssertNil(venus21ff.resetImage)
    }

    func testMercuryResetImageUsesFiveHundredRides() {
        let image = ResetSequence.for(.mercury).resetImage()
        XCTAssertEqual(image[5], RideSequence.mercury.encode(500))
        XCTAssertEqual(image[6], RideSequence.mercury.encode(500))
    }

    private struct Fixture: Decodable {
        struct Profile: Decodable {
            let friendlyName: String
            let rideSequence: String
            let tokenId: String
            let block1: String
            let block2: String
            let block3: String
            let block4: String
            let canReset: Bool
            let resetImageFileName: String?
            let resetImage: [String]?
        }

        let schemaVersion: Int
        let fixtureId: String
        let profiles: [Profile]
    }

    private func fixtureData() throws -> Data {
        var directory = URL(fileURLWithPath: #filePath).deletingLastPathComponent()
        for _ in 0..<8 {
            let candidate = directory.appendingPathComponent("TestFixtures/IdentityProfiles/identity-profiles-v1.json")
            if FileManager.default.fileExists(atPath: candidate.path) {
                return try Data(contentsOf: candidate)
            }
            directory.deleteLastPathComponent()
        }
        throw NSError(domain: "IdentityProfileFixtureTests", code: 1)
    }

    private func loadFixture() throws -> Fixture {
        try JSONDecoder().decode(Fixture.self, from: fixtureData())
    }

    private func parse(_ value: String) throws -> UInt32 {
        guard let block = UInt32(value, radix: 16) else {
            throw NSError(domain: "IdentityProfileFixtureTests", code: 2)
        }
        return block
    }

    private func object(_ value: Any?) throws -> [String: Any] {
        guard let value, let object = value as? [String: Any] else {
            throw NSError(domain: "IdentityProfileFixtureTests", code: 3)
        }
        return object
    }

    private func array(_ value: Any?) throws -> [[String: Any]] {
        guard let value, let array = value as? [[String: Any]] else {
            throw NSError(domain: "IdentityProfileFixtureTests", code: 4)
        }
        return array
    }
}
