import Foundation

public struct BridgeCredential: Codable, Equatable, Sendable {
    public let baseURL: URL
    public let accessToken: String
    public let tokenType: String
    /// Public bridge identifier carried by QR pairing; it is not an authenticator. It is
    /// optional so credentials saved by earlier app versions continue to decode and manual
    /// pairing remains unchanged.
    public let bridgeId: String?

    public init(baseURL: URL, accessToken: String, tokenType: String = "Bearer", bridgeId: String? = nil) {
        self.baseURL = baseURL
        self.accessToken = accessToken
        self.tokenType = tokenType
        self.bridgeId = bridgeId
    }
}

public enum BridgeClientError: Error, Equatable, LocalizedError, Sendable {
    case invalidBaseURL(String)
    case invalidPIN
    case missingCredential
    case unauthorized
    case timeout
    case unreachable
    case invalidJSON
    case invalidResponse
    case server(code: String, message: String, statusCode: Int)

    public var errorDescription: String? {
        switch self {
        case .invalidBaseURL(let reason):
            return "Enter a local HTTP bridge address. \(reason)"
        case .invalidPIN:
            return "PIN must contain exactly six digits."
        case .missingCredential:
            return "Pair with the bridge before reading hardware."
        case .unauthorized:
            return "Pairing is no longer valid. Pair again with a new PIN."
        case .timeout:
            return "The bridge timed out. Check the Mac and local Wi-Fi, then try again."
        case .unreachable:
            return "Could not reach the bridge. Check its local IP, port, and Wi-Fi."
        case .invalidJSON, .invalidResponse:
            return "The bridge returned an invalid or incompatible response. Check that it is a healthy v1 bridge."
        case .server(let code, let message, _):
            return "Bridge error \(code): \(message)"
        }
    }
}

/// HTTP client for the deliberately local-only v1 bridge API.
public final class BridgeClient: @unchecked Sendable {
    public let baseURL: URL
    private let session: URLSession
    private let healthRetryDelay: @Sendable () async throws -> Void
    private let credentialLock = NSLock()
    private var credential: BridgeCredential?

    public init(
        baseURL: URL,
        session: URLSession = .shared,
        credential: BridgeCredential? = nil,
        healthRetryDelay: @escaping @Sendable () async throws -> Void = {
            try await Task.sleep(nanoseconds: 100_000_000)
        }
    ) throws {
        self.baseURL = try Self.normalizeBaseURL(baseURL)
        self.session = session
        self.healthRetryDelay = healthRetryDelay
        if let credential {
            let credentialURL = try Self.normalizeBaseURL(credential.baseURL)
            guard credentialURL == self.baseURL,
                  !credential.accessToken.isEmpty,
                  credential.tokenType.caseInsensitiveCompare("Bearer") == .orderedSame else {
                throw BridgeClientError.invalidResponse
            }
            self.credential = BridgeCredential(baseURL: self.baseURL, accessToken: credential.accessToken, tokenType: credential.tokenType, bridgeId: credential.bridgeId)
        } else {
            self.credential = nil
        }
    }

    public convenience init(
        baseURLString: String,
        session: URLSession = .shared,
        healthRetryDelay: @escaping @Sendable () async throws -> Void = {
            try await Task.sleep(nanoseconds: 100_000_000)
        }
    ) throws {
        try self.init(
            baseURL: Self.normalizeBaseURL(baseURLString),
            session: session,
            healthRetryDelay: healthRetryDelay
        )
    }

    public static func normalizeBaseURL(_ string: String) throws -> URL {
        let trimmed = string.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmed.isEmpty else {
            throw BridgeClientError.invalidBaseURL("The URL is missing.")
        }

        // The UI accepts the bridge's concise direct address. Add the scheme before
        // asking Foundation to parse it so `192.168.1.20:5080` is not mistaken for
        // a URL whose scheme is `192.168.1.20`.
        let candidate = trimmed.contains("://") ? trimmed : "http://\(trimmed)"
        guard let url = URL(string: candidate) else {
            throw BridgeClientError.invalidBaseURL("The URL is malformed.")
        }
        return try normalizeBaseURL(url)
    }

