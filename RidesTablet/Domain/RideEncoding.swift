import Foundation

/// Encodes the shared nine-bit ride counter independently of a token sequence.
public enum RideCounterCodec {
    public static let maxCounter: UInt = 511

    public static func buildDelta(_ rides: UInt, rotation: UInt8) -> UInt32? {
        guard rotation <= 7 else { return nil }
        guard rides <= maxCounter else { return nil }

        let low = UInt8(rides & 0xff)
        let high = UInt8((rides >> 8) & 0xff)
        let payload = rotateLeft(low, rotation: rotation) ^ (high << rotation)
        let firstByte: UInt32 = (payload & 0x08) != 0 ? 0xF3 : 0
        return (firstByte << 24) | (UInt32(high) << 16) | (UInt32(low) << 8) | UInt32(payload)
    }

    public static func encode(zeroBlock: UInt32, rotation: UInt8, rides: UInt) -> UInt32? {
        guard let delta = buildDelta(rides, rotation: rotation) else { return nil }
        return zeroBlock ^ delta
    }

    public static func decode(zeroBlock: UInt32, rotation: UInt8, block: UInt32) -> UInt? {
        guard rotation <= 7 else { return nil }
        let delta = block ^ zeroBlock
        let high = (delta >> 16) & 0xff
        guard high <= 1 else { return nil }

        let rides = UInt((high << 8) | ((delta >> 8) & 0xff))
        guard buildDelta(rides, rotation: rotation) == delta,
              encode(zeroBlock: zeroBlock, rotation: rotation, rides: rides) == block else {
            return nil
        }
        return rides
    }

    private static func rotateLeft(_ value: UInt8, rotation: UInt8) -> UInt8 {
        guard rotation != 0 else { return value }
        return (value << rotation) | (value >> (8 - rotation))
    }
}

/// Registered ride-counter encodings adapted from Tokens/EncodingSequence.cs.
public enum RideSequence: String, CaseIterable, Codable, Sendable {
    case mercury, venus, earth, pluto, mars, jupiter, saturn, uranus, neptune, charon, nix

    public static let minimumRides: UInt = 0
    public static let maximumRides: UInt = 500

    public var zeroBlock: UInt32 {
        switch self {
        case .mercury: 0xCCC749CC
        case .venus: 0x48C74948
        case .earth: 0x18121218
        case .pluto: 0x1F12121F
        case .mars: 0x4EC7494E
        case .jupiter: 0x8C124980
        case .saturn: 0x8B1249F0
        case .uranus: 0x891249D0
        case .neptune: 0x8F1249B0
        case .charon: 0xC0121244
        case .nix: 0x0DC7C70D
        }
    }

    public var rotation: UInt8 {
        switch self {
        case .mercury, .venus, .earth, .pluto, .mars, .nix: 4
        case .jupiter, .saturn, .uranus, .neptune, .charon: 0
        }
    }

    public var minRides: UInt { Self.minimumRides }
    public var maxRides: UInt { Self.maximumRides }

    public func encode(_ rides: UInt) -> UInt32? {
        guard rides >= minRides, rides <= maxRides else { return nil }
        return RideCounterCodec.encode(zeroBlock: zeroBlock, rotation: rotation, rides: rides)
    }

    public func decode(_ block: UInt32) -> UInt? {
        guard let rides = RideCounterCodec.decode(zeroBlock: zeroBlock, rotation: rotation, block: block),
              rides >= minRides, rides <= maxRides else {
            return nil
        }
        return rides
    }
}

/// Full registry matching Tokens/EncodingSequences.cs.
public enum RideSequenceRegistry {
    public static let all: [RideSequence] = [
        .mercury, .venus, .earth, .pluto, .mars,
        .jupiter, .saturn, .uranus, .neptune, .charon, .nix,
    ]

    private static let validated: Void = {
        let names = Set(all.map(\.rawValue))
        precondition(names.count == all.count, "Duplicate encoding sequence names in registry.")

        var blocks: [UInt32: (RideSequence, UInt)] = [:]
        for sequence in all {
            for rides in sequence.minRides...sequence.maxRides {
                guard let block = sequence.encode(rides) else {
                    preconditionFailure("Failed to encode \(sequence.rawValue)/\(rides).")
                }
                if let other = blocks[block] {
                    preconditionFailure(
                        "Encoding collision: \(sequence.rawValue)/\(rides) and \(other.0.rawValue)/\(other.1) encode as \(Token.hex(block))."
                    )
                }
                blocks[block] = (sequence, rides)
            }
        }
    }()

