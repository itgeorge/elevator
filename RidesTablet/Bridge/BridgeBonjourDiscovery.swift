import Foundation
import Network

public enum BridgeBonjourConstants {
    public static let serviceType = "_elevator-rides._tcp"
    public static let domain = "local."

    public static func isValidBridgeID(_ value: String) -> Bool {
        value.utf8.count == 32 && value.utf8.allSatisfy { byte in
            (byte >= 48 && byte <= 57) || (byte >= 65 && byte <= 70)
        }
    }
}

public struct BridgeBonjourTXTValues: Equatable, Sendable {
    public let type: String
    public let bridgeId: String
    public let apiVersion: String
    public let url: URL

    public init(type: String, bridgeId: String, apiVersion: String, url: URL) {
        self.type = type
        self.bridgeId = bridgeId
        self.apiVersion = apiVersion
        self.url = url
    }
}

public enum BridgeBonjourTXTError: Error, Equatable, LocalizedError, Sendable {
    case emptyRecord
    case malformedLength
    case emptyEntry
    case nonUTF8
    case missingEquals
    case emptyKey
    case duplicateKey
    case unknownKey
    case invalidValue
    case nonStringEntry

    public var errorDescription: String? {
        switch self {
        case .emptyRecord: "The bridge Bonjour record is empty."
        case .malformedLength: "The bridge Bonjour record has a malformed entry length."
        case .emptyEntry: "The bridge Bonjour record contains an empty entry."
        case .nonUTF8: "The bridge Bonjour record contains non-text data."
        case .missingEquals: "The bridge Bonjour record contains an entry without a key and value."
        case .emptyKey: "The bridge Bonjour record contains an empty key."
        case .duplicateKey: "The bridge Bonjour record contains a duplicate key."
        case .unknownKey: "The bridge Bonjour record contains an unsupported key."
        case .invalidValue: "The bridge Bonjour record contains an invalid value."
        case .nonStringEntry: "The bridge Bonjour record contains a non-string entry."
        }
    }
}

/// Strict parser for the raw DNS-SD TXT wire representation. Every entry is a
/// one-byte length followed by UTF-8 `key=value` bytes. DNS-SD TXT records are
/// not property-list or JSON data, so parsing never performs lossy conversion.
public enum BridgeBonjourTXTParser {
    private static let allowedKeys: Set<String> = ["type", "bridgeId", "apiVersion", "url"]

    public static func parse(_ data: Data) throws -> BridgeBonjourTXTValues {
        guard !data.isEmpty else { throw BridgeBonjourTXTError.emptyRecord }

        var values: [String: String] = [:]
        let bytes = Array(data)
        var offset = 0
        while offset < bytes.count {
            let length = Int(bytes[offset])
            offset += 1
            guard length > 0 else { throw BridgeBonjourTXTError.emptyEntry }
            guard length <= bytes.count - offset else { throw BridgeBonjourTXTError.malformedLength }

            let entry = Data(bytes[offset..<(offset + length)])
            offset += length
            guard let text = String(data: entry, encoding: .utf8) else {
                throw BridgeBonjourTXTError.nonUTF8
            }
            guard let equals = text.firstIndex(of: "=") else {
                throw BridgeBonjourTXTError.missingEquals
            }

            let key = String(text[..<equals])
            let value = String(text[text.index(after: equals)...])
            guard !key.isEmpty else { throw BridgeBonjourTXTError.emptyKey }
            guard allowedKeys.contains(key) else { throw BridgeBonjourTXTError.unknownKey }
            guard values.updateValue(value, forKey: key) == nil else {
                throw BridgeBonjourTXTError.duplicateKey
            }
        }

        guard values.count == allowedKeys.count,
              let type = values["type"], type == "elevator-rides",
              let bridgeId = values["bridgeId"], isBridgeID(bridgeId),
              let apiVersion = values["apiVersion"], apiVersion == "v1",
              let urlText = values["url"],
              let url = canonicalPrivateIPv4RootURL(urlText) else {
            throw BridgeBonjourTXTError.invalidValue
        }
        return BridgeBonjourTXTValues(type: type, bridgeId: bridgeId, apiVersion: apiVersion, url: url)
    }

    private static func isBridgeID(_ value: String) -> Bool {
        BridgeBonjourConstants.isValidBridgeID(value)
    }

    /// Mirrors the backend Bonjour contract: HTTP, an explicit port, a root path,
    /// and a canonical private IPv4 literal. The returned URL always has `/`.
    private static func canonicalPrivateIPv4RootURL(_ text: String) -> URL? {
        guard let parsed = URL(string: text),
              let components = URLComponents(url: parsed, resolvingAgainstBaseURL: false),
              components.scheme?.lowercased() == "http",
              components.user == nil,
              components.password == nil,
              components.query == nil,
              components.fragment == nil,
              components.path.isEmpty || components.path == "/",
              let host = components.host,
              let canonicalHost = canonicalPrivateIPv4Host(host),
              let port = components.port,
              (1...65535).contains(port) else {
            return nil
        }

        var canonical = URLComponents()
        canonical.scheme = "http"
        canonical.host = canonicalHost
        canonical.port = port
        canonical.path = "/"
        return canonical.url
    }

