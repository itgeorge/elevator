#if DEBUG
import Foundation
import SwiftUI
import UIKit

/// Opt-in, destructive acceptance flow for a real paired PM3. Never invoked by a normal launch.
@MainActor
public enum RidesUIPhysicalAcceptanceCoordinator {
    public static let bootstrapEnvironmentKey = "RIDES_UI_PHYSICAL_BOOTSTRAP"
    public static let runEnvironmentKey = "RIDES_UI_PHYSICAL_ACCEPTANCE"
    public static let testSecret = "rides-ui-e2e-test-2026"
    private static var bootstrapped = false
    private static var ran = false

    /// Called by the app root after the normal bridge pairing/device construction path.
    /// This bypasses calibration by doing exactly one real Detect; it fabricates no samples.
    public static func bootstrap(model: RidesViewModel) async {
        guard ProcessInfo.processInfo.environment[bootstrapEnvironmentKey] == "1", !bootstrapped else { return }
        bootstrapped = true
        await model.detect()
        if let directory = FileManager.default.urls(for: .documentDirectory, in: .userDomainMask).first {
            let receipt: [String: Any] = ["mode": "physical debug bootstrap; real Detect without fabricated calibration", "state": model.state.title, "message": model.message ?? "", "loadedKnownToken": model.loadedToken != nil]
            if let data = try? JSONSerialization.data(withJSONObject: receipt, options: [.prettyPrinted, .sortedKeys]) {
                try? data.write(to: directory.appendingPathComponent("rides-ui-physical-bootstrap.json"), options: .atomic)
            }
        }
        await checkpoint("bootstrap")
    }

