import Foundation
import XCTest
@testable import RidesTablet

final class RideEncodingFixtureTests: XCTestCase {
    private static let expectedSequenceNames = [
        "mercury", "venus", "earth", "pluto", "mars",
        "jupiter", "saturn", "uranus", "neptune", "charon", "nix",
    ]

    func testFixtureHasStableV2SchemaAndShape() throws {
        let data = try fixtureData()
        let root = try XCTUnwrap(try JSONSerialization.jsonObject(with: data) as? [String: Any])
        XCTAssertEqual(
            Set(root.keys),
            Set(["schemaVersion", "fixtureId", "boundaries", "sequences", "rejectedEncodings", "malformedBlocks", "mirrorCases"])
        )
        XCTAssertEqual(root["schemaVersion"] as? Int, 2)
        XCTAssertEqual(root["fixtureId"] as? String, "ride-encoding-v2")

        for item in try array(root["sequences"]) {
            let sequence = try object(item)
            XCTAssertEqual(
                Set(sequence.keys),
                Set(["name", "zeroBlock", "rotation", "minRides", "maxRides", "encodings"])
            )
            for entry in try array(sequence["encodings"]) {
                XCTAssertEqual(Set(try object(entry).keys), Set(["rides", "block"]))
            }
        }

        for item in try array(root["rejectedEncodings"]) {
            XCTAssertEqual(Set(try object(item).keys), Set(["sequence", "rides", "block", "reason"]))
        }
        for item in try array(root["malformedBlocks"]) {
            XCTAssertEqual(Set(try object(item).keys), Set(["name", "block", "reason"]))
        }
        for item in try array(root["mirrorCases"]) {
            let mirror = try object(item)
            XCTAssertEqual(Set(mirror.keys), Set(["name", "block5", "block6", "expected"]))
            let expected = try object(mirror["expected"])
            XCTAssertEqual(
                Set(expected.keys),
                Set(["status", "rides", "sourceBlock", "sourceBlockNumber", "blocksMatched", "warningMessage"])
            )
        }
    }

    func testFixtureMatchesEveryRegisteredSequenceAndEncoding() throws {
        let fixture = try loadFixture()
        XCTAssertEqual(fixture.boundaries, [0, 1, 7, 8, 127, 128, 255, 256, 383, 384, 500])
        XCTAssertEqual(Set(fixture.boundaries).count, fixture.boundaries.count)
        XCTAssertEqual(fixture.sequences.map(\.name), Self.expectedSequenceNames)
        XCTAssertEqual(Set(fixture.sequences.map(\.name)).count, fixture.sequences.count)

        var globalBlocks: [UInt32: (String, UInt)] = [:]
        for section in fixture.sequences {
            let oracle = try XCTUnwrap(RideSequenceRegistry.sequence(named: section.name), section.name)

            XCTAssertEqual(section.zeroBlock, String(format: "%08X", oracle.zeroBlock), section.name)
            XCTAssertEqual(section.rotation, oracle.rotation, section.name)
            XCTAssertEqual(section.minRides, oracle.minRides, section.name)
            XCTAssertEqual(section.maxRides, oracle.maxRides, section.name)
            XCTAssertEqual(section.encodings.count, 501, section.name)

            let entriesByRide = Dictionary(uniqueKeysWithValues: section.encodings.map { ($0.rides, $0.block) })
            XCTAssertEqual(Set(entriesByRide.keys), Set((0...500).map(UInt.init)), section.name)

            var localBlocks = Set<UInt32>()
            for entry in section.encodings {
                let block = try parse(entry.block)
                XCTAssertTrue(localBlocks.insert(block).inserted, "\(section.name)/\(entry.rides) duplicate within sequence")
                XCTAssertEqual(entry.block, entry.block.uppercased())
                XCTAssertEqual(entry.block.count, 8)
                XCTAssertEqual(oracle.encode(entry.rides), block, "fixture/\(section.name)/\(entry.rides)")
                XCTAssertEqual(oracle.decode(block), entry.rides, "decode/\(section.name)/\(entry.rides)")

                if let other = globalBlocks[block] {
                    XCTFail("cross-sequence collision: \(section.name)/\(entry.rides) and \(other.0)/\(other.1) encode as \(entry.block)")
                } else {
                    globalBlocks[block] = (section.name, entry.rides)
                }
            }

            for boundary in fixture.boundaries {
                XCTAssertEqual(entriesByRide[boundary], section.encodings[Int(boundary)].block, "\(section.name) boundary \(boundary)")
            }
        }
    }