    public static func normalizeBaseURL(_ url: URL) throws -> URL {
        guard let components = URLComponents(url: url, resolvingAgainstBaseURL: false),
              components.scheme?.lowercased() == "http" else {
            throw BridgeClientError.invalidBaseURL("HTTPS and other schemes are not supported on the local bridge.")
        }
        guard components.user == nil, components.password == nil,
              components.query == nil, components.fragment == nil else {
            throw BridgeClientError.invalidBaseURL("Credentials, queries, and fragments are not allowed.")
        }
        guard components.path.isEmpty || components.path == "/" else {
            throw BridgeClientError.invalidBaseURL("The URL must not contain a path.")
        }
        guard let host = components.host?.lowercased(), !host.isEmpty else {
            throw BridgeClientError.invalidBaseURL("A direct local IPv4 address or localhost is required.")
        }
        guard !hasEmptyPort(in: url.absoluteString) else {
            throw BridgeClientError.invalidBaseURL("The TCP port is missing after the colon.")
        }
        let port = components.port ?? 5080
        guard (1...65535).contains(port) else {
            throw BridgeClientError.invalidBaseURL("The TCP port must be between 1 and 65535.")
        }
        guard host == "localhost" || isPrivateIPv4(host) else {
            throw BridgeClientError.invalidBaseURL("Only localhost or a private IPv4 address is allowed.")
        }

        var normalized = URLComponents()
        normalized.scheme = "http"
        normalized.host = host
        normalized.port = port
        guard let result = normalized.url else {
            throw BridgeClientError.invalidBaseURL("The URL could not be normalized.")
        }
        return result
    }

    public static func normalizeBaseURL(_ url: URL?) throws -> URL {
        guard let url else {
            throw BridgeClientError.invalidBaseURL("The URL is missing.")
        }
        return try normalizeBaseURL(url)
    }

    public var hasCredential: Bool {
        withCredentialLock { credential != nil }
    }

    public func setCredential(_ credential: BridgeCredential) throws {
        let credentialURL = try Self.normalizeBaseURL(credential.baseURL)
        guard credentialURL == baseURL,
              !credential.accessToken.isEmpty,
              credential.tokenType.caseInsensitiveCompare("Bearer") == .orderedSame else {
            throw BridgeClientError.invalidResponse
        }
        let normalizedCredential = BridgeCredential(baseURL: baseURL, accessToken: credential.accessToken, tokenType: credential.tokenType, bridgeId: credential.bridgeId)
        withCredentialLock { self.credential = normalizedCredential }
    }

    public func clearCredential() {
        withCredentialLock { credential = nil }
    }

    public func health() async throws -> BridgeHealthResponse {
        let data = try await send(path: "api/v1/health", method: "GET", body: nil, requiresAuthentication: false)
        let response = try decode(BridgeHealthResponse.self, data: data)
        guard response.status.caseInsensitiveCompare("ok") == .orderedSame,
              response.apiVersion == "v1" else {
            throw BridgeClientError.invalidResponse
        }
        return response
    }

    /// Verifies the bearer against this client's normalized local bridge address.
    /// This is a single authenticated status request; it never retries and never touches hardware.
    public func verifyPairing() async throws -> BridgePairStatusResponse {
        guard hasCredential else { throw BridgeClientError.missingCredential }
        let data = try await send(
            path: "api/v1/pair/status",
            method: "GET",
            body: nil,
            requiresAuthentication: true
        )
        let response = try decode(BridgePairStatusResponse.self, data: data)
        guard response.version == "v1", response.paired else {
            throw BridgeClientError.invalidResponse
        }
        return response
    }

