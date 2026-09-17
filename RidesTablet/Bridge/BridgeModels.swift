import Foundation

public enum BridgeValueValidation {
    public static func isUppercaseHex32(_ value: String) -> Bool {
        guard value.utf8.count == 8 else { return false }
        return value.utf8.allSatisfy { byte in
            (byte >= 48 && byte <= 57) || (byte >= 65 && byte <= 70)
        }
    }
}

private struct AnyBridgeCodingKey: CodingKey {
    let stringValue: String
    let intValue: Int? = nil

    init?(stringValue: String) { self.stringValue = stringValue }
    init?(intValue: Int) { return nil }
}

private func requireExactKeys(_ decoder: Decoder, _ expected: Set<String>) throws {
    let container = try decoder.container(keyedBy: AnyBridgeCodingKey.self)
    guard Set(container.allKeys.map(\.stringValue)) == expected else {
        throw DecodingError.dataCorrupted(
            DecodingError.Context(
                codingPath: decoder.codingPath,
                debugDescription: "The v1 DTO contains an unexpected or missing field."
            )
        )
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
        try requireExactKeys(decoder, ["accessToken", "tokenType"])
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
        try requireExactKeys(decoder, ["status", "apiVersion", "bridgeVersion"])
        let status = try container.decode(String.self, forKey: .status)
        let apiVersion = try container.decode(String.self, forKey: .apiVersion)
        let bridgeVersion = try container.decode(String.self, forKey: .bridgeVersion)
        guard !status.isEmpty, !apiVersion.isEmpty, !bridgeVersion.isEmpty else {
            throw DecodingError.dataCorruptedError(forKey: .status, in: container, debugDescription: "Health response contains an empty field.")
        }
        self.init(status: status, apiVersion: apiVersion, bridgeVersion: bridgeVersion)
    }
}

/// Exact response contract for the unauthenticated Bonjour relocation proof.
public struct BridgePairRelocationProofResponse: Codable, Equatable, Sendable {
    public let bridgeId: String
    public let apiVersion: String
    public let nonce: String
    public let proof: String

    public init(bridgeId: String, apiVersion: String, nonce: String, proof: String) {
        self.bridgeId = bridgeId
        self.apiVersion = apiVersion
        self.nonce = nonce
        self.proof = proof
    }

    private enum CodingKeys: String, CodingKey {
        case bridgeId, apiVersion, nonce, proof
    }

    public init(from decoder: Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        try requireExactKeys(decoder, ["bridgeId", "apiVersion", "nonce", "proof"])
        self.init(
            bridgeId: try container.decode(String.self, forKey: .bridgeId),
            apiVersion: try container.decode(String.self, forKey: .apiVersion),
            nonce: try container.decode(String.self, forKey: .nonce),
            proof: try container.decode(String.self, forKey: .proof)
        )
    }
}

/// Exact request contract for the unauthenticated Bonjour relocation proof.
public struct BridgePairRelocationProofRequest: Codable, Equatable, Sendable {
    public let locator: String
    public let nonce: String
    public let url: String

    public init(locator: String, nonce: String, url: String) {
        self.locator = locator
        self.nonce = nonce
        self.url = url
    }
}

/// Strict v1 response for the authenticated no-hardware pairing status check.
public struct BridgePairStatusResponse: Codable, Equatable, Sendable {
    public let version: String
    public let paired: Bool

    public init(version: String = "v1", paired: Bool = true) throws {
        guard version == "v1", paired else {
            throw BridgeContractError.invalidPairStatus
        }
        self.version = version
        self.paired = paired
    }

    public init(from decoder: Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        try requireExactKeys(decoder, ["version", "paired"])
        try self.init(
            version: container.decode(String.self, forKey: .version),
            paired: container.decode(Bool.self, forKey: .paired)
        )
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
        try requireExactKeys(decoder, ["block", "value"])
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
        try requireExactKeys(decoder, ["code", "message"])
        let code = try container.decode(String.self, forKey: .code)
        let message = try container.decode(String.self, forKey: .message)
        guard !code.isEmpty, !message.isEmpty else {
            throw DecodingError.dataCorruptedError(forKey: .message, in: container, debugDescription: "Bridge error must contain a code and message.")
        }
        self.init(code: code, message: message)
    }
}

/// Strict v1 response for GET /api/v1/hardware/mercury/mirrors.
public struct BridgeMercuryMirrorResponse: Codable, Equatable, Sendable {
    public let version: String
    public let block5: String
    public let block6: String

