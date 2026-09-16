import Foundation
import XCTest
@testable import RidesTablet

final class MercuryRideCodecTests: XCTestCase {
    func testFixtureHasStableSchemaVersionAndCompleteShape() throws {
        let data = try fixtureData()
        let root = try XCTUnwrap(try JSONSerialization.jsonObject(with: data) as? [String: Any])
        XCTAssertEqual(Set(root.keys), Set(["schemaVersion", "fixtureId", "sequence", "boundaries", "encodings", "rejectedEncodings", "malformedBlocks", "mirrorCases"]))
        XCTAssertEqual(root["schemaVersion"] as? Int, 1)
        XCTAssertEqual(root["fixtureId"] as? String, "mercury-v1")

        let sequence = try object(root["sequence"])
        XCTAssertEqual(Set(sequence.keys), Set(["name", "zeroBlock", "rotation", "minRides", "maxRides"]))

        for item in try array(root["encodings"]) {
            XCTAssertEqual(Set(try object(item).keys), Set(["rides", "block"]))
        }
        for item in try array(root["rejectedEncodings"]) {
            XCTAssertEqual(Set(try object(item).keys), Set(["rides", "block", "reason"]))
        }
        for item in try array(root["malformedBlocks"]) {
            XCTAssertEqual(Set(try object(item).keys), Set(["name", "block", "reason"]))
        }
        for item in try array(root["mirrorCases"]) {
            let mirror = try object(item)
            XCTAssertEqual(Set(mirror.keys), Set(["name", "block5", "block6", "expected"]))
            let expected = try object(mirror["expected"])
            XCTAssertEqual(Set(expected.keys), Set(["status", "rides", "sourceBlock", "sourceBlockNumber", "blocksMatched", "warningMessage"]))
        }
    }

    func testFixtureIsCompleteAndUsesMercuryMetadata() throws {
        let fixture = try loadFixture()
        XCTAssertEqual(fixture.schemaVersion, 1)
        XCTAssertEqual(fixture.fixtureId, "mercury-v1")
        XCTAssertEqual(fixture.sequence.name, "mercury")
        XCTAssertEqual(fixture.sequence.zeroBlock, "CCC749CC")
        XCTAssertEqual(fixture.sequence.rotation, 4)
        XCTAssertEqual(fixture.sequence.minRides, 0)
        XCTAssertEqual(fixture.sequence.maxRides, 500)
        XCTAssertEqual(fixture.boundaries, [0, 1, 7, 8, 127, 128, 255, 256, 383, 384, 500])
        XCTAssertEqual(Set(fixture.boundaries).count, fixture.boundaries.count)

        XCTAssertEqual(fixture.encodings.count, 501)
        XCTAssertEqual(Set(fixture.encodings.map(\.rides)), Set(Array(0...500).map(UInt.init)))
        XCTAssertEqual(Set(fixture.encodings.map(\.rides)).count, 501)
        XCTAssertEqual(fixture.rejectedEncodings.map(\.rides), Array(501...511).map(UInt.init))
        XCTAssertEqual(fixture.rejectedEncodings.count, 11)
        XCTAssertEqual(Set(fixture.malformedBlocks.map(\.name)).count, fixture.malformedBlocks.count)

        var allBlocks = Set<UInt32>()
        for entry in fixture.encodings {
            let block = try parse(entry.block)
            XCTAssertTrue(allBlocks.insert(block).inserted, "duplicate valid block for rides \(entry.rides)")
            XCTAssertEqual(entry.block, entry.block.uppercased())
            XCTAssertEqual(entry.block.count, 8)
            XCTAssertEqual(MercuryRideCodec.encode(entry.rides), block)
            XCTAssertEqual(MercuryRideCodec.decode(block), entry.rides)
        }

        let byRides = Dictionary(uniqueKeysWithValues: fixture.encodings.map { ($0.rides, $0.block) })
        for boundary in fixture.boundaries {
            XCTAssertEqual(byRides[boundary], fixture.encodings[Int(boundary)].block)
        }

        for entry in fixture.rejectedEncodings {
            let block = try parse(entry.block)
            XCTAssertTrue(allBlocks.insert(block).inserted, "rejected block duplicates another fixture block")
            XCTAssertEqual(entry.reason, "application-range")
            XCTAssertNil(MercuryRideCodec.encode(entry.rides))
            XCTAssertNil(MercuryRideCodec.decode(block))
        }

        for entry in fixture.malformedBlocks {
            let block = try parse(entry.block)
            XCTAssertTrue(allBlocks.insert(block).inserted, "malformed block duplicates another fixture block")
            XCTAssertEqual(entry.reason, "structural")
            XCTAssertNil(MercuryRideCodec.decode(block), entry.name)
        }
    }

    func testMercuryCodecRejectsOutOfRangeAndMalformedInputsDirectly() throws {
        XCTAssertEqual(MercuryRideCodec.encode(0), 0xCCC749CC)
        XCTAssertEqual(MercuryRideCodec.encode(500), 0x3FC6BD93)
        XCTAssertNil(MercuryRideCodec.encode(501))
        XCTAssertNil(MercuryRideCodec.encode(UInt.max))

        let fixture = try loadFixture()
        for entry in fixture.rejectedEncodings {
            XCTAssertNil(MercuryRideCodec.decode(try parse(entry.block)), entry.block)
        }
        for entry in fixture.malformedBlocks {
            XCTAssertNil(MercuryRideCodec.decode(try parse(entry.block)), entry.block)
        }
    }