    func testFixtureRejectedEntriesAreStructurallyValidButOutOfApplicationRange() throws {
        let fixture = try loadFixture()
        var knownBlocks = try collectAllFixtureBlocks(fixture)

        XCTAssertEqual(fixture.rejectedEncodings.count, 11 * Self.expectedSequenceNames.count)
        XCTAssertEqual(
            Set(fixture.rejectedEncodings.map(\.sequence)),
            Set(Self.expectedSequenceNames)
        )

        for sequenceName in Self.expectedSequenceNames {
            let rejected = fixture.rejectedEncodings.filter { $0.sequence == sequenceName }
            XCTAssertEqual(rejected.count, 11, sequenceName)
            XCTAssertEqual(rejected.map(\.rides), Array((501...511).map(UInt.init)), sequenceName)
        }

        for entry in fixture.rejectedEncodings {
            let oracle = try XCTUnwrap(RideSequenceRegistry.sequence(named: entry.sequence), entry.sequence)
            let block = try parse(entry.block)

            XCTAssertTrue(knownBlocks.insert(block).inserted, "rejected block duplicates another fixture block \(entry.sequence)/\(entry.rides)")
            XCTAssertEqual(entry.reason, "application-range")
            XCTAssertNil(oracle.encode(entry.rides), "rejected/\(entry.sequence)/\(entry.rides)")
            XCTAssertNil(oracle.decode(block), "rejected/\(entry.sequence)/\(entry.rides)")
            XCTAssertNil(RideSequenceRegistry.tryDecode(block), "rejected/\(entry.sequence)/\(entry.rides)")

            let diagnosticRides = try XCTUnwrap(
                RideCounterCodec.decode(zeroBlock: oracle.zeroBlock, rotation: oracle.rotation, block: block),
                "rejected/\(entry.sequence)/\(entry.rides) must be structurally valid"
            )
            XCTAssertEqual(diagnosticRides, entry.rides)
            XCTAssertEqual(
                RideCounterCodec.encode(zeroBlock: oracle.zeroBlock, rotation: oracle.rotation, rides: entry.rides),
                block
            )
        }
    }

    func testFixtureMalformedBlocksAreStructurallyInvalid() throws {
        let fixture = try loadFixture()
        var knownBlocks = try collectAllFixtureBlocks(fixture)

        XCTAssertEqual(Set(fixture.malformedBlocks.map(\.name)).count, fixture.malformedBlocks.count)

        for entry in fixture.malformedBlocks {
            let block = try parse(entry.block)
            XCTAssertTrue(knownBlocks.insert(block).inserted, "duplicate malformed block \(entry.name)")
            XCTAssertEqual(entry.reason, "structural")
            XCTAssertNil(RideSequenceRegistry.tryDecode(block), "malformed/\(entry.name)")
        }
    }

    func testResolverMatchesEveryCompleteFixtureEntryForAllSequences() throws {
        let fixture = try loadFixture()
        XCTAssertEqual(fixture.sequences.count, 11)

        for section in fixture.sequences {
            XCTAssertEqual(section.encodings.count, 501, section.name)
            for entry in section.encodings {
                let block = try parse(entry.block)
                let result = RideBlockResolver.resolve(block5: block, block6: block)

                XCTAssertEqual(result.status, .success, "\(section.name)/\(entry.rides)")
                XCTAssertEqual(result.rides, entry.rides, "\(section.name)/\(entry.rides)")
                XCTAssertEqual(result.sequence?.rawValue, section.name, "\(section.name)/\(entry.rides)")
                XCTAssertEqual(result.sourceBlock, block, "\(section.name)/\(entry.rides)")
                XCTAssertEqual(result.sourceBlockNumber, 5, "\(section.name)/\(entry.rides)")
                XCTAssertTrue(result.blocksMatched, "\(section.name)/\(entry.rides)")
                XCTAssertNil(result.warningMessage, "\(section.name)/\(entry.rides)")
            }
        }
    }

    func testResolverRejectsAllStructurallyValidButOutOfApplicationRangeEntries() throws {
        let fixture = try loadFixture()
        XCTAssertEqual(fixture.rejectedEncodings.count, 121)

        for entry in fixture.rejectedEncodings {
            let block = try parse(entry.block)
            let result = RideBlockResolver.resolve(block5: block, block6: block)

            XCTAssertEqual(result.status, .unknownEncodingSequence, "\(entry.sequence)/\(entry.rides)")
            XCTAssertNil(result.rides, "\(entry.sequence)/\(entry.rides)")
            XCTAssertNil(result.sequence, "\(entry.sequence)/\(entry.rides)")
            XCTAssertEqual(result.sourceBlock, block, "\(entry.sequence)/\(entry.rides)")
            XCTAssertEqual(result.sourceBlockNumber, 5, "\(entry.sequence)/\(entry.rides)")
            XCTAssertTrue(result.blocksMatched, "\(entry.sequence)/\(entry.rides)")
            XCTAssertNil(result.warningMessage, "\(entry.sequence)/\(entry.rides)")
        }
    }

