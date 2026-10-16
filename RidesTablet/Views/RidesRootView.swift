import SwiftUI

public enum SimulatorScreenshotScene: String, Equatable, Sendable {
    case known
    case pending
    case history
    case reset
    case unknown
    case noChip = "no-chip"

    public static func parse(_ rawValue: String?) -> Self? {
        guard let rawValue else { return nil }
        let normalized = rawValue.trimmingCharacters(in: .whitespacesAndNewlines).lowercased()
        if normalized == "nochip" { return .noChip }
        return Self(rawValue: normalized)
    }
}

/// Owns connection state separately from token state and routes the operator
/// into Concept A once an authenticated bridge client is ready.
@MainActor
struct RidesRootView: View {
    @StateObject private var connection: BridgeConnectionModel
    @State private var ridesModel: RidesViewModel?
    @State private var isDiagnosticsPresented = false
    @State private var usesFakeReader = false
#if DEBUG
    @State private var didStartReadOnlyAutoSmoke = false
#endif
    private let launchConfiguration: BridgeConnectionLaunchConfiguration

    init(launchConfiguration: BridgeConnectionLaunchConfiguration = .processEnvironment) {
        let connection = BridgeConnectionModel()
        _connection = StateObject(wrappedValue: connection)
        self.launchConfiguration = launchConfiguration
    }

    init(
        connection: BridgeConnectionModel,
        launchConfiguration: BridgeConnectionLaunchConfiguration = .processEnvironment
    ) {
        _connection = StateObject(wrappedValue: connection)
        self.launchConfiguration = launchConfiguration
    }

    var body: some View {
        Group {
            if let ridesModel {
                ContentView(model: ridesModel) {
                    isDiagnosticsPresented = true
                }
            } else {
                connectionGate
            }
        }
        .task {
            connection.restore()
            await connection.applyLaunchAddressOverride(launchConfiguration.addressOverride)
            await connection.startAutomaticBonjourReconnect()
#if DEBUG
            if launchConfiguration.simulatorFakeReaderEnabled {
                bootstrapSimulatorFakeReader()
                if let scene = launchConfiguration.screenshotScene, let ridesModel {
                    await applySimulatorScreenshotScene(scene, to: ridesModel)
                }
            } else {
                refreshOperatorWorkflow()
            }
#else
            refreshOperatorWorkflow()
#endif
#if DEBUG
            if launchConfiguration.physicalAcceptanceEnabled {
                await connection.runLaunchPhysicalAcceptanceIfRequested()
            }
            if launchConfiguration.slice4PhysicalAcceptanceEnabled {
                await connection.runLaunchSlice4PhysicalAcceptanceIfRequested()
            }
            if launchConfiguration.slice5ConceptASmokeEnabled {
                await connection.runLaunchSlice5ConceptAPhysicalSmokeIfRequested()
            }
            if launchConfiguration.noChipDetectEnabled {
                await connection.runLaunchNoChipDetectIfRequested()
            }
            if launchConfiguration.unknownDetectEnabled {
                await connection.runLaunchUnknownDetectIfRequested()
            }
            if launchConfiguration.bridgeUnavailableDetectEnabled {
                await connection.runLaunchBridgeUnavailableDetectIfRequested(
                    overrideAddress: launchConfiguration.addressOverride
                )
            }
#endif
        }
        .onChange(of: connection.state) { _, _ in
            refreshOperatorWorkflow()
#if DEBUG
            startReadOnlyAutoSmokeIfRequested()
#endif
        }
        .onChange(of: connection.isPaired) { _, _ in
            refreshOperatorWorkflow()
        }
        .sheet(isPresented: $isDiagnosticsPresented) {
            BridgeConnectionView(model: connection, launchConfiguration: launchConfiguration)
                .overlay(alignment: .topTrailing) {
                    Button("Done") { isDiagnosticsPresented = false }
                        .font(.body.weight(.semibold))
                        .padding(16)
                }
        }
    }

    private var connectionGate: some View {
        NavigationStack {
            VStack(spacing: 28) {
                VStack(spacing: 10) {
                    Image(systemName: "antenna.radiowaves.left.and.right")
                        .font(.system(size: 44, weight: .semibold))
                        .foregroundStyle(.tint)
                    Text("Connect Mac bridge")
                        .font(.largeTitle.bold())
                    Text("Pair once with the Mac running RidesBridge, then Detect / Charge / Reset stay on this iPad.")
                        .font(.title3)
                        .foregroundStyle(.secondary)
                        .multilineTextAlignment(.center)
                        .frame(maxWidth: 560)
                }

                VStack(spacing: 14) {
                    Text(connection.state.title)
                        .font(.headline)
                    if let message = connection.message {
                        Text(message)
                            .font(.body)
                            .foregroundStyle(.secondary)
                            .multilineTextAlignment(.center)
                    }

                    Button {
                        isDiagnosticsPresented = true
                    } label: {
                        Label(
                            connection.isPaired ? "Finish connection setup" : "Pair or reconnect",
                            systemImage: "qrcode.viewfinder"
                        )
                        .font(.title2.bold())
                        .frame(maxWidth: .infinity, minHeight: 64)
                    }
                    .buttonStyle(.borderedProminent)
                    .disabled(connection.isBusy)

                    if connection.isReadyForOperatorWorkflow {
                        Button("Continue to rides") {
                            refreshOperatorWorkflow(force: true)
                        }
                        .font(.title3.weight(.semibold))
                        .frame(maxWidth: .infinity, minHeight: 52)
                        .buttonStyle(.bordered)
                    }

#if DEBUG
                    Button("Use simulator fake reader") {
                        usesFakeReader = true
                        ridesModel = RidesViewModel(device: FakeProxmark(), configuration: .load())
                    }
                    .font(.body.weight(.semibold))
#endif
                }
                .frame(maxWidth: 520)
            }
            .padding(36)
            .frame(maxWidth: .infinity, maxHeight: .infinity)
            .background(Color(uiColor: .systemGroupedBackground))
            .navigationTitle("Rides")
            .navigationBarTitleDisplayMode(.inline)
        }
    }

