import Foundation

/// Registered ride-counter encodings adapted from Tokens/EncodingSequence.cs.
public enum RideSequence: String, CaseIterable, Codable, Sendable {
    case mercury, venus, earth, pluto, mars, jupiter, saturn, uranus, neptune, charon, nix

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

    public var maxRides: UInt { 500 }

    public func encode(_ rides: UInt) -> UInt32? {
        guard rides <= maxRides else { return nil }
        let low = UInt8(rides & 0xff)
        let high = UInt8((rides >> 8) & 0xff)
        let payload: UInt8
        if rotation == 0 {
            payload = low
        } else {
            payload = ((low << rotation) | (low >> (8 - rotation))) ^ (high << rotation)
        }
        let firstByte: UInt32 = (payload & 0x08) != 0 ? 0xF3 : 0
        let delta = (firstByte << 24) | (UInt32(high) << 16) | (UInt32(low) << 8) | UInt32(payload)
        return zeroBlock ^ delta
    }

    public func decode(_ block: UInt32) -> UInt? {
        let delta = block ^ zeroBlock
        let high = (delta >> 16) & 0xff
        guard high <= 1 else { return nil }
        let rides = (high << 8) | ((delta >> 8) & 0xff)
        return encode(UInt(rides)) == block ? UInt(rides) : nil
    }
}
