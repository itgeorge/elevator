#if DEBUG

import Foundation

/// The result of the one-shot, launch-triggered Slice 2 physical acceptance run.
public struct BridgePhysicalAcceptanceSummary: Equatable, Sendable {
    public let originalBlock5: String
    public let originalBlock6: String
    public let targetRides: UInt
    public let secondTargetRides: UInt

    public var conciseDescription: String {
        "Slice 2 physical acceptance passed (original5=\(originalBlock5), original6=\(originalBlock6), targetRides=\(targetRides))."
    }

    public init(originalBlock5: String, originalBlock6: String, targetRides: UInt, secondTargetRides: UInt) {
        self.originalBlock5 = originalBlock5
        self.originalBlock6 = originalBlock6
        self.targetRides = targetRides
        self.secondTargetRides = secondTargetRides
    }
}

public struct BridgePhysicalAcceptanceFailure: Equatable, Sendable {
    public let stage: String
    public let originalBlock5: String?
    public let originalBlock6: String?

    public init(stage: String, originalBlock5: String?, originalBlock6: String?) {
        self.stage = stage
        self.originalBlock5 = originalBlock5
        self.originalBlock6 = originalBlock6
    }
}

public enum BridgePhysicalAcceptanceResult: Equatable, Sendable {
    case success(BridgePhysicalAcceptanceSummary)
    case failure(BridgePhysicalAcceptanceFailure)
}

/// Runs the deliberately destructive Slice 2 acceptance sequence exactly once.
///
/// This type is DEBUG-only because it performs real Mercury mutations. It has no
/// retry or recovery path: an error ends the run at the stage that observed it.
public final class BridgePhysicalAcceptanceCoordinator: @unchecked Sendable {
    public static let initialReadStage = "initial-read"
    public static let firstMutationStage = "first-mutation"
    public static let alreadyAppliedStage = "already-applied"
    public static let staleConflictStage = "stale-conflict"
    public static let targetReadStage = "target-read"
    public static let restoreStage = "restore"
    public static let finalReadStage = "final-read"

    private let client: BridgeClient
    private let log: @Sendable (String) -> Void

    public init(
        client: BridgeClient,
        log: @escaping @Sendable (String) -> Void = { print($0) }
    ) {
        self.client = client
        self.log = log
    }

    public func run() async -> BridgePhysicalAcceptanceResult {
        var originalBlock5: String?
        var originalBlock6: String?

        let initial: BridgeMercuryMirrorResponse
        let initialResolution: MercuryRideRead
        do {
            initial = try await client.readMercuryMirrors()
            originalBlock5 = initial.block5
            originalBlock6 = initial.block6
            guard let raw5 = UInt32(initial.block5, radix: 16),
                  let raw6 = UInt32(initial.block6, radix: 16) else {
                throw AcceptanceError.invalidInitialResolution
            }
            initialResolution = MercuryMirrorResolver.resolve(block5: raw5, block6: raw6)
            guard initialResolution.status == .success,
                  let currentRides = initialResolution.rides,
                  MercuryRideCodec.encode(currentRides) != nil else {
                throw AcceptanceError.invalidInitialResolution
            }
            // This is intentionally before the first mutation. Raw mirror values
            // are safe to print; bearer, PIN, and auth headers never are.
            log("RIDES_PHASE2_ACCEPTANCE_ORIGINAL original5=\(initial.block5) original6=\(initial.block6)")
        } catch {
            return failure(stage: Self.initialReadStage, originalBlock5: originalBlock5, originalBlock6: originalBlock6)
        }

        guard let currentRides = initialResolution.rides,
              let targetRides = adjacentValidRides(to: currentRides),
              let secondTargetRides = secondValidRides(current: currentRides, first: targetRides),
              let targetRaw = encodedHex(targetRides),
              let secondTargetRaw = encodedHex(secondTargetRides) else {
            return failure(stage: Self.initialReadStage, originalBlock5: originalBlock5, originalBlock6: originalBlock6)
        }

        do {
            let request = try mutationRequest(
                expected5: initial.block5,
                expected6: initial.block6,
                desired5: targetRaw,
                desired6: targetRaw
            )
            let response = try await client.mutateMercury(request)
            try requireSuccessfulMutation(
                response,
                matching: request,
                topStatus: "written",
                actual5: targetRaw,
                actual6: targetRaw,
                allowedBlockStatuses: ["written", "alreadyApplied"]
            )
        } catch {
            return failure(stage: Self.firstMutationStage, originalBlock5: originalBlock5, originalBlock6: originalBlock6)
        }

        do {
            let request = try mutationRequest(
                expected5: targetRaw,
                expected6: targetRaw,
                desired5: targetRaw,
                desired6: targetRaw
            )
            let response = try await client.mutateMercury(request)
            try requireSuccessfulMutation(
                response,
                matching: request,
                topStatus: "alreadyApplied",
                actual5: targetRaw,
                actual6: targetRaw,
                allowedBlockStatuses: ["alreadyApplied"]
            )
        } catch {
            return failure(stage: Self.alreadyAppliedStage, originalBlock5: originalBlock5, originalBlock6: originalBlock6)
        }

        do {
            let request = try mutationRequest(
                expected5: initial.block5,
                expected6: initial.block6,
                desired5: secondTargetRaw,
                desired6: secondTargetRaw
            )
            let response = try await client.mutateMercury(request)
            guard response.status == "conflict",
                  matchingResults(response, request: request),
                  response.results.allSatisfy({
                      $0.status == "conflict" && $0.actual == targetRaw && $0.actual != secondTargetRaw
                  }) else {
                throw AcceptanceError.invalidMutationResponse
            }
        } catch {
            return failure(stage: Self.staleConflictStage, originalBlock5: originalBlock5, originalBlock6: originalBlock6)
        }

        do {
            let fresh = try await client.readMercuryMirrors()
            guard fresh.block5 == targetRaw, fresh.block6 == targetRaw else {
                throw AcceptanceError.invalidMutationResponse
            }
            let resolution = try resolve(fresh)
            guard resolution.status == .success, resolution.rides == targetRides else {
                throw AcceptanceError.invalidMutationResponse
            }
        } catch {
            return failure(stage: Self.targetReadStage, originalBlock5: originalBlock5, originalBlock6: originalBlock6)
        }

        do {
            let request = try mutationRequest(
                expected5: targetRaw,
                expected6: targetRaw,
                desired5: initial.block5,
                desired6: initial.block6
            )
            let response = try await client.mutateMercury(request)
            try requireSuccessfulMutation(
                response,
                matching: request,
                topStatus: nil,
                actual5: initial.block5,
                actual6: initial.block6,
                allowedTopStatuses: ["written", "alreadyApplied"],
                allowedBlockStatuses: ["written", "alreadyApplied"]
            )
        } catch {
            return failure(stage: Self.restoreStage, originalBlock5: originalBlock5, originalBlock6: originalBlock6)
        }

        do {
            let final = try await client.readMercuryMirrors()
            guard final.block5 == initial.block5, final.block6 == initial.block6 else {
                throw AcceptanceError.invalidMutationResponse
            }
            let finalResolution = try resolve(final)
            guard finalResolution == initialResolution else {
                throw AcceptanceError.invalidMutationResponse
            }
        } catch {
            return failure(stage: Self.finalReadStage, originalBlock5: originalBlock5, originalBlock6: originalBlock6)
        }

        let summary = BridgePhysicalAcceptanceSummary(
            originalBlock5: initial.block5,
            originalBlock6: initial.block6,
            targetRides: targetRides,
            secondTargetRides: secondTargetRides
        )
        log("RIDES_PHASE2_ACCEPTANCE_SUCCESS original5=\(summary.originalBlock5) original6=\(summary.originalBlock6) targetRides=\(summary.targetRides)")
        return .success(summary)
    }