    private static func canonicalPrivateIPv4Host(_ host: String) -> String? {
        let parts = host.split(separator: ".", omittingEmptySubsequences: false)
        guard parts.count == 4 else { return nil }

        var octets: [Int] = []
        for part in parts {
            guard !part.isEmpty,
                  part.utf8.allSatisfy({ $0 >= 48 && $0 <= 57 }),
                  let octet = Int(part), (0...255).contains(octet) else {
                return nil
            }
            octets.append(octet)
        }

        // Keep this in parity with BridgeOptions.IsPrivateIpv4. Loopback is a
        // useful manual test address, but is not a Bonjour-reachable bridge URL.
        let privateAddress = octets[0] == 10
            || (octets[0] == 172 && (16...31).contains(octets[1]))
            || (octets[0] == 192 && octets[1] == 168)
            || (octets[0] == 169 && octets[1] == 254)
        guard privateAddress else { return nil }

        let canonical = octets.map(String.init).joined(separator: ".")
        return host == canonical ? canonical : nil
    }
}

public enum BridgeBonjourBrowserEndpoint: Equatable, Sendable {
    case service(name: String, type: String, domain: String)
    case nonService
}

public struct BridgeBonjourRawResult: Equatable, Sendable {
    public let endpoint: BridgeBonjourBrowserEndpoint
    public let txtRecord: Data
    /// NWTXTRecord can carry `.data`, `.empty`, or `.none` entries. Those are
    /// not accepted as the public string-only bridge contract.
    public let containsOnlyStringEntries: Bool

    public init(
        endpoint: BridgeBonjourBrowserEndpoint,
        txtRecord: Data,
        containsOnlyStringEntries: Bool = true
    ) {
        self.endpoint = endpoint
        self.txtRecord = txtRecord
        self.containsOnlyStringEntries = containsOnlyStringEntries
    }
}

public struct BridgeBonjourCandidate: Equatable, Hashable, Sendable, Identifiable {
    public let bridgeId: String
    public let url: URL
    public let serviceName: String

    public init(bridgeId: String, url: URL, serviceName: String) {
        self.bridgeId = bridgeId
        self.url = url
        self.serviceName = serviceName
    }

    /// URL is part of the candidate key intentionally: one bridge can advertise multiple
    /// private addresses and those must remain separate operator choices.
    public var id: String { "\(bridgeId)|\(url.absoluteString)" }
}

public struct BridgeBonjourSnapshot: Equatable, Sendable {
    public let candidates: [BridgeBonjourCandidate]
    public let rejectedResultCount: Int

    public init(candidates: [BridgeBonjourCandidate], rejectedResultCount: Int) {
        self.candidates = candidates
        self.rejectedResultCount = rejectedResultCount
    }
}

public enum BridgeBonjourCandidateParser {
    public static func parseSnapshot(_ results: [BridgeBonjourRawResult]) -> BridgeBonjourSnapshot {
        var candidatesByID: [String: BridgeBonjourCandidate] = [:]
        var rejectedResultCount = 0

        for result in results {
            guard result.containsOnlyStringEntries else {
                rejectedResultCount += 1
                continue
            }
            guard case let .service(name, type, domain) = result.endpoint,
                  isValidServiceName(name),
                  type == BridgeBonjourConstants.serviceType,
                  domain == BridgeBonjourConstants.domain else {
                rejectedResultCount += 1
                continue
            }
            guard let txt = try? BridgeBonjourTXTParser.parse(result.txtRecord) else {
                rejectedResultCount += 1
                continue
            }

            let candidate = BridgeBonjourCandidate(
                bridgeId: txt.bridgeId,
                url: txt.url,
                serviceName: name
            )
            if let previous = candidatesByID[candidate.id] {
                // The service label is not identity. Keep a deterministic display
                // label when a browser emits the same service more than once.
                if candidate.serviceName < previous.serviceName {
                    candidatesByID[candidate.id] = candidate
                }
            } else {
                candidatesByID[candidate.id] = candidate
            }
        }

        let candidates = candidatesByID.values.sorted { lhs, rhs in
            if lhs.bridgeId != rhs.bridgeId { return lhs.bridgeId < rhs.bridgeId }
            if lhs.url.absoluteString != rhs.url.absoluteString {
                return lhs.url.absoluteString < rhs.url.absoluteString
            }
            return lhs.serviceName < rhs.serviceName
        }
        return BridgeBonjourSnapshot(
            candidates: candidates,
            rejectedResultCount: rejectedResultCount
        )
    }

    private static func isValidServiceName(_ name: String) -> Bool {
        !name.isEmpty && name.utf8.count <= 63 && name.utf8.allSatisfy { byte in
            (byte >= 48 && byte <= 57)
                || (byte >= 65 && byte <= 90)
                || (byte >= 97 && byte <= 122)
                || byte == 45
        }
    }
}

