import CryptoKit
import Foundation

/// Crypto and wire-contract helpers for the token-bound Bonjour relocation proof.
/// The byte transcript intentionally mirrors PairRelocationProof.cs exactly.
public enum BridgeRelocationProof {
    public static let locatorContext = "ridesbridge-relocation-locator-v1"
    public static let proofContext = "ridesbridge-relocation-proof-v1"
    public static let digestByteCount = 32
    public static let hexDigestLength = digestByteCount * 2

    /// Returns one fresh cryptographically random 32-byte nonce encoded as uppercase hex.
    public static func makeNonce() -> String {
        var generator = SystemRandomNumberGenerator()
        let bytes = (0..<digestByteCount).map { _ in
            UInt8.random(in: UInt8.min...UInt8.max, using: &generator)
        }
        return uppercaseHex(Data(bytes))
    }

    public static func locator(for bearer: String) -> String {
        let verifier = SHA256.hash(data: Data(bearer.utf8))
        return uppercaseHex(Data(SHA256.hash(data: transcript(
            Data(locatorContext.utf8),
            Data(verifier)
        ))))
    }

    public static func proof(
        for bearer: String,
        nonce: String,
        bridgeId: String,
        canonicalURL: String,
        apiVersion: String = "v1"
    ) -> String? {
        guard let nonceBytes = decodeUpperHex(nonce, byteCount: digestByteCount) else { return nil }
        let verifier = SHA256.hash(data: Data(bearer.utf8))
        let upperBridgeId = bridgeId.uppercased()
        let message = transcript(
            Data(proofContext.utf8),
            nonceBytes,
            Data(upperBridgeId.utf8),
            Data(canonicalURL.utf8),
            Data(apiVersion.utf8)
        )
        let mac = HMAC<SHA256>.authenticationCode(
            for: message,
            using: SymmetricKey(data: Data(verifier))
        )
        return uppercaseHex(Data(mac))
    }

    /// Validates the complete response without exposing any proof material to callers.
    public static func isValid(
        response: BridgePairRelocationProofResponse,
        bearer: String,
        expectedBridgeId: String,
        expectedNonce: String,
        canonicalURL: String,
        expectedAPIVersion: String = "v1"
    ) -> Bool {
        guard response.bridgeId == expectedBridgeId,
              response.apiVersion == expectedAPIVersion,
              response.nonce == expectedNonce,
              let expected = proof(
                  for: bearer,
                  nonce: expectedNonce,
                  bridgeId: expectedBridgeId,
                  canonicalURL: canonicalURL,
                  apiVersion: expectedAPIVersion
              ) else { return false }
        return constantTimeEqualUpperHex(response.proof, expected)
    }

    /// Fixed-length byte comparison. It deliberately does not return early on content.
    public static func constantTimeEqualUpperHex(_ lhs: String, _ rhs: String) -> Bool {
        guard let left = decodeUpperHex(lhs, byteCount: digestByteCount),
              let right = decodeUpperHex(rhs, byteCount: digestByteCount) else { return false }
        var difference: UInt8 = 0
        for index in 0..<digestByteCount {
            difference |= left[index] ^ right[index]
        }
        return difference == 0
    }

    public static func uppercaseHex(_ data: Data) -> String {
        data.map { String(format: "%02X", $0) }.joined()
    }

    public static func decodeUpperHex(_ value: String, byteCount: Int) -> Data? {
        let bytes = Array(value.utf8)
        guard bytes.count == byteCount * 2,
              bytes.allSatisfy({
                  ($0 >= 48 && $0 <= 57) || ($0 >= 65 && $0 <= 70)
              }) else { return nil }
        return Data((0..<byteCount).map { index in
            let high = hexNibble(bytes[index * 2])
            let low = hexNibble(bytes[index * 2 + 1])
            return (high << 4) | low
        })
    }

    private static func hexNibble(_ byte: UInt8) -> UInt8 {
        byte <= 57 ? byte - 48 : byte - 55
    }

    private static func transcript(_ fields: Data...) -> Data {
        var result = Data()
        for (index, field) in fields.enumerated() {
            result.append(field)
            if index + 1 < fields.count { result.append(0) }
        }
        return result
    }
}
