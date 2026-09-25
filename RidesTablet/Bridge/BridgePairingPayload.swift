import Foundation

/// The strict, public data carried by the RidesBridge first-pairing QR code.
/// It intentionally contains a short-lived PIN, never a bearer token or verifier.
public struct BridgePairingPayload: Equatable, Sendable {
    public static let currentType = "ridesbridge-pairing"
    public static let currentVersion = "v1"
    public static let currentAPIVersion = "v1"

    public let type: String
    public let version: String
    public let bridgeURL: URL
    public let pin: String
    public let expiresAt: Date
    public let bridgeId: String
    public let apiVersion: String

    public var url: URL { bridgeURL }

    public init(
        type: String = Self.currentType,
        version: String = Self.currentVersion,
        bridgeURL: URL,
        pin: String,
        expiresAt: Date,
        bridgeId: String,
        apiVersion: String = Self.currentAPIVersion
    ) {
        self.type = type
        self.version = version
        self.bridgeURL = bridgeURL
        self.pin = pin
        self.expiresAt = expiresAt
        self.bridgeId = bridgeId
        self.apiVersion = apiVersion
    }

    public init(
        type: String = Self.currentType,
        version: String = Self.currentVersion,
        url: URL,
        pin: String,
        expiresAt: Date,
        bridgeId: String,
        apiVersion: String = Self.currentAPIVersion
    ) {
        self.init(
            type: type,
            version: version,
            bridgeURL: url,
            pin: pin,
            expiresAt: expiresAt,
            bridgeId: bridgeId,
            apiVersion: apiVersion
        )
    }

    public func isExpired(at now: Date) -> Bool {
        expiresAt <= now
    }

    /// Parses the backend's exact JSON contract. This does not use synthesized Codable because
    /// JSONDecoder silently accepts duplicate object keys on supported OS versions.
    public static func parse(_ json: String, now: Date = Date()) throws -> Self {
        try parse(Data(json.utf8), now: now)
    }

    public static func parse(_ data: Data, now: Date = Date()) throws -> Self {
        guard !data.isEmpty else { throw BridgePairingPayloadError.empty }

        let keys: [String]
        do {
            var scanner = JSONKeyScanner(data: data)
            keys = try scanner.parse()
        } catch {
            throw BridgePairingPayloadError.invalidJSON
        }

        let expected: Set<String> = ["type", "version", "url", "pin", "expiresAt", "bridgeId", "apiVersion"]
        guard Set(keys) == expected, keys.count == expected.count else {
            throw BridgePairingPayloadError.invalidFields
        }

        let object: [String: Any]
        do {
            guard let decoded = try JSONSerialization.jsonObject(with: data, options: []) as? [String: Any] else {
                throw BridgePairingPayloadError.invalidJSON
            }
            object = decoded
        } catch let error as BridgePairingPayloadError {
            throw error
        } catch {
            throw BridgePairingPayloadError.invalidJSON
        }

        guard let type = object["type"] as? String else { throw BridgePairingPayloadError.invalidType }
        guard type == currentType else { throw BridgePairingPayloadError.unsupportedType }
        guard let version = object["version"] as? String else { throw BridgePairingPayloadError.invalidVersion }
        guard version == currentVersion else { throw BridgePairingPayloadError.unsupportedVersion }
        guard let urlText = object["url"] as? String else { throw BridgePairingPayloadError.invalidURL }
        let bridgeURL = try validateURL(urlText)
        guard let pin = object["pin"] as? String, isPIN(pin) else {
            throw BridgePairingPayloadError.invalidPIN
        }
        guard let expirationText = object["expiresAt"] as? String,
              let expiration = parseISO8601(expirationText) else {
            throw BridgePairingPayloadError.invalidExpiration
        }
        guard let bridgeId = object["bridgeId"] as? String, isBridgeId(bridgeId) else {
            throw BridgePairingPayloadError.invalidBridgeID
        }
        guard let apiVersion = object["apiVersion"] as? String else {
            throw BridgePairingPayloadError.invalidAPIVersion
        }
        guard apiVersion == currentAPIVersion else {
            throw BridgePairingPayloadError.unsupportedAPIVersion
        }
        guard expiration > now else { throw BridgePairingPayloadError.expired }

        return Self(
            type: type,
            version: version,
            bridgeURL: bridgeURL,
            pin: pin,
            expiresAt: expiration,
            bridgeId: bridgeId,
            apiVersion: apiVersion
        )
    }