    /// Performs the unauthenticated token-bound proof before any authenticated request
    /// to a Bonjour candidate. A bad or malformed proof fails closed and never sends status.
    public func proveBonjourRelocation(expectedBridgeId: String, nonce: String) async throws {
        guard let credential = credentialSnapshot() else { throw BridgeClientError.missingCredential }
        guard BridgeRelocationProof.decodeUpperHex(nonce, byteCount: BridgeRelocationProof.digestByteCount) != nil,
              let canonicalURL = Self.canonicalBonjourURLString(baseURL) else {
            throw BridgeClientError.invalidResponse
        }

        let locator = BridgeRelocationProof.locator(for: credential.accessToken)
        let body = try JSONEncoder().encode(BridgePairRelocationProofRequest(
            locator: locator,
            nonce: nonce,
            url: canonicalURL
        ))
        let data = try await send(
            path: "api/v1/pair/proof",
            method: "POST",
            body: body,
            requiresAuthentication: false,
            // A malicious candidate must not be able to echo proof inputs into a
            // user-visible error. The bearer is also sanitized by send itself.
            ephemeralSecrets: [locator, nonce],
            maximumResponseBytes: 4_096
        )
        guard !Self.hasDuplicateTopLevelJSONKey(data) else {
            throw BridgeClientError.invalidJSON
        }
        let response = try decode(BridgePairRelocationProofResponse.self, data: data)
        guard BridgeRelocationProof.isValid(
            response: response,
            bearer: credential.accessToken,
            expectedBridgeId: expectedBridgeId,
            expectedNonce: nonce,
            canonicalURL: canonicalURL
        ) else {
            throw BridgeClientError.invalidResponse
        }
    }

    /// Verifies the current bearer at a candidate address without changing this client.
    /// The candidate is normalized and receives the same bearer only for this check.
    public func verifyPairing(at candidateBaseURL: URL) async throws -> BridgePairStatusResponse {
        let normalizedURL = try Self.normalizeBaseURL(candidateBaseURL)
        guard let current = credentialSnapshot() else { throw BridgeClientError.missingCredential }
        let candidateCredential = BridgeCredential(
            baseURL: normalizedURL,
            accessToken: current.accessToken,
            tokenType: current.tokenType,
            bridgeId: current.bridgeId
        )
        let candidate = try BridgeClient(
            baseURL: normalizedURL,
            session: session,
            credential: candidateCredential
        )
        return try await candidate.verifyPairing()
    }

    public func verifyPairing(at candidateBaseURLString: String) async throws -> BridgePairStatusResponse {
        try await verifyPairing(at: Self.normalizeBaseURL(candidateBaseURLString))
    }

    @discardableResult
    public func pair(
        pin: String,
        bridgeId: String? = nil,
        retryHealthOnUnreachable: Bool = true
    ) async throws -> BridgeCredential {
        guard Self.isSixDigitPIN(pin) else { throw BridgeClientError.invalidPIN }
        try await preflightHealth(retryOnUnreachable: retryHealthOnUnreachable)
        let body = try JSONEncoder().encode(BridgePairRequest(pin: pin))
        let data = try await send(
            path: "api/v1/pair",
            method: "POST",
            body: body,
            requiresAuthentication: false,
            ephemeralSecrets: [pin]
        )
        let response = try decode(BridgePairResponse.self, data: data)
        let newCredential = BridgeCredential(baseURL: baseURL, accessToken: response.accessToken, tokenType: response.tokenType, bridgeId: bridgeId)
        withCredentialLock { credential = newCredential }
        return newCredential
    }

    /// Revokes the current bearer at the bridge. Local credentials are cleared by the model
    /// only after this succeeds or returns 401.
    public func revoke() async throws {
        guard hasCredential else { throw BridgeClientError.missingCredential }
        _ = try await send(path: "api/v1/pair/revoke", method: "POST", body: nil, requiresAuthentication: true)
    }

    public func readBlock5() async throws -> BridgeBlockResponse {
        guard hasCredential else { throw BridgeClientError.missingCredential }
        let data = try await send(path: "api/v1/hardware/page0/block5", method: "GET", body: nil, requiresAuthentication: true)
        let response = try decode(BridgeBlockResponse.self, data: data)
        guard response.block == 5 else { throw BridgeClientError.invalidResponse }
        return response
    }

    /// Targeted known-token scan: block 4, mirrors 5/6, and LF signal strength.
    public func scanPage0() async throws -> BridgePage0ScanResponse {
        guard hasCredential else { throw BridgeClientError.missingCredential }
        let data = try await send(
            path: "api/v1/hardware/page0/scan",
            method: "GET",
            body: nil,
            requiresAuthentication: true
        )
        return try decode(BridgePage0ScanResponse.self, data: data)
    }

    /// Reads only the missing page-0 blocks required to assemble an unknown dump.
    public func readPage0MissingBlocks() async throws -> BridgePage0MissingBlocksResponse {
        guard hasCredential else { throw BridgeClientError.missingCredential }
        let data = try await send(
            path: "api/v1/hardware/page0/missing",
            method: "GET",
            body: nil,
            requiresAuthentication: true
        )
        return try decode(BridgePage0MissingBlocksResponse.self, data: data)
    }

