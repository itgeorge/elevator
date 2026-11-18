import Combine
import Foundation

@MainActor
public final class ApartmentSecretStore: ObservableObject {
    public static let shared = ApartmentSecretStore()
    @Published public private(set) var secret: String?

    public init(secret: String? = nil) { self.secret = secret }
    public func setSecret(_ value: String) {
        let trimmed = value.trimmingCharacters(in: .whitespacesAndNewlines)
        secret = trimmed.isEmpty ? nil : trimmed
    }
    public func clear() { secret = nil }
}