    func testResolverRejectsEveryStructurallyMalformedFixtureEntry() throws {
        let fixture = try loadFixture()

        for entry in fixture.malformedBlocks {
            let block = try parse(entry.block)
            let result = RideBlockResolver.resolve(block5: block, block6: block)

            XCTAssertEqual(result.status, .unknownEncodingSequence, entry.name)
            XCTAssertNil(result.rides, entry.name)
            XCTAssertNil(result.sequence, entry.name)
            XCTAssertEqual(result.sourceBlock, block, entry.name)
            XCTAssertEqual(result.sourceBlockNumber, 5, entry.name)
            XCTAssertTrue(result.blocksMatched, entry.name)
            XCTAssertNil(result.warningMessage, entry.name)
        }
    }

    func testResolverMatchesEveryFixtureMirrorCaseIncludingSequenceAndMetadata() throws {
        let fixture = try loadFixture()
        XCTAssertEqual(
            Set(fixture.mirrorCases.map(\.name)),
            Set([
                "mercury-matching-valid",
                "mercury-only-block-5-valid",
                "mercury-only-block-6-valid",
                "mercury-both-valid-block-6-wins",
                "mercury-neither-valid",
                "venus-both-valid-block-6-wins",
                "jupiter-matching-valid",
                "neptune-matching-valid",
                "neither-valid-unrelated",
            ])
        )

        for item in fixture.mirrorCases {
            let result = RideBlockResolver.resolve(
                block5: try parse(item.block5),
                block6: try parse(item.block6)
            )
            XCTAssertEqual(result.status.rawValue, item.expected.status, item.name)
            XCTAssertEqual(result.rides, item.expected.rides, item.name)
            XCTAssertEqual(result.sourceBlock.map(Token.hex), item.expected.sourceBlock, item.name)
            XCTAssertEqual(result.sourceBlockNumber, item.expected.sourceBlockNumber, item.name)
            XCTAssertEqual(result.blocksMatched, item.expected.blocksMatched, item.name)
            XCTAssertEqual(result.warningMessage, item.expected.warningMessage, item.name)

            if result.status == .success {
                XCTAssertEqual(result.sequence?.rawValue, expectedMirrorSequenceName(item.name), item.name)
            } else {
                XCTAssertNil(result.sequence, item.name)
            }
        }
    }

    func testRegistryHasNoDuplicateNamesOrEncodedCollisions() {
        XCTAssertEqual(RideSequenceRegistry.all.count, 11)
        XCTAssertEqual(Set(RideSequenceRegistry.all.map(\.rawValue)).count, 11)

        var blocks: [UInt32: (RideSequence, UInt)] = [:]
        for sequence in RideSequenceRegistry.all {
            for rides in sequence.minRides...sequence.maxRides {
                guard let block = sequence.encode(rides) else {
                    XCTFail("Failed to encode \(sequence.rawValue)/\(rides)")
                    continue
                }
                if let other = blocks[block] {
                    XCTFail("collision: \(sequence.rawValue)/\(rides) and \(other.0.rawValue)/\(other.1) -> \(Token.hex(block))")
                } else {
                    blocks[block] = (sequence, rides)
                }
            }
        }
        XCTAssertEqual(blocks.count, 11 * 501)
    }

    func testRegistryDecodeRejectsHighWordGuessingAndMalformedPayloads() throws {
        XCTAssertNil(RideSequenceRegistry.tryDecode(0xDEAD1234))
        XCTAssertNil(RideSequenceRegistry.tryDecode(0xCCC70000))
        XCTAssertNil(RideSequenceRegistry.tryDecode(0x3FC6BC83))

        let fixture = try loadFixture()
        for entry in fixture.malformedBlocks {
            XCTAssertNil(RideSequenceRegistry.tryDecode(try parse(entry.block)), entry.name)
        }
    }