    /// Reads the fixed allowlist of page-0 blocks 1...6 for reset planning.
    public func readPage0Blocks1To6() async throws -> BridgePage0Blocks1To6Response {
        guard hasCredential else { throw BridgeClientError.missingCredential }
        let data = try await send(
            path: "api/v1/hardware/page0/blocks1to6",
            method: "GET",
            body: nil,
            requiresAuthentication: true
        )
        return try decode(BridgePage0Blocks1To6Response.self, data: data)
    }

    /// Reads exactly the two page0 ride mirrors. This request is intentionally a
    /// one-shot hardware read: `send` has no retry path for hardware endpoints.
    public func readPage0Mirrors() async throws -> BridgePage0MirrorResponse {
        guard hasCredential else { throw BridgeClientError.missingCredential }
        let data = try await send(
            path: "api/v1/hardware/page0/mirrors",
            method: "GET",
            body: nil,
            requiresAuthentication: true
        )
        return try decode(BridgePage0MirrorResponse.self, data: data)
    }

    /// Sends one conditional mutation request. The bridge owns preflight, write,
    /// verification, and rollback; the client never replays this call.
    public func mutatePage0(_ request: BridgePage0MutationRequest) async throws -> BridgePage0MutationResponse {
        guard hasCredential else { throw BridgeClientError.missingCredential }
        let body: Data
        do {
            body = try JSONEncoder().encode(request)
        } catch {
            throw BridgeClientError.invalidResponse
        }
        let data = try await send(
            path: "api/v1/hardware/page0/mutations",
            method: "POST",
            body: body,
            requiresAuthentication: true
        )
        let response = try decode(BridgePage0MutationResponse.self, data: data)
        // The response is also an optimistic-concurrency acknowledgement. Do not
        // let a syntactically valid response for a different mutation update state.
        guard Set(response.results.map(\.block)) == Set(request.mutations.map(\.block)),
              response.results.count == request.mutations.count,
              response.results.allSatisfy({ result in
                  request.mutations.contains {
                      $0.block == result.block && $0.expected == result.expected && $0.desired == result.desired
                  }
              }) else {
            throw BridgeClientError.invalidResponse
        }
        return response
    }

    private func send(
        path: String,
        method: String,
        body: Data?,
        requiresAuthentication: Bool,
        ephemeralSecrets: [String] = [],
        maximumResponseBytes: Int? = nil
    ) async throws -> Data {
        var request = URLRequest(url: baseURL.appendingPathComponent(path))
        request.httpMethod = method
        request.httpBody = body
        request.setValue("application/json", forHTTPHeaderField: "Accept")
        if body != nil { request.setValue("application/json", forHTTPHeaderField: "Content-Type") }
        if requiresAuthentication {
            guard let credential = credentialSnapshot() else { throw BridgeClientError.missingCredential }
            // Keep the bearer value confined to the URLRequest; it is never included in errors or UI state.
            request.setValue("\(credential.tokenType) \(credential.accessToken)", forHTTPHeaderField: "Authorization")
        }

        request.cachePolicy = .reloadIgnoringLocalCacheData
        request.setValue("no-cache", forHTTPHeaderField: "Cache-Control")
        request.timeoutInterval = 30

        let data: Data
        let response: URLResponse
        do {
            if let maximumResponseBytes {
                guard maximumResponseBytes > 0 else { throw BridgeClientError.invalidResponse }
                let (bytes, streamedResponse) = try await session.bytes(for: request)
                response = streamedResponse
                var boundedData = Data()
                boundedData.reserveCapacity(min(maximumResponseBytes, 16_384))
                for try await byte in bytes {
                    guard boundedData.count < maximumResponseBytes else {
                        throw BridgeClientError.invalidResponse
                    }
                    boundedData.append(byte)
                }
                data = boundedData
            } else {
                (data, response) = try await session.data(for: request)
            }
        } catch let error as BridgeClientError {
            throw error
        } catch {
            let nsError = error as NSError
            if error is CancellationError || nsError.domain == "Swift.CancellationError" ||
                (nsError.domain == NSURLErrorDomain && nsError.code == NSURLErrorCancelled) {
                throw CancellationError()
            }
            if let urlError = error as? URLError {
                switch urlError.code {
                case .timedOut:
                    throw BridgeClientError.timeout
                case .cannotFindHost, .cannotConnectToHost, .networkConnectionLost, .notConnectedToInternet,
                     .dnsLookupFailed, .internationalRoamingOff, .dataNotAllowed:
                    throw BridgeClientError.unreachable
                default:
                    throw BridgeClientError.unreachable
                }
            }
            throw BridgeClientError.unreachable
        }

        guard let http = response as? HTTPURLResponse else { throw BridgeClientError.invalidResponse }
        if http.statusCode == 401 { throw BridgeClientError.unauthorized }
        guard (200...299).contains(http.statusCode) else {
            do {
                let error = try decode(BridgeErrorResponse.self, data: data)
                throw BridgeClientError.server(
                    code: sanitizedServerValue(error.code, ephemeralSecrets: ephemeralSecrets),
                    message: sanitizedServerValue(error.message, ephemeralSecrets: ephemeralSecrets),
                    statusCode: http.statusCode
                )
            } catch let error as BridgeClientError {
                throw error
            } catch {
                throw BridgeClientError.invalidJSON
            }
        }
        return data
    }

