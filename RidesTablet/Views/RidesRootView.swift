import SwiftUI

public enum SimulatorScreenshotScene: String, Equatable, Sendable {
    case known
    case pending
    case history
    case reset
    case unknown
    case noChip = "no-chip"
    case calibrated
    case apartment
    case apartmentHistory = "apartment-history"
    case apartmentCharge = "apartment-charge"
    case apartmentEdit = "apartment-edit"
    case settings

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
    @State private var simulatorPresenceFixtureDevice: SimulatorPresenceSequenceDevice?
#endif
#if DEBUG
    @State private var didStartReadOnlyAutoSmoke = false
    @State private var didFinishLaunchConnectionSetup = false
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
            didFinishLaunchConnectionSetup = true
            if launchConfiguration.simulatorFakeReaderEnabled {
                bootstrapSimulatorFakeReader()
                if let scene = launchConfiguration.screenshotScene, let ridesModel {
                    if ProcessInfo.processInfo.environment["RIDES_SIMULATOR_PRESENCE_SEQUENCE"] != nil {
                        await prepareSimulatorPresenceFixture(ridesModel)
                    }
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
#if DEBUG
        let environment = ProcessInfo.processInfo.environment
        let physicalTest = environment["RIDES_UI_PHYSICAL_BOOTSTRAP"] == "1" || environment["RIDES_UI_PHYSICAL_ACCEPTANCE"] == "1"
        // A test Detect must use the final paired client, not the pre-relocation URL.
        if physicalTest && !didFinishLaunchConnectionSetup { return }
#endif
        if usesFakeReader { return }
        guard connection.isReadyForOperatorWorkflow, let device = connection.makeRideTokenDevice() else {
            if !connection.isPaired {
                ridesModel = nil
            }
            return
        }
        if ridesModel == nil || force {
            let model = RidesViewModel(device: device, configuration: .load())
            ridesModel = model
#if DEBUG
            Task { @MainActor in
                await RidesUIPhysicalAcceptanceCoordinator.bootstrap(model: model)
                if let client = connection.physicalAcceptanceClient() {
                    await RidesUIPhysicalAcceptanceCoordinator.run(model: model, client: client)
                }
            }
#endif
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
            guard model.signalCalibrationReady else {
                receipt["skipped"] = true
                receipt["reason"] = "Signal calibration is not ready; Auto smoke was not started."
                if let directory = FileManager.default.urls(for: .documentDirectory, in: .userDomainMask).first,
                   let data = try? JSONSerialization.data(withJSONObject: receipt, options: [.prettyPrinted, .sortedKeys]) {
                    try? data.write(to: directory.appendingPathComponent("auto-scan-read-only-smoke.json"), options: .atomic)
                }
                return
            }
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
        let fake = FakeProxmark(scanDelay: simulatorScanDelay)
        let presenceFixture = simulatorPresenceSequence.map {
            SimulatorPresenceSequenceDevice(base: fake, sequence: $0)
        }
        simulatorPresenceFixtureDevice = presenceFixture
        let device: any RideTokenDevice = presenceFixture ?? fake
        if launchConfiguration.screenshotScene != nil {
            let fixtureDirectory = FileManager.default.temporaryDirectory.appendingPathComponent("RidesTablet-ScreenshotChargeSessions", isDirectory: true)
            try? FileManager.default.removeItem(at: fixtureDirectory)
            try? FileManager.default.createDirectory(at: fixtureDirectory, withIntermediateDirectories: true)
            ridesModel = RidesViewModel(
                device: device,
                configuration: .load(),
                sessionStore: FileChargeSessionStore(directory: fixtureDirectory)
            )
        } else {
            ridesModel = RidesViewModel(device: device, configuration: .load())
        }
    }

    private var simulatorPresenceSequence: [TokenPresenceOutcome]? {
        guard launchConfiguration.simulatorFakeReaderEnabled,
              let raw = ProcessInfo.processInfo.environment["RIDES_SIMULATOR_PRESENCE_SEQUENCE"] else { return nil }
        let outcomes = raw.split(separator: ",").compactMap { item -> TokenPresenceOutcome? in
            switch item.trimmingCharacters(in: .whitespacesAndNewlines).lowercased() {
            case "present": .present
            case "absent": .absent
            case "failure": .failure("simulated probe failure")
            default: nil
            }
        }
        return outcomes.isEmpty ? nil : outcomes
    }

    private var simulatorScanDelay: TimeInterval {
        let rawValue = ProcessInfo.processInfo.environment["RIDES_SIMULATOR_SCAN_DELAY_MS"]
        guard let milliseconds = rawValue.flatMap(Int.init) else { return 0 }
        return Double(min(max(milliseconds, 0), 5_000)) / 1_000
    }

    private func prepareSimulatorPresenceFixture(_ model: RidesViewModel) async {
        simulatorPresenceFixtureDevice?.setScenario(.noChip)
        await model.performPrimaryAction()
        simulatorPresenceFixtureDevice?.setScenario(.goodToken)
        await model.performPrimaryAction()
        await model.detect()
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
        case .calibrated:
            model.selectSimulation(.noChip)
            await model.performPrimaryAction()
            model.selectSimulation(.goodToken)
            await model.performPrimaryAction()
        case .apartment:
            ApartmentSecretStore.shared.setSecret("rides-demo-secret")
            await model.detect()
            model.adjustRides(by: 50)
            await model.editApartment()
            model.apartmentInput = "52"
            await model.saveApartmentEntry()
        case .apartmentHistory:
            await model.detect()
            for delta in [50, 33, -20] {
                model.adjustRides(by: delta)
                await model.charge()
            }
            ApartmentSecretStore.shared.setSecret("rides-demo-secret")
            await model.editApartment()
            model.apartmentInput = "52"
            await model.saveApartmentEntry()
            model.adjustRides(by: 33)
            await model.charge()
        case .apartmentCharge:
            ApartmentSecretStore.shared.setSecret("rides-demo-secret")
            await model.detect()
            model.adjustRides(by: 50)
            await model.requestCharge()
        case .apartmentEdit:
            ApartmentSecretStore.shared.setSecret("rides-demo-secret")
            await model.detect()
            model.adjustRides(by: 50)
            await model.editApartment()
            model.apartmentInput = "52"
            await model.saveApartmentEntry()
            await model.editApartment()
        case .settings:
            await model.detect()
        }
    }
#endif
}

#if DEBUG
/// Explicitly mocked popup-presence sequence used only by deterministic simulator UI tests.
/// All reads/writes are delegated to FakeProxmark; only token-presence probes are scripted.
private final class SimulatorPresenceSequenceDevice: RideTokenDevice, @unchecked Sendable {
    private let base: FakeProxmark
    private let sequence: [TokenPresenceOutcome]
    private let lock = NSLock()
    private var nextIndex = 0