    public static func sequence(named friendlyName: String) -> RideSequence? {
        _ = validated
        let normalized = friendlyName.trimmingCharacters(in: .whitespacesAndNewlines).lowercased()
        guard !normalized.isEmpty else { return nil }
        return all.first { $0.rawValue == normalized }
    }

    /// Matches a block against every registered sequence using complete structural validation.
    public static func tryDecode(_ block: UInt32) -> (sequence: RideSequence, rides: UInt)? {
        _ = validated
        var match: (RideSequence, UInt)?
        for candidate in all {
            guard let rides = candidate.decode(block) else { continue }
            if let existing = match {
                preconditionFailure(
                    "Block \(Token.hex(block)) ambiguously matches '\(existing.0.rawValue)' and '\(candidate.rawValue)'."
                )
            }
            match = (candidate, rides)
        }
        return match
    }
}

public enum RideReadStatus: String, Equatable, Sendable {
    case success
    case unknownEncodingSequence
}

public struct RideRead: Equatable, Sendable {
    public let status: RideReadStatus
    public let rides: UInt?
    public let sequence: RideSequence?
    public let sourceBlock: UInt32?
    public let sourceBlockNumber: Int?
    public let blocksMatched: Bool
    public let warningMessage: String?

    public init(
        status: RideReadStatus,
        rides: UInt?,
        sequence: RideSequence?,
        sourceBlock: UInt32?,
        sourceBlockNumber: Int?,
        blocksMatched: Bool,
        warningMessage: String?
    ) {
        self.status = status
        self.rides = rides
        self.sequence = sequence
        self.sourceBlock = sourceBlock
        self.sourceBlockNumber = sourceBlockNumber
        self.blocksMatched = blocksMatched
        self.warningMessage = warningMessage
    }
}

/// Resolves ride count from mirrored page-0 blocks 5 and 6 using the full registry.
public enum RideBlockResolver {
    public static let minimumRides = RideSequence.minimumRides
    public static let maximumRides = RideSequence.maximumRides

    public static func resolve(block5: UInt32, block6: UInt32) -> RideRead {
        if block5 == block6 {
            return resolveMatching(block5)
        }

        let valid5 = tryValidate(block5)
        let valid6 = tryValidate(block6)

        if let (sequence6, rides6) = valid6, let (_, rides5) = valid5 {
            return RideRead(
                status: .success,
                rides: rides6,
                sequence: sequence6,
                sourceBlock: block6,
                sourceBlockNumber: 6,
                blocksMatched: false,
                warningMessage: rides5 == rides6
                    ? "Warning: blocks 5 and 6 differ; using block 6."
                    : "Warning: blocks 5 and 6 differ; using block 6 (\(rides6) rides)."
            )
        }

        if let (sequence5, rides5) = valid5 {
            return RideRead(
                status: .success,
                rides: rides5,
                sequence: sequence5,
                sourceBlock: block5,
                sourceBlockNumber: 5,
                blocksMatched: false,
                warningMessage: "Warning: blocks 5 and 6 differ; using block 5."
            )
        }

        if let (sequence6, rides6) = valid6 {
            return RideRead(
                status: .success,
                rides: rides6,
                sequence: sequence6,
                sourceBlock: block6,
                sourceBlockNumber: 6,
                blocksMatched: false,
                warningMessage: "Warning: blocks 5 and 6 differ; using block 6."
            )
        }

        return failure(block5: block5, blocksMatched: false)
    }

    private static func resolveMatching(_ block: UInt32) -> RideRead {
        if let (sequence, rides) = tryValidate(block) {
            return RideRead(
                status: .success,
                rides: rides,
                sequence: sequence,
                sourceBlock: block,
                sourceBlockNumber: 5,
                blocksMatched: true,
                warningMessage: nil
            )
        }

        return failure(block5: block, blocksMatched: true)
    }

    private static func failure(block5: UInt32, blocksMatched: Bool) -> RideRead {
        RideRead(
            status: .unknownEncodingSequence,
            rides: nil,
            sequence: nil,
            sourceBlock: block5,
            sourceBlockNumber: 5,
            blocksMatched: blocksMatched,
            warningMessage: nil
        )
    }

    private static func tryValidate(_ block: UInt32) -> (RideSequence, UInt)? {
        guard let decoded = RideSequenceRegistry.tryDecode(block),
              decoded.rides <= maximumRides else {
            return nil
        }
        return decoded
    }
}
