import Foundation
import Security

public protocol BridgeCredentialStore: Sendable {
    func load() throws -> BridgeCredential?
    func save(_ credential: BridgeCredential) throws
    func remove() throws
}

public enum BridgeCredentialStoreError: Error, Equatable, LocalizedError, Sendable {
    case keychain(OSStatus)
    case corruptData

    public var errorDescription: String? {
        switch self {
        case .keychain:
            return "Secure bridge credentials are unavailable."
        case .corruptData:
            return "The saved bridge credential is invalid. Pair again."
        }
    }
}

/// Production credential storage. The bearer token is stored only as Keychain data.
public final class KeychainBridgeCredentialStore: BridgeCredentialStore, @unchecked Sendable {
    private let service: String
    private let account: String

    public init(service: String = Bundle.main.bundleIdentifier ?? "com.example.RidesTablet", account: String = "bridge-credential") {
        self.service = service
        self.account = account
    }

    public func load() throws -> BridgeCredential? {
        let query: [CFString: Any] = [
            kSecClass: kSecClassGenericPassword,
            kSecAttrService: service,
            kSecAttrAccount: account,
            kSecReturnData: true,
            kSecMatchLimit: kSecMatchLimitOne
        ]
        var result: CFTypeRef?
        let status = SecItemCopyMatching(query as CFDictionary, &result)
        if status == errSecItemNotFound { return nil }
        guard status == errSecSuccess else { throw BridgeCredentialStoreError.keychain(status) }
        guard let data = result as? Data else { throw BridgeCredentialStoreError.corruptData }
        do {
            return try JSONDecoder().decode(BridgeCredential.self, from: data)
        } catch {
            throw BridgeCredentialStoreError.corruptData
        }
    }

    public func save(_ credential: BridgeCredential) throws {
        let data: Data
        do {
            data = try JSONEncoder().encode(credential)
        } catch {
            throw BridgeCredentialStoreError.corruptData
        }
        let query: [CFString: Any] = [
            kSecClass: kSecClassGenericPassword,
            kSecAttrService: service,
            kSecAttrAccount: account
        ]
        let attributes: [CFString: Any] = [
            kSecValueData: data,
            kSecAttrAccessible: kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly
        ]
        let updateStatus = SecItemUpdate(query as CFDictionary, attributes as CFDictionary)
        if updateStatus == errSecSuccess { return }
        guard updateStatus == errSecItemNotFound else { throw BridgeCredentialStoreError.keychain(updateStatus) }
        var addQuery = query
        addQuery[kSecValueData] = data
        addQuery[kSecAttrAccessible] = kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly
        let addStatus = SecItemAdd(addQuery as CFDictionary, nil)
        guard addStatus == errSecSuccess else { throw BridgeCredentialStoreError.keychain(addStatus) }
    }

    public func remove() throws {
        let query: [CFString: Any] = [
            kSecClass: kSecClassGenericPassword,
            kSecAttrService: service,
            kSecAttrAccount: account
        ]
        let status = SecItemDelete(query as CFDictionary)
        guard status == errSecSuccess || status == errSecItemNotFound else {
            throw BridgeCredentialStoreError.keychain(status)
        }
    }
}

/// Deterministic store for unit tests and simulator-only model tests.
public final class InMemoryBridgeCredentialStore: BridgeCredentialStore, @unchecked Sendable {
    private let lock = NSLock()
    private var storedCredential: BridgeCredential?
    private var saveError: Error?
    private var removalError: Error?

    public init(credential: BridgeCredential? = nil) {
        storedCredential = credential
    }

    public var credential: BridgeCredential? {
        lock.lock()
        defer { lock.unlock() }
        return storedCredential
    }

    public func setSaveError(_ error: Error?) {
        lock.lock()
        saveError = error
        lock.unlock()
    }

    public func setRemovalError(_ error: Error?) {
        lock.lock()
        removalError = error
        lock.unlock()
    }

    public func load() throws -> BridgeCredential? {
        lock.lock()
        defer { lock.unlock() }
        return storedCredential
    }

    public func save(_ credential: BridgeCredential) throws {
        lock.lock()
        defer { lock.unlock() }
        if let saveError { throw saveError }
        storedCredential = credential
    }

    public func remove() throws {
        lock.lock()
        defer { lock.unlock() }
        if let removalError { throw removalError }
        storedCredential = nil
    }
}