    init(base: FakeProxmark, sequence: [TokenPresenceOutcome]) {
        self.base = base
        self.sequence = sequence
    }

    func setScenario(_ scenario: SimulationScenario) { base.apply(scenario) }
    func scan() async -> ScanOutcome { await base.scan() }
    func measureTokenPresence() async -> TokenPresenceOutcome {
        let (outcome, shouldDelay) = nextOutcome()
        if shouldDelay { try? await Task.sleep(for: .seconds(5)) }
        return outcome
    }

    private func nextOutcome() -> (TokenPresenceOutcome, Bool) {
        lock.lock()
        defer { lock.unlock() }
        let shouldDelay = nextIndex > 0
        let outcome = sequence[min(nextIndex, sequence.count - 1)]
        nextIndex += 1
        return (outcome, shouldDelay)
    }
    func scanForApartmentSnapshot() async -> ScanOutcome { await base.scanForApartmentSnapshot() }
    func measureCalibrationSignal() async -> CalibrationSignalOutcome { await base.measureCalibrationSignal() }
    func measureSignal() async -> TuneOutcome { await base.measureSignal() }
    func writeRideMirrors(_ request: RideMirrorWriteRequest) async -> WriteOutcome { await base.writeRideMirrors(request) }
    func reset(_ request: ResetMutationRequest) async -> ResetOutcome { await base.reset(request) }
    func writeApartment(_ request: ApartmentMutationRequest) async -> WriteOutcome { await base.writeApartment(request) }
}
#endif
