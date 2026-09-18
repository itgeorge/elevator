#if DEBUG

import Foundation

/// The result of the one-shot, launch-triggered Slice 4 physical acceptance run.
public struct BridgeSlice4PhysicalAcceptanceSummary: Equatable, Sendable {
    public let block4: String
    public let originalBlock5: String
    public let originalBlock6: String
    public let signalMillivolts: Int
    public let originalRides: UInt
    public let resetBlock5: String
    public let resetBlock6: String

    public var conciseDescription: String {
        "Slice 4 physical acceptance passed (block4=\(block4), original5=\(originalBlock5), original6=\(originalBlock6), signal=\(signalMillivolts)mV, reset5=\(resetBlock5), reset6=\(resetBlock6))."
    }

    public init(
        block4: String,
        originalBlock5: String,
        originalBlock6: String,
        signalMillivolts: Int,
        originalRides: UInt,
        resetBlock5: String,
        resetBlock6: String
    ) {
        self.block4 = block4
        self.originalBlock5 = originalBlock5
        self.originalBlock6 = originalBlock6
        self.signalMillivolts = signalMillivolts
        self.originalRides = originalRides
        self.resetBlock5 = resetBlock5
        self.resetBlock6 = resetBlock6
    }
}

public struct BridgeSlice4PhysicalAcceptanceFailure: Equatable, Sendable {
    public let stage: String
    public let block4: String?
    public let originalBlock5: String?
    public let originalBlock6: String?

    public init(stage: String, block4: String?, originalBlock5: String?, originalBlock6: String?) {
        self.stage = stage
        self.block4 = block4
        self.originalBlock5 = originalBlock5
        self.originalBlock6 = originalBlock6
    }
}

public enum BridgeSlice4PhysicalAcceptanceResult: Equatable, Sendable {
    case success(BridgeSlice4PhysicalAcceptanceSummary)
    case failure(BridgeSlice4PhysicalAcceptanceFailure)
}

/// Runs the Slice 4 scan + explicit Venus reset acceptance sequence exactly once.
///
/// This type is DEBUG-only because it performs real page0 mutations against the bridge.
/// It has no retry or recovery path: an error ends the run at the stage that observed it.
public final class BridgeSlice4PhysicalAcceptanceCoordinator: @unchecked Sendable {
    public static let scanStage = "scan"
    public static let resetPlanStage = "reset-plan"
    public static let resetMutationStage = "reset-mutation"
    public static let postResetVerifyStage = "post-reset-verify"
    public static let restoreStage = "restore"
    public static let finalVerifyStage = "final-verify"

    /// Default `--fake-pm3` Venus seed values from `FakePm3Device`.
    public static let expectedBlock4 = "D6D1C733"
    public static let expectedBlock5 = "BBC7FD03"
    public static let expectedBlock6 = "BBC7FD03"
    public static let expectedSignalMillivolts = 420
    public static let expectedRides: UInt = 180
    public static let expectedSequence = RideSequence.venus

    private let client: BridgeClient
    private let log: @Sendable (String) -> Void

    public init(
        client: BridgeClient,
        log: @escaping @Sendable (String) -> Void = { print($0) }
    ) {
        self.client = client
        self.log = log
    }