    public init(version: String = "v1", block5: String, block6: String) throws {
        guard version == "v1",
              BridgeValueValidation.isUppercaseHex32(block5),
              BridgeValueValidation.isUppercaseHex32(block6) else {
            throw BridgeContractError.invalidMercuryResponse
        }
        self.version = version
        self.block5 = block5
        self.block6 = block6
    }

    public init(from decoder: Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        try requireExactKeys(decoder, ["version", "block5", "block6"])
        try self.init(
            version: container.decode(String.self, forKey: .version),
            block5: container.decode(String.self, forKey: .block5),
            block6: container.decode(String.self, forKey: .block6)
        )
    }
}

/// One conditional raw-block mutation. Slice 2 permits only blocks 5 and 6.
public struct BridgeMercuryMutation: Codable, Equatable, Sendable {
    public let block: Int
    public let expected: String
    public let desired: String

    public init(block: Int, expected: String, desired: String) throws {
        guard block == 5 || block == 6,
              BridgeValueValidation.isUppercaseHex32(expected),
              BridgeValueValidation.isUppercaseHex32(desired) else {
            throw BridgeContractError.invalidMercuryMutation
        }
        self.block = block
        self.expected = expected
        self.desired = desired
    }

    public init(from decoder: Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        try requireExactKeys(decoder, ["block", "expected", "desired"])
        try self.init(
            block: container.decode(Int.self, forKey: .block),
            expected: container.decode(String.self, forKey: .expected),
            desired: container.decode(String.self, forKey: .desired)
        )
    }
}

/// Strict v1 request for POST /api/v1/hardware/mercury/mutations.
public struct BridgeMercuryMutationRequest: Codable, Equatable, Sendable {
    public let version: String
    public let mutations: [BridgeMercuryMutation]

    public init(version: String = "v1", mutations: [BridgeMercuryMutation]) throws {
        guard version == "v1", (1...2).contains(mutations.count),
              Set(mutations.map(\.block)).count == mutations.count else {
            throw BridgeContractError.invalidMercuryMutation
        }
        self.version = version
        self.mutations = mutations
    }

    public init(from decoder: Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        try requireExactKeys(decoder, ["version", "mutations"])
        try self.init(
            version: container.decode(String.self, forKey: .version),
            mutations: container.decode([BridgeMercuryMutation].self, forKey: .mutations)
        )
    }
}

public struct BridgeMercuryMutationBlockResult: Codable, Equatable, Sendable {
    public let block: Int
    public let status: String
    public let expected: String
    public let desired: String
    public let actual: String?

    fileprivate init(block: Int, status: String, expected: String, desired: String, actual: String?) throws {
        guard block == 5 || block == 6,
              BridgeValueValidation.isUppercaseHex32(expected),
              BridgeValueValidation.isUppercaseHex32(desired),
              actual == nil || BridgeValueValidation.isUppercaseHex32(actual!) else {
            throw BridgeContractError.invalidMercuryResponse
        }
        self.block = block
        self.status = status
        self.expected = expected
        self.desired = desired
        self.actual = actual
    }

    public init(from decoder: Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        try requireExactKeys(decoder, ["block", "status", "expected", "desired", "actual"])
        try self.init(
            block: container.decode(Int.self, forKey: .block),
            status: container.decode(String.self, forKey: .status),
            expected: container.decode(String.self, forKey: .expected),
            desired: container.decode(String.self, forKey: .desired),
            actual: container.decodeIfPresent(String.self, forKey: .actual)
        )
    }
}

public struct BridgeMercuryRollbackResult: Codable, Equatable, Sendable {
    public let block: Int
    public let expected: String
    public let actual: String?
    public let succeeded: Bool

    fileprivate init(block: Int, expected: String, actual: String?, succeeded: Bool) throws {
        guard block == 5 || block == 6,
              BridgeValueValidation.isUppercaseHex32(expected),
              actual == nil || BridgeValueValidation.isUppercaseHex32(actual!) else {
            throw BridgeContractError.invalidMercuryResponse
        }
        self.block = block
        self.expected = expected
        self.actual = actual
        self.succeeded = succeeded
    }

    public init(from decoder: Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        try requireExactKeys(decoder, ["block", "expected", "actual", "succeeded"])
        try self.init(
            block: container.decode(Int.self, forKey: .block),
            expected: container.decode(String.self, forKey: .expected),
            actual: container.decodeIfPresent(String.self, forKey: .actual),
            succeeded: container.decode(Bool.self, forKey: .succeeded)
        )
    }
}