    /// Run once per app process, only when explicitly enabled in a DEBUG build.
    public static func run(model: RidesViewModel, client: BridgeClient) async {
        guard ProcessInfo.processInfo.environment[runEnvironmentKey] == "1", !ran else { return }
        ran = true
        var receipt: [String: Any] = [
            "version": 1,
            "mode": "physical iPad model + live bridge/PM3, programmatic actions; not UI tap automation",
            "startedAt": ISO8601DateFormatter().string(from: Date()),
            "calibration": "bypassed only by direct real model.detect(); Auto/calibration not tested",
            "testSecretConfigured": false,
            "stages": [String: Any]()
        ]
        var stages: [String: Any] = [:]
        func record(_ name: String, _ passed: Bool, _ detail: String? = nil) {
            var stage: [String: Any] = ["passed": passed]
            if let detail { stage["detail"] = detail }
            stages[name] = stage
        }
        do {
            await checkpoint("initial")
            guard case .known = model.state, let initialToken = model.loadedToken else {
                throw AcceptanceFailure("Initial real Detect did not load a known token; state=\(model.state.title)")
            }
            let initialRides = model.currentRides
            let initialHistoryCount = model.completedCharges.count
            guard initialRides <= model.configuration.maxRides else { throw AcceptanceFailure("Detected rides outside configured range") }
            let originalBlocks = try await client.readPage0Blocks1To6().blocks
            receipt["originalBlocks1To6"] = Dictionary(uniqueKeysWithValues: originalBlocks.map { (String($0.block), $0.value) })
            record("initial-detect", true, "rides=\(initialRides), sequence=\(initialToken.sequence.rawValue)")

            // This repeatable secret is installed only inside this explicit DEBUG acceptance run.
            let secretStore = ApartmentSecretStore.shared
            secretStore.setSecret(testSecret)
            receipt["testSecretConfigured"] = true
            await Task.yield()
            await waitForModel(model)
            model.adjustRides(by: initialRides < model.configuration.maxRides ? 1 : -1)
            guard model.pendingRides != model.currentRides else { throw AcceptanceFailure("Could not stage clear-pending check") }
            model.clearPendingRides()
            guard model.pendingRides == model.currentRides, model.completedCharges.count == initialHistoryCount else {
                throw AcceptanceFailure("Clear pending changed completed-charge history or failed to restore rides")
            }
            record("clear-pending-no-charge", true)
            if model.apartmentNumber != nil {
                // Guarded block-4-only mutation establishes the missing-apartment prompt state.
                let block4 = originalBlocks.first(where: { $0.block == 4 })!.value
                let mutation = try BridgePage0Mutation(block: 4, expected: block4, desired: "00000000")
                _ = try await client.mutatePage0(BridgePage0MutationRequest(mutations: [mutation]))
                await model.detect()
            }
            guard model.apartmentNumber == nil, let missingApartmentToken = model.loadedToken else { throw AcceptanceFailure("Could not establish missing-apartment test state") }
            await checkpoint("apartment-visible")
            record("apartment-visible", true, "test secret configured; missing-apartment prompt expected")

            // Cancel must be a no-write; Skip changes rides only; Save persists apartment + rides.
            let rideDelta = initialRides < model.configuration.maxRides ? 1 : -1
            model.adjustRides(by: rideDelta)
            guard model.canCharge else { throw AcceptanceFailure("Could not stage a ride change") }
            await model.requestCharge()
            guard model.isApartmentSheetPresented else { throw AcceptanceFailure("Expected apartment prompt for charge") }
            await checkpoint("apartment-prompt")
            await model.cancelApartmentEntry()
            guard model.currentRides == initialRides, model.completedCharges.count == initialHistoryCount else { throw AcceptanceFailure("Cancel changed token/session") }
            record("apartment-cancel", true)
            await model.requestCharge()
            guard model.isApartmentSheetPresented else { throw AcceptanceFailure("Apartment prompt missing on Skip path") }
            await model.skipApartmentEntry()
            let afterFirstCharge = UInt(Int(initialRides) + rideDelta)
            guard model.currentRides == afterFirstCharge, model.completedCharges.count == initialHistoryCount + 1,
                  let afterSkip = model.loadedToken, afterSkip.block4 == missingApartmentToken.block4 else {
                throw AcceptanceFailure("Skip did not charge rides only")
            }
            record("apartment-skip", true)

            model.adjustRides(by: rideDelta)
            await model.requestCharge()
            guard model.isApartmentSheetPresented else { throw AcceptanceFailure("Save path did not prompt") }
            model.apartmentInput = "42"
            await model.saveApartmentEntry()
            let afterSecondCharge = UInt(Int(initialRides) + rideDelta * 2)
            guard model.currentRides == afterSecondCharge, model.apartmentNumber == 42,
                  model.completedCharges.count == initialHistoryCount + 2 else { throw AcceptanceFailure("Save did not charge and save apartment") }
            record("apartment-save-charge", true)

            // Edit the now-existing apartment with pending rides; only block 4 changes.
            model.adjustRides(by: rideDelta)
            await model.editApartment()
            model.apartmentInput = "43"
            await checkpoint("apartment-edit-prompt")
            await model.saveApartmentEntry()
            let afterThirdPending = UInt(Int(initialRides) + rideDelta * 3)
            guard model.apartmentNumber == 43, model.pendingRides == afterThirdPending,
                  model.currentRides == afterSecondCharge, model.completedCharges.count == initialHistoryCount + 2 else {
                throw AcceptanceFailure("Apartment-only edit changed ride/session state")
            }
            record("apartment-edit-preserves-pending", true)
            await checkpoint("apartment-edit")

            guard model.completedCharges.count == initialHistoryCount + 2,
                  let oldSession = model.activeChargeSession else { throw AcceptanceFailure("Expected two new completed charges before session switch") }
            model.startNewChargeSession()
            guard model.activeChargeSession?.id != oldSession.id, model.completedCharges.isEmpty else { throw AcceptanceFailure("New session failed") }
            model.loadChargeSession(oldSession)
            guard model.activeChargeSession?.id == oldSession.id,
                  model.completedCharges.count == initialHistoryCount + 2 else { throw AcceptanceFailure("Load session failed") }
            model.adjustRides(by: rideDelta)
            await model.charge()
            guard model.completedCharges.count == initialHistoryCount + 3 else { throw AcceptanceFailure("Loaded session did not accept charge") }
            record("session-new-load-restart", true, "charges=\(model.completedCharges.count)")

            for profile: RideSequence in [.venus, .mercury] {
                model.openReset()
                guard !model.canConfirmReset else { throw AcceptanceFailure("Reset must require profile selection") }
                model.isResetSheetPresented = false
                model.selectedResetSequence = profile
                await model.confirmReset()
                guard model.currentRides == 0, model.apartmentNumber == 43,
                      model.loadedToken?.sequence == profile else { throw AcceptanceFailure("Reset to \(profile.rawValue) failed or did not preserve apt") }
                let verifiedBlocks = try await client.readPage0Blocks1To6().blocks
                guard let block3Text = verifiedBlocks.first(where: { $0.block == 3 })?.value,
                      let block4Text = verifiedBlocks.first(where: { $0.block == 4 })?.value,
                      let block3 = UInt32(block3Text, radix: 16), let block4 = UInt32(block4Text, radix: 16),
                      ApartmentBlockCodec.decode(secret: testSecret, block3: block3, block4: block4)?.apartment == 43 else {
                    throw AcceptanceFailure("Live blocks 3/4 did not verify apartment 43 after \(profile.rawValue) reset")
                }
                record("reset-\(profile.rawValue)", true, "rides=0, live blocks3/4 verify apartment=43")
            }
            guard let finalSessionID = model.activeChargeSession?.id else { throw AcceptanceFailure("Active session missing") }
            let reopened = try FileChargeSessionStore().loadActiveSession()
            guard reopened.id == finalSessionID, reopened.charges.count == initialHistoryCount + 3 else { throw AcceptanceFailure("Active session did not survive reopen") }
            guard model.apartmentNumber == 43, model.currentRides == 0, model.completedCharges.count == initialHistoryCount + 3 else {
                throw AcceptanceFailure("Final UI model state does not match expected state")
            }
            record("final-state", true, "rides=0, apartment=43, charges=\(model.completedCharges.count)")
            await checkpoint("final")
            receipt["passed"] = true
        } catch {
            let message = (error as? AcceptanceFailure)?.message ?? error.localizedDescription
            receipt["passed"] = false
            receipt["error"] = message
            stages["failure"] = ["passed": false, "detail": message]
        }
        receipt["stages"] = stages
        receipt["finishedAt"] = ISO8601DateFormatter().string(from: Date())
        writeReceipt(receipt)
    }