    public func run() async -> BridgeSlice4PhysicalAcceptanceResult {
        var block4: String?
        var originalBlock5: String?
        var originalBlock6: String?

        let scan: BridgePage0ScanResponse
        let initialResolution: RideRead
        do {
            scan = try await client.scanPage0()
            block4 = scan.block4
            originalBlock5 = scan.block5
            originalBlock6 = scan.block6
            guard scan.block4 == Self.expectedBlock4,
                  scan.block5 == Self.expectedBlock5,
                  scan.block6 == Self.expectedBlock6,
                  scan.signalMillivolts == Self.expectedSignalMillivolts else {
                throw AcceptanceError.invalidScan
            }
            guard let raw5 = UInt32(scan.block5, radix: 16),
                  let raw6 = UInt32(scan.block6, radix: 16) else {
                throw AcceptanceError.invalidScan
            }
            initialResolution = RideBlockResolver.resolve(block5: raw5, block6: raw6)
            guard initialResolution.status == .success,
                  initialResolution.sequence == Self.expectedSequence,
                  initialResolution.rides == Self.expectedRides else {
                throw AcceptanceError.invalidScan
            }
            log("RIDES_PHASE4_ACCEPTANCE_SCAN block4=\(scan.block4) block5=\(scan.block5) block6=\(scan.block6) signal=\(scan.signalMillivolts) rides=\(Self.expectedRides) sequence=\(Self.expectedSequence.rawValue)")
        } catch {
            return failure(stage: Self.scanStage, block4: block4, originalBlock5: originalBlock5, originalBlock6: originalBlock6)
        }

        let venusProfile = ResetSequence.for(.venus)
        let resetDesired5 = formatBlock(venusProfile.resetImage()[5])
        let resetDesired6 = formatBlock(venusProfile.resetImage()[6])

        let planned: [Page0ResetPlanningWorkflow.PlannedMutation]
        do {
            let current = try await client.readPage0Blocks1To6()
            planned = try Page0ResetPlanningWorkflow.planMutations(
                currentBlocks: current.blocks,
                profile: venusProfile
            )
            guard planned.map(\.block) == [5, 6],
                  planned.allSatisfy({
                      ($0.block == 5 && $0.expected == Self.expectedBlock5 && $0.desired == resetDesired5) ||
                      ($0.block == 6 && $0.expected == Self.expectedBlock6 && $0.desired == resetDesired6)
                  }) else {
                throw AcceptanceError.invalidResetPlan
            }
            log("RIDES_PHASE4_ACCEPTANCE_RESET_PLAN blocks=5,6 desired5=\(resetDesired5) desired6=\(resetDesired6)")
        } catch {
            return failure(stage: Self.resetPlanStage, block4: block4, originalBlock5: originalBlock5, originalBlock6: originalBlock6)
        }

        let resetActual: (block5: String, block6: String)
        do {
            let request = try BridgePage0MutationRequest(mutations: planned.map {
                try BridgePage0Mutation(block: $0.block, expected: $0.expected, desired: $0.desired)
            })
            let response = try await client.mutatePage0(request)
            try requireSuccessfulMutation(
                response,
                matching: request,
                topStatus: nil,
                actual5: resetDesired5,
                actual6: resetDesired6,
                allowedTopStatuses: ["written", "alreadyApplied"],
                allowedBlockStatuses: ["written", "alreadyApplied"]
            )
            resetActual = try actualValues(for: response, matching: request)
            log("RIDES_PHASE4_ACCEPTANCE_RESET actual5=\(resetActual.block5) actual6=\(resetActual.block6)")
        } catch {
            return failure(stage: Self.resetMutationStage, block4: block4, originalBlock5: originalBlock5, originalBlock6: originalBlock6)
        }

        do {
            let postReset = try resolve(block5: resetActual.block5, block6: resetActual.block6)
            guard postReset.status == .success,
                  postReset.sequence == Self.expectedSequence,
                  postReset.rides == 0 else {
                throw AcceptanceError.invalidPostReset
            }
            log("RIDES_PHASE4_ACCEPTANCE_POST_RESET rides=0 sequence=\(Self.expectedSequence.rawValue)")
        } catch {
            return failure(stage: Self.postResetVerifyStage, block4: block4, originalBlock5: originalBlock5, originalBlock6: originalBlock6)
        }

        do {
            let request = try mutationRequest(
                expected5: resetActual.block5,
                expected6: resetActual.block6,
                desired5: Self.expectedBlock5,
                desired6: Self.expectedBlock6
            )
            let response = try await client.mutatePage0(request)
            try requireSuccessfulMutation(
                response,
                matching: request,
                topStatus: nil,
                actual5: Self.expectedBlock5,
                actual6: Self.expectedBlock6,
                allowedTopStatuses: ["written", "alreadyApplied"],
                allowedBlockStatuses: ["written", "alreadyApplied"]
            )
            log("RIDES_PHASE4_ACCEPTANCE_RESTORE actual5=\(Self.expectedBlock5) actual6=\(Self.expectedBlock6)")
        } catch {
            return failure(stage: Self.restoreStage, block4: block4, originalBlock5: originalBlock5, originalBlock6: originalBlock6)
        }

        do {
            let final = try await client.readPage0Mirrors()
            guard final.block5 == Self.expectedBlock5, final.block6 == Self.expectedBlock6 else {
                throw AcceptanceError.invalidFinalVerify
            }
            let finalResolution = try resolve(block5: final.block5, block6: final.block6)
            guard finalResolution == initialResolution else {
                throw AcceptanceError.invalidFinalVerify
            }
        } catch {
            return failure(stage: Self.finalVerifyStage, block4: block4, originalBlock5: originalBlock5, originalBlock6: originalBlock6)
        }

        let summary = BridgeSlice4PhysicalAcceptanceSummary(
            block4: block4!,
            originalBlock5: originalBlock5!,
            originalBlock6: originalBlock6!,
            signalMillivolts: Self.expectedSignalMillivolts,
            originalRides: Self.expectedRides,
            resetBlock5: resetDesired5,
            resetBlock6: resetDesired6
        )
        log("RIDES_PHASE4_ACCEPTANCE_SUCCESS block4=\(summary.block4) original5=\(summary.originalBlock5) original6=\(summary.originalBlock6) rides=\(summary.originalRides)")
        return .success(summary)
    }