    func testResolverMatchesEveryValidFixtureEncoding() throws {
        let fixture = try loadFixture()
        for entry in fixture.encodings {
            let block = try parse(entry.block)
            let result = MercuryMirrorResolver.resolve(block5: block, block6: block)
            XCTAssertEqual(result.status, .success, "\(entry.rides)")
            XCTAssertEqual(result.rides, entry.rides, "\(entry.rides)")
            XCTAssertEqual(result.sourceBlock, block, "\(entry.rides)")
            XCTAssertEqual(result.sourceBlockNumber, 5, "\(entry.rides)")
            XCTAssertTrue(result.blocksMatched, "\(entry.rides)")
            XCTAssertNil(result.warningMessage, "\(entry.rides)")
        }
    }

    func testResolverMatchesEveryRejectedAndMalformedFixtureEncoding() throws {
        let fixture = try loadFixture()
        for entry in fixture.rejectedEncodings {
            assertUnknownMatchingResult(for: try parse(entry.block), label: "\(entry.rides)")
        }
        for entry in fixture.malformedBlocks {
            assertUnknownMatchingResult(for: try parse(entry.block), label: entry.name)
        }
    }

    func testResolverMatchesEveryFixtureMirrorCaseIncludingMetadata() throws {
        let fixture = try loadFixture()
        XCTAssertEqual(Set(fixture.mirrorCases.map(\.name)), Set([
            "matching-valid", "only-block-5-valid", "only-block-6-valid",
            "both-valid-block-6-wins", "neither-valid"
        ]))

        for item in fixture.mirrorCases {
            let result = MercuryMirrorResolver.resolve(
                block5: try parse(item.block5),
                block6: try parse(item.block6)
            )
            XCTAssertEqual(result.status.rawValue, item.expected.status, item.name)
            XCTAssertEqual(result.rides, item.expected.rides, item.name)
            XCTAssertEqual(result.sourceBlock.map(Token.hex), item.expected.sourceBlock, item.name)
            XCTAssertEqual(result.sourceBlockNumber, item.expected.sourceBlockNumber, item.name)
            XCTAssertEqual(result.blocksMatched, item.expected.blocksMatched, item.name)
            XCTAssertEqual(result.warningMessage, item.expected.warningMessage, item.name)
        }
    }

    private func assertUnknownMatchingResult(for block: UInt32, label: String) {
        let result = MercuryMirrorResolver.resolve(block5: block, block6: block)
        XCTAssertEqual(result.status, .unknownEncodingSequence, label)
        XCTAssertNil(result.rides, label)
        XCTAssertEqual(result.sourceBlock, block, label)
        XCTAssertEqual(result.sourceBlockNumber, 5, label)
        XCTAssertTrue(result.blocksMatched, label)
        XCTAssertNil(result.warningMessage, label)
    }

    private func fixtureData() throws -> Data {
        var directory = URL(fileURLWithPath: #filePath).deletingLastPathComponent()
        for _ in 0..<8 {
            let candidate = directory.appendingPathComponent("TestFixtures/RideEncoding/mercury-v1.json")
            if FileManager.default.fileExists(atPath: candidate.path) {
                return try Data(contentsOf: candidate)
            }
            directory.deleteLastPathComponent()
        }
        throw NSError(domain: "MercuryRideCodecTests", code: 1, userInfo: [NSLocalizedDescriptionKey: "Could not locate mercury-v1.json in the repository."])
    }

    private func loadFixture() throws -> Fixture {
        try JSONDecoder().decode(Fixture.self, from: fixtureData())
    }

    private func parse(_ value: String) throws -> UInt32 {
        guard value.range(of: "^[0-9A-F]{8}$", options: .regularExpression) != nil,
              let block = UInt32(value, radix: 16) else {
            throw NSError(domain: "MercuryRideCodecTests", code: 2, userInfo: [NSLocalizedDescriptionKey: "Invalid block \(value)"])
        }
        return block
    }

    private func object(_ value: Any?) throws -> [String: Any] {
        guard let value, let object = value as? [String: Any] else {
            throw NSError(domain: "MercuryRideCodecTests", code: 3, userInfo: [NSLocalizedDescriptionKey: "Expected JSON object"])
        }
        return object
    }

    private func array(_ value: Any?) throws -> [[String: Any]] {
        guard let value, let array = value as? [[String: Any]] else {
            throw NSError(domain: "MercuryRideCodecTests", code: 4, userInfo: [NSLocalizedDescriptionKey: "Expected JSON object array"])
        }
        return array
    }
}

private struct Fixture: Decodable {
    let schemaVersion: Int
    let fixtureId: String
    let sequence: Sequence
    let boundaries: [UInt]
    let encodings: [Encoding]
    let rejectedEncodings: [RejectedEncoding]
    let malformedBlocks: [MalformedBlock]
    let mirrorCases: [MirrorCase]
}

private struct Sequence: Decodable {
    let name: String
    let zeroBlock: String
    let rotation: UInt8
    let minRides: UInt
    let maxRides: UInt
}

private struct Encoding: Decodable {
    let rides: UInt
    let block: String
}

private struct RejectedEncoding: Decodable {
    let rides: UInt
    let block: String
    let reason: String
}

private struct MalformedBlock: Decodable {
    let name: String
    let block: String
    let reason: String
}

private struct MirrorCase: Decodable {
    let name: String
    let block5: String
    let block6: String
    let expected: Expected
}

private struct Expected: Decodable {
    let status: String
    let rides: UInt?
    let sourceBlock: String?
    let sourceBlockNumber: Int?
    let blocksMatched: Bool
    let warningMessage: String?
}
