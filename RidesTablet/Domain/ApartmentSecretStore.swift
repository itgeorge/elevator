import Combine
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
}