    private func failure(
        stage: String,
        block4: String?,
        originalBlock5: String?,
        originalBlock6: String?
    ) -> BridgeSlice4PhysicalAcceptanceResult {
        var line = "RIDES_PHASE4_ACCEPTANCE_FAILURE stage=\(stage)"
        if let block4 { line += " block4=\(block4)" }
        if let originalBlock5 { line += " original5=\(originalBlock5)" }
        if let originalBlock6 { line += " original6=\(originalBlock6)" }
        log(line)
        return .failure(BridgeSlice4PhysicalAcceptanceFailure(
            stage: stage,
            block4: block4,
            originalBlock5: originalBlock5,
            originalBlock6: originalBlock6
        ))
    }

    private func mutationRequest(
        expected5: String,
        expected6: String,
        desired5: String,
        desired6: String
    ) throws -> BridgePage0MutationRequest {
        try BridgePage0MutationRequest(mutations: [
            try BridgePage0Mutation(block: 5, expected: expected5, desired: desired5),
            try BridgePage0Mutation(block: 6, expected: expected6, desired: desired6)
        ])
    }

    private func requireSuccessfulMutation(
        _ response: BridgePage0MutationResponse,
        matching request: BridgePage0MutationRequest,
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

    private func actualValues(
        for response: BridgePage0MutationResponse,
        matching request: BridgePage0MutationRequest
    ) throws -> (block5: String, block6: String) {
        guard matchingResults(response, request: request),
              let block5 = response.results.first(where: { $0.block == 5 })?.actual,
              let block6 = response.results.first(where: { $0.block == 6 })?.actual else {
            throw AcceptanceError.invalidMutationResponse
        }
        return (block5, block6)
    }

    private func resolve(block5: String, block6: String) throws -> RideRead {
        guard let raw5 = UInt32(block5, radix: 16),
              let raw6 = UInt32(block6, radix: 16) else {
            throw AcceptanceError.invalidMutationResponse
        }
        return RideBlockResolver.resolve(block5: raw5, block6: raw6)
    }

    private func formatBlock(_ word: UInt32) -> String {
        String(format: "%08X", word)
    }

    private func matchingResults(_ response: BridgePage0MutationResponse, request: BridgePage0MutationRequest) -> Bool {
        response.results.count == request.mutations.count &&
        Set(response.results.map(\.block)) == Set(request.mutations.map(\.block)) &&
        response.results.allSatisfy { result in
            request.mutations.contains {
                $0.block == result.block && $0.expected == result.expected && $0.desired == result.desired
            }
        }
    }

    private enum AcceptanceError: Error {
        case invalidScan
        case invalidResetPlan
        case invalidMutationResponse
        case invalidPostReset
        case invalidFinalVerify
    }
}

#endif