    private func failure(stage: String, originalBlock5: String?, originalBlock6: String?) -> BridgePhysicalAcceptanceResult {
        var line = "RIDES_PHASE2_ACCEPTANCE_FAILURE stage=\(stage)"
        if let originalBlock5 { line += " original5=\(originalBlock5)" }
        if let originalBlock6 { line += " original6=\(originalBlock6)" }
        log(line)
        return .failure(BridgePhysicalAcceptanceFailure(stage: stage, originalBlock5: originalBlock5, originalBlock6: originalBlock6))
    }

    private func mutationRequest(
        expected5: String,
        expected6: String,
        desired5: String,
        desired6: String
    ) throws -> BridgeMercuryMutationRequest {
        try BridgeMercuryMutationRequest(mutations: [
            try BridgeMercuryMutation(block: 5, expected: expected5, desired: desired5),
            try BridgeMercuryMutation(block: 6, expected: expected6, desired: desired6)
        ])
    }

    private func requireSuccessfulMutation(
        _ response: BridgeMercuryMutationResponse,
        matching request: BridgeMercuryMutationRequest,
        topStatus: String?,
        actual5: String,
        actual6: String,
        allowedTopStatuses: Set<String> = [],
        allowedBlockStatuses: Set<String>
    ) throws {
        if let topStatus {
            guard response.status == topStatus else { throw AcceptanceError.invalidMutationResponse }
        } else {
            guard allowedTopStatuses.contains(response.status) else { throw AcceptanceError.invalidMutationResponse }
        }
        guard matchingResults(response, request: request),
              response.results.allSatisfy({ result in
                  let expectedActual = result.block == 5 ? actual5 : actual6
                  return allowedBlockStatuses.contains(result.status) && result.actual == expectedActual
              }) else {
            throw AcceptanceError.invalidMutationResponse
        }
    }

    private func resolve(_ mirrors: BridgeMercuryMirrorResponse) throws -> MercuryRideRead {
        guard let raw5 = UInt32(mirrors.block5, radix: 16),
              let raw6 = UInt32(mirrors.block6, radix: 16) else {
            throw AcceptanceError.invalidMutationResponse
        }
        return MercuryMirrorResolver.resolve(block5: raw5, block6: raw6)
    }

    private func encodedHex(_ rides: UInt) -> String? {
        guard let encoded = MercuryRideCodec.encode(rides) else { return nil }
        return String(format: "%08X", encoded)
    }

    private func adjacentValidRides(to current: UInt) -> UInt? {
        current < MercuryRideCodec.maximumRides ? current + 1 : current - 1
    }

    private func secondValidRides(current: UInt, first: UInt) -> UInt? {
        let candidate = current < MercuryRideCodec.maximumRides - 1 ? current + 2 : current - 2
        guard candidate <= MercuryRideCodec.maximumRides,
              candidate != current,
              candidate != first else { return nil }
        return candidate
    }

    private func matchingResults(_ response: BridgeMercuryMutationResponse, request: BridgeMercuryMutationRequest) -> Bool {
        response.results.count == request.mutations.count &&
        Set(response.results.map(\.block)) == Set(request.mutations.map(\.block)) &&
        response.results.allSatisfy { result in
            request.mutations.contains {
                $0.block == result.block && $0.expected == result.expected && $0.desired == result.desired
            }
        }
    }

    private enum AcceptanceError: Error {
        case invalidInitialResolution
        case invalidMutationResponse
    }
}

#endif