    private func decode<T: Decodable>(_ type: T.Type, data: Data) throws -> T {
        do {
            return try JSONDecoder().decode(type, from: data)
        } catch {
            throw BridgeClientError.invalidJSON
        }
    }

    private func preflightHealth(retryOnUnreachable: Bool) async throws {
        do {
            _ = try await health()
        } catch BridgeClientError.unreachable where retryOnUnreachable {
            // Manual pairing retains the existing local-network permission transition
            // workaround. QR import disables this branch: one scan means one readiness
            // check and one PIN attempt, never a network replay.
            try await healthRetryDelay()
            _ = try await health()
        }
    }

    private func credentialSnapshot() -> BridgeCredential? {
        withCredentialLock { credential }
    }

    private static func hasDuplicateTopLevelJSONKey(_ data: Data) -> Bool {
        let bytes = Array(data)
        var index = 0
        skipJSONWhitespace(bytes, &index)
        guard consumeJSONByte(0x7B, bytes, &index) else { return false } // {
        skipJSONWhitespace(bytes, &index)
        if consumeJSONByte(0x7D, bytes, &index) { return false } // }

        var keys = Set<String>()
        while index < bytes.count {
            skipJSONWhitespace(bytes, &index)
            guard index < bytes.count, bytes[index] == 0x22 else { return false } // \"
            let start = index
            guard skipJSONString(bytes, &index),
                  let key = try? JSONDecoder().decode(String.self, from: Data(bytes[start..<index])) else {
                return false
            }
            if !keys.insert(key).inserted { return true }
            skipJSONWhitespace(bytes, &index)
            guard consumeJSONByte(0x3A, bytes, &index), skipJSONValue(bytes, &index) else { return false } // :
            skipJSONWhitespace(bytes, &index)
            if consumeJSONByte(0x7D, bytes, &index) { return false } // }
            guard consumeJSONByte(0x2C, bytes, &index) else { return false } // ,
        }
        return false
    }

    private static func skipJSONWhitespace(_ bytes: [UInt8], _ index: inout Int) {
        while index < bytes.count && (bytes[index] == 0x20 || bytes[index] == 0x09 || bytes[index] == 0x0A || bytes[index] == 0x0D) {
            index += 1
        }
    }

    private static func consumeJSONByte(_ byte: UInt8, _ bytes: [UInt8], _ index: inout Int) -> Bool {
        guard index < bytes.count, bytes[index] == byte else { return false }
        index += 1
        return true
    }

    private static func skipJSONString(_ bytes: [UInt8], _ index: inout Int) -> Bool {
        guard consumeJSONByte(0x22, bytes, &index) else { return false } // \"
        while index < bytes.count {
            switch bytes[index] {
            case 0x22:
                index += 1
                return true
            case 0x5C:
                index += 2
            case 0x00...0x1F:
                return false
            default:
                index += 1
            }
        }
        return false
    }