    public init(json: String, now: Date = Date()) throws {
        self = try Self.parse(json, now: now)
    }

    public init(data: Data, now: Date = Date()) throws {
        self = try Self.parse(data, now: now)
    }

    /// Re-validates a value supplied by a caller rather than parsed from JSON.
    public func validated(now: Date = Date()) throws -> Self {
        guard type == Self.currentType else { throw BridgePairingPayloadError.unsupportedType }
        guard version == Self.currentVersion else { throw BridgePairingPayloadError.unsupportedVersion }
        let canonicalURL = try Self.validateURL(bridgeURL.absoluteString)
        guard Self.isPIN(pin) else { throw BridgePairingPayloadError.invalidPIN }
        guard Self.isBridgeId(bridgeId) else { throw BridgePairingPayloadError.invalidBridgeID }
        guard apiVersion == Self.currentAPIVersion else { throw BridgePairingPayloadError.unsupportedAPIVersion }
        guard expiresAt > now else { throw BridgePairingPayloadError.expired }
        return Self(
            type: type,
            version: version,
            bridgeURL: canonicalURL,
            pin: pin,
            expiresAt: expiresAt,
            bridgeId: bridgeId,
            apiVersion: apiVersion
        )
    }

    private static func validateURL(_ value: String) throws -> URL {
        guard let url = URL(string: value),
              let components = URLComponents(url: url, resolvingAgainstBaseURL: false),
              components.scheme?.lowercased() == "http",
              components.user == nil,
              components.password == nil,
              components.query == nil,
              components.fragment == nil,
              components.path.isEmpty || components.path == "/",
              let host = components.host,
              components.port != nil,
              let port = components.port,
              (1...65535).contains(port),
              let octets = parseIPv4(host),
              isPrivateNonLoopbackIPv4(octets) else {
            throw BridgePairingPayloadError.invalidURL
        }

        // The backend emits the canonical root URL. Normalize both parsed payloads and
        // caller-supplied values so URL equality and the initial credential are stable.
        var canonical = URLComponents()
        canonical.scheme = "http"
        canonical.host = host
        canonical.port = port
        canonical.path = "/"
        guard let result = canonical.url else { throw BridgePairingPayloadError.invalidURL }
        return result
    }

