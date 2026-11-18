import Combine
import CryptoKit
import Foundation

@MainActor
public final class ApartmentSecretStore: ObservableObject {
    public static let shared = ApartmentSecretStore()
    @Published public private(set) var secret: String?

    public init(secret: String? = nil) { self.secret = secret }
    public func setSecret(_ value: String) {
        // Match the exact UTF-8 secret used by the CLI, including any whitespace.
        secret = value.isEmpty ? nil : value
    }
    public func clear() { secret = nil }

    /// A display-only SHA-256 fingerprint: the first 16 lowercase hex characters (64 bits).
    /// Hashes the exact UTF-8 bytes, including whitespace; the secret is never serialized.
    public var sha256Fingerprint: String? {
        guard let secret else { return nil }
        let digest = SHA256.hash(data: Data(secret.utf8))
        return String(digest.map { String(format: "%02x", $0) }.joined().prefix(16))
    }
}