    private static func skipJSONValue(_ bytes: [UInt8], _ index: inout Int) -> Bool {
        skipJSONWhitespace(bytes, &index)
        guard index < bytes.count else { return false }
        if bytes[index] == 0x22 { return skipJSONString(bytes, &index) }
        if bytes[index] == 0x7B || bytes[index] == 0x5B {
            let opening = bytes[index]
            let closing: UInt8 = opening == 0x7B ? 0x7D : 0x5D
            index += 1
            skipJSONWhitespace(bytes, &index)
            if consumeJSONByte(closing, bytes, &index) { return true }
            while index < bytes.count {
                if opening == 0x7B {
                    guard skipJSONString(bytes, &index) else { return false }
                    skipJSONWhitespace(bytes, &index)
                    guard consumeJSONByte(0x3A, bytes, &index) else { return false }
                }
                guard skipJSONValue(bytes, &index) else { return false }
                skipJSONWhitespace(bytes, &index)
                if consumeJSONByte(closing, bytes, &index) { return true }
                guard consumeJSONByte(0x2C, bytes, &index) else { return false }
                skipJSONWhitespace(bytes, &index)
            }
            return false
        }
        let start = index
        while index < bytes.count && ![0x20, 0x09, 0x0A, 0x0D, 0x2C, 0x5D, 0x7D].contains(bytes[index]) {
            index += 1
        }
        return index > start
    }

    private static func canonicalBonjourURLString(_ url: URL) -> String? {
        guard let components = URLComponents(url: url, resolvingAgainstBaseURL: false),
              components.scheme?.caseInsensitiveCompare("http") == .orderedSame,
              components.user == nil, components.password == nil,
              components.query == nil, components.fragment == nil,
              components.path.isEmpty || components.path == "/",
              let host = components.host, let port = components.port else { return nil }
        var canonical = URLComponents()
        canonical.scheme = "http"
        canonical.host = host
        canonical.port = port
        canonical.path = "/"
        return canonical.url?.absoluteString
    }

    private func sanitizedServerValue(_ value: String, ephemeralSecrets: [String]) -> String {
        var sanitized = value
        for secret in ephemeralSecrets where !secret.isEmpty {
            sanitized = sanitized.replacingOccurrences(of: secret, with: "[redacted]")
        }
        if let credential = credentialSnapshot(), !credential.accessToken.isEmpty {
            sanitized = sanitized.replacingOccurrences(of: credential.accessToken, with: "[redacted]")
        }
        return sanitized
    }

    private func withCredentialLock<T>(_ body: () -> T) -> T {
        credentialLock.lock()
        defer { credentialLock.unlock() }
        return body()
    }

    private static func hasEmptyPort(in absoluteURL: String) -> Bool {
        guard let separator = absoluteURL.range(of: "://") else { return false }
        let authorityStart = separator.upperBound
        let authorityEnd = absoluteURL[authorityStart...].firstIndex { "/?#".contains($0) } ?? absoluteURL.endIndex
        var authority = absoluteURL[authorityStart..<authorityEnd]
        if let credentialsEnd = authority.lastIndex(of: "@") {
            authority = authority[authority.index(after: credentialsEnd)...]
        }

        if authority.first == "[" {
            guard let closingBracket = authority.lastIndex(of: "]") else { return false }
            return authority[authority.index(after: closingBracket)...] == ":"
        }
        guard let colon = authority.lastIndex(of: ":") else { return false }
        return authority[authority.index(after: colon)...].isEmpty
    }

    private static func isPrivateIPv4(_ host: String) -> Bool {
        let parts = host.split(separator: ".", omittingEmptySubsequences: false)
        guard parts.count == 4 else { return false }
        var octets: [Int] = []
        for part in parts {
            guard !part.isEmpty,
                  part.utf8.allSatisfy({ $0 >= 48 && $0 <= 57 }),
                  let value = Int(part),
                  (0...255).contains(value) else { return false }
            octets.append(value)
        }
        let first = octets[0]
        let second = octets[1]
        if first == 127 { return true }
        if first == 10 { return true }
        if first == 172 && (16...31).contains(second) { return true }
        if first == 192 && second == 168 { return true }
        return first == 169 && second == 254
    }

    private static func isSixDigitPIN(_ pin: String) -> Bool {
        pin.utf8.count == 6 && pin.utf8.allSatisfy { $0 >= 48 && $0 <= 57 }
    }
}