    func testBoundaryRegressionsForRotation0AndRotation4() throws {
        let fixture = try loadFixture()
        let rotation4 = try XCTUnwrap(fixture.sequences.first { $0.name == "mercury" })
        let rotation0 = try XCTUnwrap(fixture.sequences.first { $0.name == "jupiter" })
        let boundaries: [UInt] = [7, 8, 127, 128, 255, 256, 383, 384]

        for boundary in boundaries {
            for section in [rotation4, rotation0] {
                let entry = section.encodings[Int(boundary)]
                let block = try parse(entry.block)
                let oracle = try XCTUnwrap(RideSequenceRegistry.sequence(named: section.name))
                XCTAssertEqual(oracle.encode(entry.rides), block, "\(section.name)/\(boundary)")
                XCTAssertEqual(oracle.decode(block), entry.rides, "\(section.name)/\(boundary)")
                XCTAssertEqual(RideSequenceRegistry.tryDecode(block)?.sequence, oracle)
            }
        }
    }

    func testCodecRejectsOutOfRangeAndMalformedInputsDirectly() throws {
        XCTAssertEqual(RideSequence.mercury.encode(0), 0xCCC749CC)
        XCTAssertEqual(RideSequence.mercury.encode(500), 0x3FC6BD93)
        XCTAssertNil(RideSequence.mercury.encode(501))
        XCTAssertNil(RideSequence.mercury.encode(UInt.max))

        let fixture = try loadFixture()
        for entry in fixture.rejectedEncodings where entry.sequence == "mercury" {
            XCTAssertNil(RideSequence.mercury.decode(try parse(entry.block)), entry.block)
        }
        for entry in fixture.malformedBlocks where entry.name.hasPrefix("mercury-") {
            XCTAssertNil(RideSequence.mercury.decode(try parse(entry.block)), entry.name)
        }
    }

    private func expectedMirrorSequenceName(_ caseName: String) -> String? {
        if caseName.hasPrefix("mercury-") { return "mercury" }
        if caseName.hasPrefix("venus-") { return "venus" }
        if caseName.hasPrefix("jupiter-") { return "jupiter" }
        if caseName.hasPrefix("neptune-") { return "neptune" }
        return nil
    }

    private func collectAllFixtureBlocks(_ fixture: Fixture) throws -> Set<UInt32> {
        var blocks = Set<UInt32>()
        for section in fixture.sequences {
            for entry in section.encodings {
                blocks.insert(try parse(entry.block))
            }
        }
        return blocks
    }

    private func fixtureData() throws -> Data {
        var directory = URL(fileURLWithPath: #filePath).deletingLastPathComponent()
        for _ in 0..<8 {
            let candidate = directory.appendingPathComponent("TestFixtures/RideEncoding/ride-encoding-v2.json")
            if FileManager.default.fileExists(atPath: candidate.path) {
                return try Data(contentsOf: candidate)
            }
            directory.deleteLastPathComponent()
        }
        throw NSError(
            domain: "RideEncodingFixtureTests",
            code: 1,
            userInfo: [NSLocalizedDescriptionKey: "Could not locate ride-encoding-v2.json in the repository."]
        )
    }

    private func loadFixture() throws -> Fixture {
        try JSONDecoder().decode(Fixture.self, from: fixtureData())
    }

    private func parse(_ value: String) throws -> UInt32 {
        guard value.range(of: "^[0-9A-F]{8}$", options: .regularExpression) != nil,
              let block = UInt32(value, radix: 16) else {
            throw NSError(domain: "RideEncodingFixtureTests", code: 2, userInfo: [NSLocalizedDescriptionKey: "Invalid block \(value)"])
        }
        return block
    }

    private func object(_ value: Any?) throws -> [String: Any] {
        guard let value, let object = value as? [String: Any] else {
            throw NSError(domain: "RideEncodingFixtureTests", code: 3, userInfo: [NSLocalizedDescriptionKey: "Expected JSON object"])
        }
        return object
    }

    private func array(_ value: Any?) throws -> [[String: Any]] {
        guard let value, let array = value as? [[String: Any]] else {
            throw NSError(domain: "RideEncodingFixtureTests", code: 4, userInfo: [NSLocalizedDescriptionKey: "Expected JSON object array"])
        }
        return array
    }
}

private struct Fixture: Decodable {
    let schemaVersion: Int
    let fixtureId: String
    let boundaries: [UInt]
    let sequences: [SequenceSection]
    let rejectedEncodings: [RejectedEncoding]
    let malformedBlocks: [MalformedBlock]
    let mirrorCases: [MirrorCase]
}

private struct SequenceSection: Decodable {
    let name: String
    let zeroBlock: String
    let rotation: UInt8
    let minRides: UInt
    let maxRides: UInt
    let encodings: [Encoding]
}

private struct Encoding: Decodable {
    let rides: UInt
    let block: String
}

private struct RejectedEncoding: Decodable {
    let sequence: String
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