    private struct AcceptanceFailure: Error { let message: String; init(_ message: String) { self.message = message } }
    private static func waitForModel(_ model: RidesViewModel) async {
        for _ in 0..<60 where model.isBusy { try? await Task.sleep(nanoseconds: 250_000_000) }
    }
    private static func checkpoint(_ name: String) async {
        try? await Task.sleep(nanoseconds: 500_000_000)
        guard let window = UIApplication.shared.connectedScenes
            .compactMap({ $0 as? UIWindowScene }).flatMap(\.windows).first(where: \.isKeyWindow) else { return }
        let renderer = UIGraphicsImageRenderer(bounds: window.bounds)
        let image = renderer.image { _ in _ = window.drawHierarchy(in: window.bounds, afterScreenUpdates: true) }
        guard let data = image.pngData(), let dir = FileManager.default.urls(for: .documentDirectory, in: .userDomainMask).first else { return }
        try? data.write(to: dir.appendingPathComponent("rides-ui-physical-\(name).png"), options: .atomic)
    }
    private static func writeReceipt(_ receipt: [String: Any]) {
        guard let dir = FileManager.default.urls(for: .documentDirectory, in: .userDomainMask).first,
              let data = try? JSONSerialization.data(withJSONObject: receipt, options: [.prettyPrinted, .sortedKeys]) else { return }
        try? data.write(to: dir.appendingPathComponent("rides-ui-physical-acceptance.json"), options: .atomic)
    }
}
#endif
