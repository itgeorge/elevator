import CryptoKit
import Foundation

/// Compatible with Tokens/ApartmentBlockCodec.cs (apt-v1).
public enum ApartmentBlockCodec {
    public struct Payload: Equatable, Sendable {
        public let building: UInt8
        public let apartment: UInt8
    }

    public static func encode(secret: String, block3: UInt32, building: UInt8 = 0, apartment: UInt8) -> UInt32 {
        let plain = UInt16(building) << 8 | UInt16(apartment)
        let mask = trunc16(secret: Data(secret.utf8), message: prefix("mask", block3))
        let seal = trunc16(secret: Data(secret.utf8), message: prefix("seal", block3) + [UInt8(plain >> 8), UInt8(plain & 0xff)])
        return UInt32(seal) << 16 | UInt32(plain ^ mask)
    }

    public static func decode(secret: String, block3: UInt32, block4: UInt32) -> Payload? {
        let key = Data(secret.utf8)
        let mask = trunc16(secret: key, message: prefix("mask", block3))
        let plain = UInt16(block4 & 0xffff) ^ mask
        let seal = trunc16(secret: key, message: prefix("seal", block3) + [UInt8(plain >> 8), UInt8(plain & 0xff)])
        guard UInt16(block4 >> 16) == seal else { return nil }
        return Payload(building: UInt8(plain >> 8), apartment: UInt8(plain & 0xff))
    }

    private static func prefix(_ label: String, _ block3: UInt32) -> [UInt8] {
        let value = block3.bigEndian
        return Array(label.utf8) + Array("apt-v1".utf8) + withUnsafeBytes(of: value) { Array($0) }
    }

    private static func trunc16(secret: Data, message: [UInt8]) -> UInt16 {
        let key = SymmetricKey(data: secret)
        let digest = Array(HMAC<SHA256>.authenticationCode(for: Data(message), using: key))
        return UInt16(digest[0]) << 8 | UInt16(digest[1])
    }
}
