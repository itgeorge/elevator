import Foundation

/// The Mercury-only ride counter codec used by Slice 2.
public enum MercuryRideCodec {
    public static let minimumRides: UInt = 0
    public static let maximumRides: UInt = 500
    public static let zeroBlock: UInt32 = 0xCCC749CC
    public static let rotation: UInt8 = 4

    /// Encodes an application-valid Mercury ride count into its page-0 block value.
    public static func encode(_ rides: UInt) -> UInt32? {
        guard rides <= maximumRides else { return nil }

        let low = UInt8(rides & 0xff)
        let high = UInt8((rides >> 8) & 0xff)
        let rotated = (low << rotation) | (low >> (8 - rotation))
        let payload = rotated ^ (high << rotation)
        let toggle: UInt32 = (payload & 0x08) != 0 ? 0xF3000000 : 0
        let delta = toggle | (UInt32(high) << 16) | (UInt32(low) << 8) | UInt32(payload)
        return zeroBlock ^ delta
    }

    /// Decodes a block only when it is structurally a Mercury block and in range.
    public static func decode(_ block: UInt32) -> UInt? {
        let delta = block ^ zeroBlock
        let high = (delta >> 16) & 0xff
        guard high <= 1 else { return nil }

        let rides = (high << 8) | ((delta >> 8) & 0xff)
        guard rides <= maximumRides,
              encode(UInt(rides)) == block else { return nil }
        return UInt(rides)
    }
}

public enum MercuryRideReadStatus: String, Equatable, Sendable {
    case success
    case unknownEncodingSequence
}

public struct MercuryRideRead: Equatable, Sendable {
    public let status: MercuryRideReadStatus
    public let rides: UInt?
    public let sourceBlock: UInt32?
    public let sourceBlockNumber: Int?
    public let blocksMatched: Bool
    public let warningMessage: String?

    public init(
        status: MercuryRideReadStatus,
        rides: UInt?,
        sourceBlock: UInt32?,
        sourceBlockNumber: Int?,
        blocksMatched: Bool,
        warningMessage: String?
    ) {
        self.status = status
        self.rides = rides
        self.sourceBlock = sourceBlock
        self.sourceBlockNumber = sourceBlockNumber
        self.blocksMatched = blocksMatched
        self.warningMessage = warningMessage
    }
}

/// Resolves Mercury's mirrored ride blocks using the same authority and diagnostics as C#.
public enum MercuryMirrorResolver {
    public static func resolve(block5: UInt32, block6: UInt32) -> MercuryRideRead {
        if block5 == block6 {
            return resolveMatching(block5)
        }

        let rides5 = MercuryRideCodec.decode(block5)
        let rides6 = MercuryRideCodec.decode(block6)

        if let rides5, let rides6 {
            return MercuryRideRead(
                status: .success,
                rides: rides6,
                sourceBlock: block6,
                sourceBlockNumber: 6,
                blocksMatched: false,
                warningMessage: rides5 == rides6
                    ? "Warning: blocks 5 and 6 differ; using block 6."
                    : "Warning: blocks 5 and 6 differ; using block 6 (\(rides6) rides)."
            )
        }

        if let rides5 {
            return MercuryRideRead(
                status: .success,
                rides: rides5,
                sourceBlock: block5,
                sourceBlockNumber: 5,
                blocksMatched: false,
                warningMessage: "Warning: blocks 5 and 6 differ; using block 5."
            )
        }

        if let rides6 {
            return MercuryRideRead(
                status: .success,
                rides: rides6,
                sourceBlock: block6,
                sourceBlockNumber: 6,
                blocksMatched: false,
                warningMessage: "Warning: blocks 5 and 6 differ; using block 6."
            )
        }

        return failure(block5: block5, blocksMatched: false)
    }

    private static func resolveMatching(_ block: UInt32) -> MercuryRideRead {
        guard let rides = MercuryRideCodec.decode(block) else {
            return failure(block5: block, blocksMatched: true)
        }

        return MercuryRideRead(
            status: .success,
            rides: rides,
            sourceBlock: block,
            sourceBlockNumber: 5,
            blocksMatched: true,
            warningMessage: nil
        )
    }

    private static func failure(block5: UInt32, blocksMatched: Bool) -> MercuryRideRead {
        MercuryRideRead(
            status: .unknownEncodingSequence,
            rides: nil,
            sourceBlock: block5,
            sourceBlockNumber: 5,
            blocksMatched: blocksMatched,
            warningMessage: nil
        )
    }
}