    private static func parseISO8601(_ value: String) -> Date? {
        // This is the ISO-8601 extended profile emitted by System.Text.Json for
        // DateTimeOffset: a fixed-width local date/time, optional 1...7 fractional
        // digits, and either Z or a numeric offset.
        let pattern = #"^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(\.[0-9]{1,7})?(Z|[+-][0-9]{2}:[0-9]{2})$"#
        guard let match = value.range(of: pattern, options: .regularExpression),
              match.lowerBound == value.startIndex,
              match.upperBound == value.endIndex else { return nil }

        let bytes = Array(value.utf8)
        func number(_ range: Range<Int>) -> Int? {
            Int(String(decoding: bytes[range], as: UTF8.self))
        }

        guard let year = number(0..<4),
              let month = number(5..<7),
              let day = number(8..<10),
              let hour = number(11..<13),
              let minute = number(14..<16),
              let second = number(17..<19),
              year >= 1,
              (1...12).contains(month),
              (1...31).contains(day),
              (0...23).contains(hour),
              (0...59).contains(minute),
              (0...59).contains(second) else { return nil }

        let hasFraction = bytes[19] == 46 // "."
        let zoneStart = bytes.last == 90 ? bytes.count - 1 : bytes.count - 6 // "Z" or +/-HH:MM
        var nanosecond = 0
        if hasFraction {
            let fractionLength = zoneStart - 20
            guard (1...7).contains(fractionLength),
                  let fraction = number(20..<zoneStart) else { return nil }
            nanosecond = fraction
            for _ in 0..<(9 - fractionLength) { nanosecond *= 10 }
        }

        if bytes.last != 90 {
            guard bytes[zoneStart] == 43 || bytes[zoneStart] == 45,
                  bytes[zoneStart + 3] == 58,
                  let offsetHour = number(zoneStart + 1..<zoneStart + 3),
                  let offsetMinute = number(zoneStart + 4..<zoneStart + 6),
                  (0...14).contains(offsetHour),
                  (0...59).contains(offsetMinute),
                  offsetHour < 14 || offsetMinute == 0 else { return nil }
        }

        // Calendar validation is explicit because ISO8601DateFormatter normalizes
        // some invalid no-fraction values (for example, February 30).
        var calendar = Calendar(identifier: .gregorian)
        calendar.timeZone = TimeZone(secondsFromGMT: 0)!
        var components = DateComponents()
        components.year = year
        components.month = month
        components.day = day
        components.hour = hour
        components.minute = minute
        components.second = second
        components.nanosecond = nanosecond
        guard let localDate = calendar.date(from: components) else { return nil }
        let normalized = calendar.dateComponents([.year, .month, .day, .hour, .minute, .second], from: localDate)
        guard normalized.year == year,
              normalized.month == month,
              normalized.day == day,
              normalized.hour == hour,
              normalized.minute == minute,
              normalized.second == second else { return nil }

        let formatter = ISO8601DateFormatter()
        formatter.timeZone = calendar.timeZone
        formatter.formatOptions = hasFraction
            ? [.withInternetDateTime, .withFractionalSeconds]
            : [.withInternetDateTime]
        return formatter.date(from: value)
    }

    private static func parseIPv4(_ value: String) -> [UInt8]? {
        let pieces = value.split(separator: ".", omittingEmptySubsequences: false)
        guard pieces.count == 4 else { return nil }
        var result: [UInt8] = []
        for piece in pieces {
            guard !piece.isEmpty,
                  piece.utf8.allSatisfy({ $0 >= 48 && $0 <= 57 }),
                  (piece.count == 1 || piece.first != "0"),
                  let number = Int(piece), number <= 255 else { return nil }
            result.append(UInt8(number))
        }
        return result
    }

    private static func isPrivateNonLoopbackIPv4(_ octets: [UInt8]) -> Bool {
        guard octets.count == 4 else { return false }
        let first = octets[0]
        let second = octets[1]
        return first == 10
            || (first == 172 && (16...31).contains(second))
            || (first == 192 && second == 168)
            || (first == 169 && second == 254)
    }

    private static func isPIN(_ value: String) -> Bool {
        value.utf8.count == 6 && value.utf8.allSatisfy { $0 >= 48 && $0 <= 57 }
    }

    private static func isBridgeId(_ value: String) -> Bool {
        value.utf8.count == 32 && value.utf8.allSatisfy {
            ($0 >= 48 && $0 <= 57) || ($0 >= 65 && $0 <= 70)
        }
    }
}

public enum BridgePairingPayloadError: Error, Equatable, LocalizedError, Sendable {
    case empty
    case invalidJSON
    case invalidFields
    case invalidType
    case unsupportedType
    case invalidVersion
    case unsupportedVersion
    case invalidURL
    case invalidPIN
    case invalidExpiration
    case invalidBridgeID
    case invalidAPIVersion
    case unsupportedAPIVersion
    case expired

