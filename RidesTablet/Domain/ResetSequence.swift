import Foundation

/// Canonical page-0 identity/reset data adapted from RidesCli/Data reset images.
public struct ResetSequence: Codable, Equatable, Identifiable, Sendable {
    public let sequence: RideSequence
    public let block1: UInt32
    public let block2: UInt32
    public let block3: UInt32
    public let block4: UInt32
    public let block0: UInt32
    public let block7: UInt32

    public var id: String { sequence.rawValue }
    public var writableBlocks: ClosedRange<Int> { 1...6 }
    public var identityBlocks: [UInt32] { [block1, block2, block3, block4] }

    public init(sequence: RideSequence, block1: UInt32, block2: UInt32, block3: UInt32, block4: UInt32, block0: UInt32 = 0x00148040, block7: UInt32 = 0) {
        self.sequence = sequence
        self.block1 = block1
        self.block2 = block2
        self.block3 = block3
        self.block4 = block4
        self.block0 = block0
        self.block7 = block7
    }

    /// The CLI writes only blocks 1...6; block 4 is part of identity and is not omitted.
    public func resetImage(rideCount: UInt = 0) -> [UInt32] {
        let encoded = sequence.encode(rideCount) ?? sequence.zeroBlock
        return [block0, block1, block2, block3, block4, encoded, encoded, block7]
    }

    public static let all: [ResetSequence] = [
        .init(sequence: .mercury, block1: 0x9BFE0062, block2: 0x5BA4A3DE, block3: 0xD5D1D713, block4: 0xD5D1D713),
        .init(sequence: .venus, block1: 0x43FE0062, block2: 0x5BA494A3, block3: 0xD6D1C733, block4: 0xD6D1C733),
        .init(sequence: .earth, block1: 0xD3FE005D, block2: 0x522BC69D, block3: 0x650432F5, block4: 0x650432F5),
        .init(sequence: .pluto, block1: 0x83FE002A, block2: 0xF100C064, block3: 0xA3045930, block4: 0xA3045930),
        .init(sequence: .mars, block1: 0xC3FE0031, block2: 0x20C60722, block3: 0xB6D14924, block4: 0xB6D14924),
        .init(sequence: .jupiter, block1: 0xEBFE002A, block2: 0xF100CC5B, block3: 0xA5045936, block4: 0xA5045936),
        .init(sequence: .saturn, block1: 0x23FE007B, block2: 0xD88CBD8A, block3: 0x5D04593D, block4: 0x5D04593D),
        .init(sequence: .uranus, block1: 0xFBFE002A, block2: 0xF1003C92, block3: 0xF5D1D766, block4: 0xF5D1D766),
        .init(sequence: .neptune, block1: 0x8BFE002A, block2: 0xF100C6A2, block3: 0x95D15917, block4: 0x95D15917, block7: 0x57F674C3),
        .init(sequence: .charon, block1: 0xEBFE0077, block2: 0x7BECB142, block3: 0x610412F3, block4: 0x610412F3),
        .init(sequence: .nix, block1: 0x1BFE002A, block2: 0xF100C605, block3: 0x82045966, block4: 0x82045966)
    ]

    public static func `for`(_ sequence: RideSequence) -> ResetSequence {
        all.first(where: { $0.sequence == sequence })!
    }
}