/// Strict and semantically validated v1 response for the conditional mutation endpoint.
public struct BridgeMercuryMutationResponse: Codable, Equatable, Sendable {
    public let version: String
    public let status: String
    public let results: [BridgeMercuryMutationBlockResult]
    public let rollbackStatus: String
    public let rollback: [BridgeMercuryRollbackResult]

    public init(
        version: String,
        status: String,
        results: [BridgeMercuryMutationBlockResult],
        rollbackStatus: String,
        rollback: [BridgeMercuryRollbackResult]
    ) throws {
        guard version == "v1", !results.isEmpty, results.count <= 2,
              Set(results.map(\.block)).count == results.count,
              results.allSatisfy({ ["written", "alreadyApplied", "conflict", "verifyFailed", "notAttempted"].contains($0.status) }),
              ["written", "alreadyApplied", "conflict", "verifyFailed"].contains(status),
              ["notNeeded", "rollbackSucceeded", "rollbackIncomplete"].contains(rollbackStatus),
              rollback.allSatisfy({ result in
                  results.contains(where: { $0.block == result.block && $0.expected == result.expected })
              }),
              Set(rollback.map(\.block)).count == rollback.count else {
            throw BridgeContractError.invalidMercuryResponse
        }

        switch status {
        case "written":
            guard rollbackStatus == "notNeeded", rollback.isEmpty,
                  results.allSatisfy({ ($0.status == "written" || $0.status == "alreadyApplied") && $0.actual == $0.desired }) else {
                throw BridgeContractError.invalidMercuryResponse
            }
        case "alreadyApplied":
            guard rollbackStatus == "notNeeded", rollback.isEmpty,
                  results.allSatisfy({ $0.status == "alreadyApplied" && $0.actual == $0.desired }) else {
                throw BridgeContractError.invalidMercuryResponse
            }
        case "conflict":
            // The bridge reports the complete preflight set as `conflict` when any
            // target is stale. Other targets may already be at the desired value
            // (or still equal their expected value), so only one result must prove
            // that it is neither expected nor desired.
            guard rollbackStatus == "notNeeded", rollback.isEmpty,
                  results.allSatisfy({ $0.status == "conflict" && $0.actual != nil }),
                  results.contains(where: { result in
                      guard let actual = result.actual else { return false }
                      return actual != result.expected && actual != result.desired
                  }) else {
                throw BridgeContractError.invalidMercuryResponse
            }
        case "verifyFailed":
            guard results.contains(where: { $0.status == "verifyFailed" }) else {
                throw BridgeContractError.invalidMercuryResponse
            }
            if rollbackStatus == "notNeeded" {
                guard rollback.isEmpty else { throw BridgeContractError.invalidMercuryResponse }
            } else {
                guard !rollback.isEmpty else { throw BridgeContractError.invalidMercuryResponse }
                if rollbackStatus == "rollbackSucceeded" && !rollback.allSatisfy(\.succeeded) {
                    throw BridgeContractError.invalidMercuryResponse
                }
                if rollbackStatus == "rollbackIncomplete" && rollback.allSatisfy(\.succeeded) {
                    throw BridgeContractError.invalidMercuryResponse
                }
            }
            guard rollback.allSatisfy({ !$0.succeeded || $0.actual == $0.expected }) else {
                throw BridgeContractError.invalidMercuryResponse
            }
        default:
            throw BridgeContractError.invalidMercuryResponse
        }

        self.version = version
        self.status = status
        self.results = results
        self.rollbackStatus = rollbackStatus
        self.rollback = rollback
    }

    public init(from decoder: Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        try requireExactKeys(decoder, ["version", "status", "results", "rollbackStatus", "rollback"])
        try self.init(
            version: container.decode(String.self, forKey: .version),
            status: container.decode(String.self, forKey: .status),
            results: container.decode([BridgeMercuryMutationBlockResult].self, forKey: .results),
            rollbackStatus: container.decode(String.self, forKey: .rollbackStatus),
            rollback: container.decode([BridgeMercuryRollbackResult].self, forKey: .rollback)
        )
    }
}

public enum BridgeContractError: Error, Equatable, Sendable {
    case invalidPairStatus
    case invalidBlockValue
    case invalidMercuryMutation
    case invalidMercuryResponse
}

// Short aliases keep the DTO names easy to use at call sites while retaining their
// transport-specific meaning in API documentation.
public typealias MercuryMirrorReadResponse = BridgeMercuryMirrorResponse
public typealias MercuryMutation = BridgeMercuryMutation
public typealias MercuryMutationRequest = BridgeMercuryMutationRequest
public typealias MercuryMutationResponse = BridgeMercuryMutationResponse