public enum BridgeBonjourSourceState: Equatable, Sendable {
    case ready
    case denied
    case failed
}

/// Injectable boundary for discovery. Unit tests use a fake and never construct
/// or start NWBrowser, so simulator tests never depend on multicast.
@MainActor
public protocol BridgeBonjourBrowserSource: AnyObject {
    func start(
        onState: @escaping @Sendable (BridgeBonjourSourceState) -> Void,
        onResults: @escaping @Sendable ([BridgeBonjourRawResult]) -> Void
    )
    func stop()
}

/// Production Network.framework implementation. NWBrowser is created lazily by
/// `start`, so constructing the model does not perform multicast discovery.
/// NWBrowser callbacks are marshalled back to MainActor before touching
/// lifecycle state, and the actor-isolated source owns its browser.
@MainActor
public final class NetworkBonjourBrowserSource: BridgeBonjourBrowserSource {
    private var browser: NWBrowser?
    // All lifecycle operations are issued on the main queue: the model is
    // MainActor-isolated and NWBrowser is started on .main. The generation also
    // prevents callbacks queued by a cancelled browser from affecting a later one.
    private var browseGeneration = 0

    nonisolated public init() {}

    public func start(
        onState: @escaping @Sendable (BridgeBonjourSourceState) -> Void,
        onResults: @escaping @Sendable ([BridgeBonjourRawResult]) -> Void
    ) {
        guard browser == nil else { return }
        browseGeneration &+= 1
        let generation = browseGeneration
        let browser = NWBrowser(
            for: .bonjourWithTXTRecord(
                type: BridgeBonjourConstants.serviceType,
                domain: BridgeBonjourConstants.domain
            ),
            using: .tcp
        )
        self.browser = browser
        browser.stateUpdateHandler = { [weak self, onState] state in
            Task { @MainActor [weak self, onState] in
                guard let self, self.browseGeneration == generation else { return }
                switch state {
                case .ready:
                    onState(.ready)
                case .failed(let error):
                    onState(Self.isAuthorizationError(error) ? .denied : .failed)
                    self.stop()
                case .waiting(let error):
                    // Waiting is not a terminal browser error. Authorization
                    // failures are terminal; other waits may recover to ready.
                    if Self.isAuthorizationError(error) {
                        onState(.denied)
                        self.stop()
                    }
                case .setup, .cancelled:
                    break
                @unknown default:
                    break
                }
            }
        }
        browser.browseResultsChangedHandler = { results, _ in
            let rawResults = results.map { result -> BridgeBonjourRawResult in
                let endpoint: BridgeBonjourBrowserEndpoint
                if case let .service(name, type, domain, _) = result.endpoint {
                    endpoint = .service(name: name, type: type, domain: domain)
                } else {
                    endpoint = .nonService
                }

                guard case let .bonjour(record) = result.metadata else {
                    return BridgeBonjourRawResult(endpoint: endpoint, txtRecord: Data())
                }
                let onlyStrings = record.allSatisfy { _, entry in
                    if case .string = entry { return true }
                    return false
                }
                return BridgeBonjourRawResult(
                    endpoint: endpoint,
                    txtRecord: record.data,
                    containsOnlyStringEntries: onlyStrings
                )
            }
            Task { @MainActor [weak self, onResults] in
                guard let self, self.browseGeneration == generation else { return }
                onResults(rawResults)
            }
        }
        browser.start(queue: .main)
    }

    public func stop() {
        browseGeneration &+= 1
        browser?.cancel()
        browser = nil
    }

    deinit {
        browser?.cancel()
    }

    nonisolated internal static func isAuthorizationError(_ error: NWError) -> Bool {
        switch error {
        case .posix(let code):
            return code == .EACCES || code == .EPERM
        case .dns(let code):
            // kDNSServiceErr_PolicyDenied from dns_sd.h. This is the stable
            // DNS-SD result used when Local Network policy blocks browsing.
            return code == -65570
        default:
            return false
        }
    }
}

public enum BridgeBonjourSelectionError: Error, Equatable, LocalizedError, Sendable {
    case identityUnavailable
    case identityMismatch

    public var errorDescription: String? {
        switch self {
        case .identityUnavailable:
            "This saved pairing has no bridge identifier. Enter the address manually or pair again before relocating."
        case .identityMismatch:
            "This Bonjour bridge has a different public identifier from the saved pairing. No request was sent."
        }
    }
}

public enum BridgeBonjourDiscoveryState: Equatable, Sendable {
    case idle
    case browsing
    case offered
    case reconnecting
    case selectionRequired
    case stopped
    case denied
    case failed

    public var title: String {
        switch self {
        case .idle: "Bonjour discovery idle"
        case .browsing: "Searching for local bridges…"
        case .offered: "One compatible bridge found"
        case .reconnecting: "Reconnecting to saved bridge…"
        case .selectionRequired: "Select a compatible bridge"
        case .stopped: "Bonjour discovery stopped"
        case .denied: "Local Network access denied"
        case .failed: "Bonjour discovery failed"
        }
    }
}
