#if DEBUG

import Foundation

public struct ConceptAPhysicalSmokeSummary: Equatable, Sendable {
    public let sequence: RideSequence
    public let detectedRides: UInt
    public let chargedRides: UInt
    public let resetRides: UInt

    public var conciseDescription: String {
        "Slice 5 Concept A smoke passed (sequence=\(sequence.rawValue), detected=\(detectedRides), charged=\(chargedRides), reset=\(resetRides))."
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

/// Exercises Concept A through `RidesViewModel` + `RideTokenDevice` on any known registered token.
@MainActor
public final class ConceptAPhysicalSmokeCoordinator {
    public static let detectStage = "detect"
    public static let chargeStage = "charge"
    public static let alreadyAppliedStage = "already-applied"
    public static let conflictStage = "conflict"
    public static let resetCancelStage = "reset-cancel"
    public static let resetStage = "reset"
    public static let restoreStage = "restore"

    /// Optional `--fake-pm3` Venus seed values for documentation only; smoke does not assert them.
    public static let fakePm3Block4 = "D6D1C733"
    public static let fakePm3Block5 = "BBC7FD03"
    public static let fakePm3Block6 = "BBC7FD03"
    public static let fakePm3SignalMillivolts = 420
    public static let fakePm3Rides: UInt = 180
    public static let fakePm3Sequence = RideSequence.venus

    private struct TokenSnapshot: Equatable {
        let sequence: RideSequence
        let rides: UInt
        let block4Text: String
        let block5: UInt32
        let block6: UInt32
        let signalMillivolts: Int?
    }

    private let model: RidesViewModel
    private let device: any RideTokenDevice
    private let log: (String) -> Void

    public init(
        device: any RideTokenDevice,
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
              let token = model.loadedToken,
              let signal = model.lastSignalMillivolts else {
            return failure(stage: Self.detectStage, detail: "state=\(model.state.title) rides=\(model.currentRides) signal=\(String(describing: model.lastSignalMillivolts))")
        }
        let snapshot = TokenSnapshot(
            sequence: token.sequence,
            rides: model.currentRides,
            block4Text: model.aptBlock4Text,
            block5: token.block5,
            block6: token.block6,
            signalMillivolts: signal
        )
        log("RIDES_SLICE5_SMOKE_DETECT sequence=\(snapshot.sequence.rawValue) rides=\(snapshot.rides) block4=\(snapshot.block4Text) signal=\(signal)")

        let chargeDelta = snapshot.rides >= 10 ? -10 : 10
        let chargedRides = adjustedRides(snapshot.rides, by: chargeDelta)
        model.adjustRides(by: chargeDelta)
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
        let staleToken = Token(blocks: staleBlocks, rideCount: chargedRides, sequence: snapshot.sequence)
        let conflictRides = conflictTarget(from: chargedRides)
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

        model.selectedResetSequence = snapshot.sequence
        guard model.canConfirmReset else {
            return failure(stage: Self.resetStage, detail: "cannot confirm \(snapshot.sequence.rawValue) reset")
        }
        await model.confirmReset()
        guard case .known = model.state,
              model.currentRides == 0,
              model.pendingRides == 0,
              model.message == "Reset successful.",
              !model.isResetSheetPresented else {
            return failure(stage: Self.resetStage, detail: model.message)
        }
        log("RIDES_SLICE5_SMOKE_RESET rides=0 sequence=\(snapshot.sequence.rawValue)")

        model.adjustRides(by: Int(snapshot.rides))
        guard model.canCharge else {
            return failure(stage: Self.restoreStage, detail: "cannot charge restore")
        }
        await model.charge()
        guard case .known = model.state,
              model.currentRides == snapshot.rides,
              model.pendingRides == snapshot.rides,
              model.loadedToken?.sequence == snapshot.sequence else {
            return failure(stage: Self.restoreStage, detail: model.message)
        }
        log("RIDES_SLICE5_SMOKE_RESTORE rides=\(snapshot.rides) sequence=\(snapshot.sequence.rawValue)")

        return .success(ConceptAPhysicalSmokeSummary(
            sequence: snapshot.sequence,
            detectedRides: snapshot.rides,
            chargedRides: chargedRides,
            resetRides: 0
        ))
    }

    private func adjustedRides(_ rides: UInt, by delta: Int) -> UInt {
        let maxRides = Int(model.configuration.maxRides)
        return UInt(max(0, min(maxRides, Int(rides) + delta)))
    }

    private func conflictTarget(from chargedRides: UInt) -> UInt {
        chargedRides >= 10 ? chargedRides - 10 : min(chargedRides + 10, model.configuration.maxRides)
    }

    private func failure(stage: String, detail: String?) -> ConceptAPhysicalSmokeResult {
        var line = "RIDES_SLICE5_SMOKE_FAILURE stage=\(stage)"
        if let detail, !detail.isEmpty { line += " detail=\(detail)" }
        log(line)
        return .failure(ConceptAPhysicalSmokeFailure(stage: stage, detail: detail))
    }
}

#endif