    private func refreshOperatorWorkflow(force: Bool = false) {
        if usesFakeReader { return }
        guard connection.isReadyForOperatorWorkflow, let device = connection.makeRideTokenDevice() else {
            if !connection.isPaired {
                ridesModel = nil
            }
            return
        }
        if ridesModel == nil || force {
            ridesModel = RidesViewModel(device: device, configuration: .load())
        }
    }

#if DEBUG
    /// Opt-in device smoke. This path never calls charge/reset or any write API.
    private func startReadOnlyAutoSmokeIfRequested() {
        guard ProcessInfo.processInfo.environment["RIDES_AUTO_SCAN_READ_ONLY_SMOKE"] == "1",
              !didStartReadOnlyAutoSmoke, !usesFakeReader,
              connection.isReadyForOperatorWorkflow, let model = ridesModel else { return }
        didStartReadOnlyAutoSmoke = true
        Task { @MainActor in
            let started = Date()
            var receipt: [String: Any] = [
                "mode": "physical iPad model + live bridge/PM3; no UI automation",
                "startedAt": ISO8601DateFormatter().string(from: started),
                "hardwareWritesPerformed": false
            ]
            defer { model.stopAutoScanning() }
            model.toggleAutoScanning()
            let deadline = Date().addingTimeInterval(30)
            while model.loadedToken == nil && Date() < deadline {
                try? await Task.sleep(for: .milliseconds(100))
            }
            if model.loadedToken != nil {
                receipt["initialRides"] = model.currentRides
                model.adjustRides(by: model.currentRides < model.configuration.maxRides ? 1 : -1)
                let pending = model.pendingRides
                receipt["pendingAfterAdjustment"] = pending
                try? await Task.sleep(for: .seconds(12))
                receipt["autoStayedEnabled"] = model.isAutoScanning
                receipt["pendingAfterPolling"] = model.pendingRides
                receipt["pendingPreserved"] = model.pendingRides == pending
                if let signal = model.lastSignalMillivolts { receipt["signalMillivolts"] = signal }
                await model.detect()
                receipt["manualDetectStoppedAuto"] = !model.isAutoScanning
                receipt["manualCurrentRides"] = model.currentRides
                receipt["manualPendingRides"] = model.pendingRides
                receipt["passed"] = receipt["pendingPreserved"] as? Bool == true
                    && receipt["autoStayedEnabled"] as? Bool == true
                    && !model.isAutoScanning && model.loadedToken != nil
            } else {
                receipt["passed"] = false
                receipt["error"] = "No known token was confirmed within 30 seconds."
                receipt["status"] = model.autoScanStatus
            }
            receipt["elapsedSeconds"] = Date().timeIntervalSince(started)
            if let directory = FileManager.default.urls(for: .documentDirectory, in: .userDomainMask).first,
               let data = try? JSONSerialization.data(withJSONObject: receipt, options: [.prettyPrinted, .sortedKeys]) {
                try? data.write(to: directory.appendingPathComponent("auto-scan-read-only-smoke.json"), options: .atomic)
            }
        }
    }

    private func bootstrapSimulatorFakeReader() {
        usesFakeReader = true
        if launchConfiguration.screenshotScene != nil {
            let suite = "RidesTablet.ScreenshotHistory"
            let defaults = UserDefaults(suiteName: suite)!
            defaults.removePersistentDomain(forName: suite)
            ridesModel = RidesViewModel(device: FakeProxmark(scanDelay: simulatorScanDelay), configuration: .load(),
                                       chargeHistoryStore: UserDefaultsChargeHistoryStore(defaults: defaults))
        } else {
            ridesModel = RidesViewModel(device: FakeProxmark(scanDelay: simulatorScanDelay), configuration: .load())
        }
    }

    private var simulatorScanDelay: TimeInterval {
        let rawValue = ProcessInfo.processInfo.environment["RIDES_SIMULATOR_SCAN_DELAY_MS"]
        guard let milliseconds = rawValue.flatMap(Int.init) else { return 0 }
        return Double(min(max(milliseconds, 0), 5_000)) / 1_000
    }

    private func applySimulatorScreenshotScene(_ scene: SimulatorScreenshotScene, to model: RidesViewModel) async {
        switch scene {
        case .known:
            await model.detect()
        case .pending:
            await model.detect()
            model.adjustRides(by: 150)
        case .history:
            // Fixture-only completed charges; the real reader is never used here.
            model.selectSimulation(.noChip)
            await model.performPrimaryAction()
            model.selectSimulation(.goodToken)
            await model.performPrimaryAction()
            await model.detect()
            for delta in [50, 33, -20] {
                model.adjustRides(by: delta)
                await model.charge()
            }
            model.adjustRides(by: 33)
        case .reset:
            await model.detect()
            model.openReset()
        case .unknown:
            model.selectSimulation(.unknownFamily)
            await model.detect()
        case .noChip:
            model.selectSimulation(.noChip)
            await model.detect()
        }
    }
#endif
}
