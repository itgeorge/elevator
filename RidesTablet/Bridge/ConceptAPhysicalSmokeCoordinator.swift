#if DEBUG

import Foundation

public struct ConceptAPhysicalSmokeSummary: Equatable, Sendable {
    public let detectedRides: UInt
    public let chargedRides: UInt
    public let resetRides: UInt

    public var conciseDescription: String {
        "Slice 5 Concept A smoke passed (detected=\(detectedRides), charged=\(chargedRides), reset=\(resetRides))."
    }
}

public struct ConceptAPhysicalSmokeFailure: Equatable, Sendable {
    public let stage: String
    public let detail: String?
}

public enum ConceptAPhysicalSmokeResult: Equatable, Sendable {
    case success(ConceptAPhysicalSmokeSummary)
    case failure(ConceptAPhysicalSmokeFailure)
}

/// Exercises Concept A through `RidesViewModel` + `NetworkRideTokenDevice` against `--fake-pm3`.
@MainActor
public final class ConceptAPhysicalSmokeCoordinator {
    public static let detectStage = "detect"
    public static let chargeStage = "charge"
    public static let alreadyAppliedStage = "already-applied"
    public static let conflictStage = "conflict"
    public static let resetCancelStage = "reset-cancel"
    public static let resetStage = "reset"
    public static let restoreStage = "restore"

    public static let expectedBlock4 = "D6D1C733"
    public static let expectedBlock5 = "BBC7FD03"
    public static let expectedBlock6 = "BBC7FD03"
    public static let expectedSignalMillivolts = 420
    public static let expectedRides: UInt = 180
    public static let expectedSequence = RideSequence.venus

    private let model: RidesViewModel
    private let device: NetworkRideTokenDevice
    private let log: (String) -> Void

    public init(
        device: NetworkRideTokenDevice,
        configuration: RidesConfiguration = .load(),
        log: @escaping (String) -> Void = { print($0) }
    ) {
        self.device = device
        self.model = RidesViewModel(device: device, configuration: configuration)
        self.log = log
    }

    public func run() async -> ConceptAPhysicalSmokeResult {
        await model.detect()
        guard case .known = model.state,
              model.currentRides == Self.expectedRides,
              model.pendingRides == Self.expectedRides,
              model.aptBlock4Text == Self.expectedBlock4,
              model.lastSignalMillivolts == Self.expectedSignalMillivolts,
              model.loadedToken?.sequence == Self.expectedSequence else {
            return failure(stage: Self.detectStage, detail: "state=\(model.state.title) rides=\(model.currentRides) signal=\(String(describing: model.lastSignalMillivolts))")
        }
        log("RIDES_SLICE5_SMOKE_DETECT rides=\(Self.expectedRides) block4=\(Self.expectedBlock4) signal=\(Self.expectedSignalMillivolts)")

        let chargedRides: UInt = 170
        model.adjustRides(by: -10)
        guard model.canCharge else {
            return failure(stage: Self.chargeStage, detail: "canCharge=false after adjust")
        }
        await model.charge()
        guard case .known = model.state,
              model.currentRides == chargedRides,
              model.pendingRides == chargedRides,
              model.message == "Charge successful." else {
            return failure(stage: Self.chargeStage, detail: model.message)
        }
        log("RIDES_SLICE5_SMOKE_CHARGE rides=\(chargedRides)")

        guard let chargedToken = model.loadedToken else {
            return failure(stage: Self.alreadyAppliedStage, detail: "missing loaded token")
        }
        switch await device.writeRideMirrors(RideMirrorWriteRequest(token: chargedToken, desiredRides: chargedRides)) {
        case .success:
            break
        case .failure(let error):
            return failure(stage: Self.alreadyAppliedStage, detail: error)
        case .requiresRefresh(let error):
            return failure(stage: Self.alreadyAppliedStage, detail: error)
        }
        log("RIDES_SLICE5_SMOKE_ALREADY_APPLIED rides=\(chargedRides)")

        var staleBlocks = chargedToken.blocks
        staleBlocks[5] = 0x11111111
        staleBlocks[6] = 0x22222222
        let staleToken = Token(blocks: staleBlocks, rideCount: chargedRides, sequence: .venus)
        let conflictRides: UInt = 160
        switch await device.writeRideMirrors(RideMirrorWriteRequest(token: staleToken, desiredRides: conflictRides)) {
        case .requiresRefresh:
            break
        case .success:
            return failure(stage: Self.conflictStage, detail: "stale expected unexpectedly succeeded")
        case .failure(let error):
            return failure(stage: Self.conflictStage, detail: error)
        }
        log("RIDES_SLICE5_SMOKE_CONFLICT stale-expected rejected")

        await model.detect()
        guard case .known = model.state, model.currentRides == chargedRides else {
            return failure(stage: Self.detectStage, detail: "refresh after conflict failed")
        }

        model.openReset()
        guard model.isResetSheetPresented, !model.canConfirmReset else {
            return failure(stage: Self.resetCancelStage, detail: "reset sheet open without selection")
        }
        model.selectedResetSequence = nil
        model.isResetSheetPresented = false
        log("RIDES_SLICE5_SMOKE_RESET_CANCEL selection-required")

        model.selectedResetSequence = .venus
        guard model.canConfirmReset else {
            return failure(stage: Self.resetStage, detail: "cannot confirm Venus reset")
        }
        await model.confirmReset()
        guard case .known = model.state,
              model.currentRides == 0,
              model.pendingRides == 0,
              model.message == "Reset successful.",
              !model.isResetSheetPresented else {
            return failure(stage: Self.resetStage, detail: model.message)
        }
        log("RIDES_SLICE5_SMOKE_RESET rides=0 sequence=venus")

        model.adjustRides(by: Int(Self.expectedRides))
        guard model.canCharge else {
            return failure(stage: Self.restoreStage, detail: "cannot charge restore")
        }
        await model.charge()
        guard case .known = model.state,
              model.currentRides == Self.expectedRides,
              model.pendingRides == Self.expectedRides else {
            return failure(stage: Self.restoreStage, detail: model.message)
        }
        log("RIDES_SLICE5_SMOKE_RESTORE rides=\(Self.expectedRides)")

        return .success(ConceptAPhysicalSmokeSummary(
            detectedRides: Self.expectedRides,
            chargedRides: chargedRides,
            resetRides: 0
        ))
    }

    private func failure(stage: String, detail: String?) -> ConceptAPhysicalSmokeResult {
        var line = "RIDES_SLICE5_SMOKE_FAILURE stage=\(stage)"
        if let detail, !detail.isEmpty { line += " detail=\(detail)" }
        log(line)
        return .failure(ConceptAPhysicalSmokeFailure(stage: stage, detail: detail))
    }
}

#endif