    public var errorDescription: String? {
        switch self {
        case .empty: "The pairing QR is empty."
        case .invalidJSON, .invalidFields: "The pairing QR is malformed or contains unsupported fields."
        case .invalidType, .unsupportedType: "The pairing QR is not a RidesBridge pairing payload."
        case .invalidVersion, .unsupportedVersion: "The pairing QR uses an unsupported payload version."
        case .invalidURL: "The pairing QR must contain a private IPv4 HTTP address with an explicit port and no path, query, fragment, or credentials."
        case .invalidPIN: "The pairing QR contains an invalid pairing PIN."
        case .invalidExpiration: "The pairing QR expiration is not a valid ISO-8601 date."
        case .invalidBridgeID: "The pairing QR contains an invalid bridge identifier."
        case .invalidAPIVersion, .unsupportedAPIVersion: "The pairing QR uses an unsupported bridge API version."
        case .expired: "The pairing QR has expired. Request a fresh pairing QR."
        }
    }
}

// A small lexical pass is used solely to observe every top-level key. JSONSerialization then
// performs the complete JSON/type parse. This closes the duplicate-key hole without trusting
// a reusable secret or introducing a general JSON parser dependency.
private struct JSONKeyScanner {
    let data: Data
    var bytes: [UInt8] { Array(data) }
    var index = 0

    init(data: Data) {
        self.data = data
    }

    fileprivate mutating func parse() throws -> [String] {
        var keys: [String] = []
        skipWhitespace()
        guard consume(123) else { throw SyntaxError.invalid }
        skipWhitespace()
        if consume(125) {
            skipWhitespace()
            guard index == bytes.count else { throw SyntaxError.invalid }
            return keys
        }

        while true {
            skipWhitespace()
            let keyData = try consumeStringData()
            guard let key = try JSONSerialization.jsonObject(with: keyData, options: [.fragmentsAllowed]) as? String else {
                throw SyntaxError.invalid
            }
            keys.append(key)
            skipWhitespace()
            guard consume(58) else { throw SyntaxError.invalid }
            skipWhitespace()
            try skipValue()
            skipWhitespace()
            if consume(125) { break }
            guard consume(44) else { throw SyntaxError.invalid }
        }
        skipWhitespace()
        guard index == bytes.count else { throw SyntaxError.invalid }
        return keys
    }

    private mutating func skipValue() throws {
        guard index < bytes.count else { throw SyntaxError.invalid }
        switch bytes[index] {
        case 34: _ = try consumeStringData()
        case 123:
            index += 1
            skipWhitespace()
            if consume(125) { return }
            while true {
                skipWhitespace(); _ = try consumeStringData()
                skipWhitespace(); guard consume(58) else { throw SyntaxError.invalid }
                skipWhitespace(); try skipValue(); skipWhitespace()
                if consume(125) { return }
                guard consume(44) else { throw SyntaxError.invalid }
            }
        case 91:
            index += 1
            skipWhitespace()
            if consume(93) { return }
            while true {
                skipWhitespace(); try skipValue(); skipWhitespace()
                if consume(93) { return }
                guard consume(44) else { throw SyntaxError.invalid }
            }
        default:
            let start = index
            while index < bytes.count && ![44, 125, 93].contains(bytes[index]) { index += 1 }
            guard index > start else { throw SyntaxError.invalid }
        }
    }

    private mutating func consumeStringData() throws -> Data {
        guard index < bytes.count, bytes[index] == 34 else { throw SyntaxError.invalid }
        let start = index
        index += 1
        var escaped = false
        while index < bytes.count {
            let byte = bytes[index]
            index += 1
            if escaped {
                escaped = false
            } else if byte == 92 {
                escaped = true
            } else if byte == 34 {
                return Data(bytes[start..<index])
            }
        }
        throw SyntaxError.invalid
    }

    private mutating func consume(_ byte: UInt8) -> Bool {
        guard index < bytes.count, bytes[index] == byte else { return false }
        index += 1
        return true
    }

    private mutating func skipWhitespace() {
        while index < bytes.count && [9, 10, 13, 32].contains(bytes[index]) { index += 1 }
    }

    private enum SyntaxError: Error { case invalid }
}
