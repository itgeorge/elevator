import Foundation

public enum BridgeValueValidation {
    public static func isUppercaseHex32(_ value: String) -> Bool {
        guard value.utf8.count == 8 else { return false }
        return value.utf8.allSatisfy { byte in
            (byte >= 48 && byte <= 57) || (byte >= 65 && byte <= 70)
        }
    }
}

public struct BridgePairRequest: Codable, Equatable, Sendable {
    public let pin: String

    public init(pin: String) {
        self.pin = pin
    }
}

public struct BridgePairResponse: Codable, Equatable, Sendable {
    public let accessToken: String
    public let tokenType: String

    public init(accessToken: String, tokenType: String) {
        self.accessToken = accessToken
        self.tokenType = tokenType
    }

    public init(from decoder: Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        let accessToken = try container.decode(String.self, forKey: .accessToken)
        let tokenType = try container.decode(String.self, forKey: .tokenType)
        guard !accessToken.isEmpty, tokenType.caseInsensitiveCompare("Bearer") == .orderedSame else {
            throw DecodingError.dataCorruptedError(forKey: .tokenType, in: container, debugDescription: "Pair response must contain a bearer token.")
        }
        self.init(accessToken: accessToken, tokenType: tokenType)
    }
}

public struct BridgeHealthResponse: Codable, Equatable, Sendable {
    public let status: String
    public let apiVersion: String
    public let bridgeVersion: String

    public init(status: String, apiVersion: String, bridgeVersion: String) {
        self.status = status
        self.apiVersion = apiVersion
        self.bridgeVersion = bridgeVersion
    }

    public init(from decoder: Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        let status = try container.decode(String.self, forKey: .status)
        let apiVersion = try container.decode(String.self, forKey: .apiVersion)
        let bridgeVersion = try container.decode(String.self, forKey: .bridgeVersion)
        guard !status.isEmpty, !apiVersion.isEmpty, !bridgeVersion.isEmpty else {
            throw DecodingError.dataCorruptedError(forKey: .status, in: container, debugDescription: "Health response contains an empty field.")
        }
        self.init(status: status, apiVersion: apiVersion, bridgeVersion: bridgeVersion)
    }
}

public struct BridgeBlockResponse: Codable, Equatable, Sendable {
    public let block: Int
    public let value: String

    public init(block: Int, value: String) throws {
        guard block == 5, BridgeValueValidation.isUppercaseHex32(value) else {
            throw BridgeContractError.invalidBlockValue
        }
        self.block = block
        self.value = value
    }

    public init(from decoder: Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        let block = try container.decode(Int.self, forKey: .block)
        let value = try container.decode(String.self, forKey: .value)
        guard block == 5, BridgeValueValidation.isUppercaseHex32(value) else {
            throw DecodingError.dataCorruptedError(forKey: .value, in: container, debugDescription: "Block 5 response must contain exactly eight uppercase hexadecimal characters.")
        }
        self.block = block
        self.value = value
    }
}

public struct BridgeErrorResponse: Codable, Equatable, Sendable {
    public let code: String
    public let message: String

    public init(code: String, message: String) {
        self.code = code
        self.message = message
    }

    public init(from decoder: Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        let code = try container.decode(String.self, forKey: .code)
        let message = try container.decode(String.self, forKey: .message)
        guard !code.isEmpty, !message.isEmpty else {
            throw DecodingError.dataCorruptedError(forKey: .message, in: container, debugDescription: "Bridge error must contain a code and message.")
        }
        self.init(code: code, message: message)
    }
}

public enum BridgeContractError: Error, Equatable, Sendable {
    case invalidBlockValue
}
