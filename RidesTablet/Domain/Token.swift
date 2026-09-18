import Foundation

public struct Token: Codable, Equatable, Identifiable, Sendable {
    /// Complete page-0 image. Indexes are the proxmark block numbers; block 4 is intentionally modeled.
    public let blocks: [UInt32]
    public let rideCount: UInt
    public let sequence: RideSequence

    public var id: String { blocks.map(Self.hex).joined(separator: "-") }
    public var block4: UInt32 { blocks[4] }
    public var block5: UInt32 { blocks[5] }
    public var block6: UInt32 { blocks[6] }

    public init(blocks: [UInt32], rideCount: UInt, sequence: RideSequence) {
        precondition(blocks.count == 8, "A token page-0 image must contain exactly 8 blocks.")
        self.blocks = blocks
        self.rideCount = rideCount
        self.sequence = sequence
    }

    public var hexBlocks: [String] { blocks.map(Self.hex) }

    /// Returns the same token identity with both ride mirrors updated.
    public func withRideCount(_ rides: UInt) -> Token {
        var updated = blocks
        if let encoded = sequence.encode(rides) {
            updated[5] = encoded
            updated[6] = encoded
        }
        return Token(blocks: updated, rideCount: rides, sequence: sequence)
    }

    public static func sample(rideCount: UInt = 73, sequence: RideSequence = .mercury) -> Token {
        let identity = ResetSequence.for(sequence).resetImage(rideCount: rideCount)
        return Token(blocks: identity, rideCount: rideCount, sequence: sequence)
    }

    public static func hex(_ value: UInt32) -> String {
        String(format: "%08X", value)
    }
}

public struct UnknownToken: Codable, Equatable, Identifiable, Sendable {
    public let blocks: [UInt32]
    public let reason: String

    public var id: String { blocks.map(Token.hex).joined(separator: "-") }
    public var block4: UInt32 { blocks[4] }
    public var block5: UInt32 { blocks[5] }
    public var block6: UInt32 { blocks[6] }

    public init(blocks: [UInt32], reason: String = "Unknown ride encoding sequence") {
        precondition(blocks.count == 8, "A token page-0 image must contain exactly 8 blocks.")
        self.blocks = blocks
        self.reason = reason
    }
}

public enum TokenReadOutcome: Equatable, Sendable {
    case known(Token)
    case unknown(UnknownToken)
    case noChip
}

/// Mirrors the CLI convention: block 6 is authoritative when both mirrors decode but differ.
public enum TokenDecoder {
    public static func decode(blocks: [UInt32]) -> TokenReadOutcome {
        guard blocks.count == 8 else {
            return .unknown(UnknownToken(blocks: Array(blocks.prefix(8)) + Array(repeating: 0, count: max(0, 8 - blocks.count))))
        }

        let block5 = blocks[5]
        let block6 = blocks[6]
        let decoded5 = decodeRide(block5)
        let decoded6 = decodeRide(block6)
        let selected: (RideSequence, UInt)?

        if block5 == block6 {
            selected = decoded5
        } else if let decoded6 {
            selected = decoded6
        } else {
            selected = decoded5
        }

        guard let (sequence, rides) = selected else {
            return .unknown(UnknownToken(blocks: blocks))
        }
        return .known(Token(blocks: blocks, rideCount: rides, sequence: sequence))
    }

    private static func decodeRide(_ block: UInt32) -> (RideSequence, UInt)? {
        guard let decoded = RideSequenceRegistry.tryDecode(block) else { return nil }
        return (decoded.sequence, decoded.rides)
    }
}
