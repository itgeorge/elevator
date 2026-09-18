import SwiftUI

public struct BridgeConnectionLaunchConfiguration: Equatable, Sendable {
    public static let addressOverrideEnvironmentKey = "RIDES_BRIDGE_ADDRESS_OVERRIDE"
    public static let physicalAcceptanceEnvironmentKey = "RIDES_PHASE2_PHYSICAL_ACCEPTANCE"
    public static let slice4PhysicalAcceptanceEnvironmentKey = "RIDES_PHASE4_PHYSICAL_ACCEPTANCE"
    public static let slice5ConceptASmokeEnvironmentKey = "RIDES_SLICE5_CONCEPTA_SMOKE"
    public static let noChipDetectEnvironmentKey = "RIDES_NOCHIP_DETECT"
    public static let unknownDetectEnvironmentKey = "RIDES_UNKNOWN_DETECT"
    public static let bridgeUnavailableDetectEnvironmentKey = "RIDES_BRIDGE_UNAVAILABLE_DETECT"
    public static let simulatorFakeReaderEnvironmentKey = "RIDES_SIMULATOR_FAKE_READER"
    public static let screenshotSceneEnvironmentKey = "RIDES_SCREENSHOT_SCENE"

    public let addressOverride: String?
    public let physicalAcceptanceEnabled: Bool
    public let slice4PhysicalAcceptanceEnabled: Bool
    public let slice5ConceptASmokeEnabled: Bool
    public let noChipDetectEnabled: Bool
    public let unknownDetectEnabled: Bool
    public let bridgeUnavailableDetectEnabled: Bool
    public let simulatorFakeReaderEnabled: Bool
    public let screenshotScene: SimulatorScreenshotScene?

    public init(
        addressOverride: String? = nil,
        physicalAcceptanceEnabled: Bool = false,
        slice4PhysicalAcceptanceEnabled: Bool = false,
        slice5ConceptASmokeEnabled: Bool = false,
        noChipDetectEnabled: Bool = false,
        unknownDetectEnabled: Bool = false,
        bridgeUnavailableDetectEnabled: Bool = false,
        simulatorFakeReaderEnabled: Bool = false,
        screenshotScene: SimulatorScreenshotScene? = nil
    ) {
        self.addressOverride = addressOverride
        self.physicalAcceptanceEnabled = physicalAcceptanceEnabled
        self.slice4PhysicalAcceptanceEnabled = slice4PhysicalAcceptanceEnabled
        self.slice5ConceptASmokeEnabled = slice5ConceptASmokeEnabled
        self.noChipDetectEnabled = noChipDetectEnabled
        self.unknownDetectEnabled = unknownDetectEnabled
        self.bridgeUnavailableDetectEnabled = bridgeUnavailableDetectEnabled
        self.simulatorFakeReaderEnabled = simulatorFakeReaderEnabled
        self.screenshotScene = screenshotScene
    }

    public init(environment: [String: String]) {
        self.init(
            addressOverride: environment[Self.addressOverrideEnvironmentKey],
            physicalAcceptanceEnabled: environment[Self.physicalAcceptanceEnvironmentKey] == "1",
            slice4PhysicalAcceptanceEnabled: environment[Self.slice4PhysicalAcceptanceEnvironmentKey] == "1",
            slice5ConceptASmokeEnabled: environment[Self.slice5ConceptASmokeEnvironmentKey] == "1",
            noChipDetectEnabled: environment[Self.noChipDetectEnvironmentKey] == "1",
            unknownDetectEnabled: environment[Self.unknownDetectEnvironmentKey] == "1",
            bridgeUnavailableDetectEnabled: environment[Self.bridgeUnavailableDetectEnvironmentKey] == "1",
            simulatorFakeReaderEnabled: environment[Self.simulatorFakeReaderEnvironmentKey] == "1",
            screenshotScene: SimulatorScreenshotScene.parse(environment[Self.screenshotSceneEnvironmentKey])
        )
    }

    public init(environmentLookup: @escaping @Sendable (String) -> String?) {
        self.init(
            addressOverride: environmentLookup(Self.addressOverrideEnvironmentKey),
            physicalAcceptanceEnabled: environmentLookup(Self.physicalAcceptanceEnvironmentKey) == "1",
            slice4PhysicalAcceptanceEnabled: environmentLookup(Self.slice4PhysicalAcceptanceEnvironmentKey) == "1",
            slice5ConceptASmokeEnabled: environmentLookup(Self.slice5ConceptASmokeEnvironmentKey) == "1",
            noChipDetectEnabled: environmentLookup(Self.noChipDetectEnvironmentKey) == "1",
            unknownDetectEnabled: environmentLookup(Self.unknownDetectEnvironmentKey) == "1",
            bridgeUnavailableDetectEnabled: environmentLookup(Self.bridgeUnavailableDetectEnvironmentKey) == "1",
            simulatorFakeReaderEnabled: environmentLookup(Self.simulatorFakeReaderEnvironmentKey) == "1",
            screenshotScene: SimulatorScreenshotScene.parse(environmentLookup(Self.screenshotSceneEnvironmentKey))
        )
    }

    public static var processEnvironment: Self {
        Self(environment: ProcessInfo.processInfo.environment)
    }
}

/// Connection / pairing screen retained as an onboarding and diagnostics affordance.
/// Concept A (`ContentView`) is the normal operator root once connected.
public struct BridgeConnectionView: View {
    public static let addressOverrideEnvironmentKey = BridgeConnectionLaunchConfiguration.addressOverrideEnvironmentKey
    public static let physicalAcceptanceEnvironmentKey = BridgeConnectionLaunchConfiguration.physicalAcceptanceEnvironmentKey
    public static let slice4PhysicalAcceptanceEnvironmentKey = BridgeConnectionLaunchConfiguration.slice4PhysicalAcceptanceEnvironmentKey
    public static let slice5ConceptASmokeEnvironmentKey = BridgeConnectionLaunchConfiguration.slice5ConceptASmokeEnvironmentKey
    public static let noChipDetectEnvironmentKey = BridgeConnectionLaunchConfiguration.noChipDetectEnvironmentKey
    public static let unknownDetectEnvironmentKey = BridgeConnectionLaunchConfiguration.unknownDetectEnvironmentKey
    public static let bridgeUnavailableDetectEnvironmentKey = BridgeConnectionLaunchConfiguration.bridgeUnavailableDetectEnvironmentKey
    public static let simulatorFakeReaderEnvironmentKey = BridgeConnectionLaunchConfiguration.simulatorFakeReaderEnvironmentKey
    public static let screenshotSceneEnvironmentKey = BridgeConnectionLaunchConfiguration.screenshotSceneEnvironmentKey

    @StateObject private var model: BridgeConnectionModel
    @State private var pin = ""
    @State private var didApplyLaunchAddressOverride = false
    @State private var didRunLaunchPhysicalAcceptance = false
    @State private var didRunLaunchSlice4PhysicalAcceptance = false
    @State private var isScannerPresented = false
    @State private var scannerImportInFlight = false
    private let launchConfiguration: BridgeConnectionLaunchConfiguration
    private let scannerCoordinator: any BridgePairingScannerCoordinator

    @MainActor
    public init(
        environmentLookup: @escaping @Sendable () -> String? = {
            ProcessInfo.processInfo.environment[BridgeConnectionLaunchConfiguration.addressOverrideEnvironmentKey]
        },
        scannerCoordinator: (any BridgePairingScannerCoordinator)? = nil
    ) {
        _model = StateObject(wrappedValue: BridgeConnectionModel())
        self.scannerCoordinator = scannerCoordinator ?? NativeBridgePairingScannerCoordinator()
        let addressOverride = environmentLookup()
        launchConfiguration = BridgeConnectionLaunchConfiguration(
            environmentLookup: { key in
                if key == BridgeConnectionLaunchConfiguration.addressOverrideEnvironmentKey {
                    return addressOverride
                }
                return ProcessInfo.processInfo.environment[key]
            }
        )
    }

    @MainActor
    public init(
        environmentLookup: @escaping @Sendable (String) -> String?,
        scannerCoordinator: (any BridgePairingScannerCoordinator)? = nil
    ) {
        _model = StateObject(wrappedValue: BridgeConnectionModel())
        self.scannerCoordinator = scannerCoordinator ?? NativeBridgePairingScannerCoordinator()
        launchConfiguration = BridgeConnectionLaunchConfiguration(environmentLookup: environmentLookup)
    }

    @MainActor
    public init(
        model: BridgeConnectionModel,
        launchAddressOverride: String? = nil,
        scannerCoordinator: (any BridgePairingScannerCoordinator)? = nil
    ) {
        _model = StateObject(wrappedValue: model)
        self.scannerCoordinator = scannerCoordinator ?? NativeBridgePairingScannerCoordinator()
        launchConfiguration = BridgeConnectionLaunchConfiguration(addressOverride: launchAddressOverride)
    }

    @MainActor
    public init(
        model: BridgeConnectionModel,
        launchConfiguration: BridgeConnectionLaunchConfiguration,
        scannerCoordinator: (any BridgePairingScannerCoordinator)? = nil
    ) {
        _model = StateObject(wrappedValue: model)
        self.scannerCoordinator = scannerCoordinator ?? NativeBridgePairingScannerCoordinator()
        self.launchConfiguration = launchConfiguration
    }

    public var body: some View {
        NavigationStack {
            Form {
                Section("Local RidesBridge") {
                    TextField("Mac IP or localhost[:port]", text: $model.bridgeURLText)
                        .textInputAutocapitalization(.never)
                        .autocorrectionDisabled()
                        .keyboardType(.URL)

                    Text("Enter the Mac's private IPv4 address or localhost. Port defaults to 5080.")
                        .font(.footnote)
                        .foregroundStyle(.secondary)

                    Button {
                        scannerImportInFlight = false
                        isScannerPresented = true
                    } label: {
                        Label("Scan pairing QR", systemImage: "qrcode.viewfinder")
                    }
                    .disabled(model.isBusy || model.isPaired || scannerImportInFlight)

                    if model.hasSavedCredential && model.isPaired && model.hasEnteredBridgeAddressChange {
                        Button("Use entered address") {
                            Task { await model.useEnteredBridgeAddress() }
                        }
                        .disabled(!model.canUseEnteredBridgeAddress)
                    }

                    SecureField("Six-digit PIN", text: $pin)
                        .textContentType(.oneTimeCode)
                        .keyboardType(.numberPad)

                    HStack {
                        Button("Pair") {
                            Task { await model.pair(pin: pin) }
                        }
                        .buttonStyle(.borderedProminent)
                        .disabled(model.isBusy || model.isPaired || pin.count != 6 || model.bridgeURLText.isEmpty)

                        Button("Forget", role: .destructive) {
                            Task {
                                await model.forget()
                                pin = ""
                            }
                        }
                        .disabled(!model.hasPairingToForget)
                    }
                }

                Section("Bonjour discovery") {
                    HStack {
                        Label(model.bonjourDiscoveryState.title, systemImage: bonjourStatusSymbol)
                        Spacer()
                        if model.isBonjourBrowsing {
                            Button("Stop") {
                                model.stopBonjourBrowse()
                            }
                            .buttonStyle(.bordered)
                        } else {
                            Button("Find bridges") {
                                model.startBonjourBrowse()
                            }
                            .buttonStyle(.bordered)
                        }
                    }

                    if model.bonjourCandidates.isEmpty {
                        Text("Find offers a compatible bridge without pairing or changing the address. Manual IP entry and QR pairing remain available.")
                            .font(.footnote)
                            .foregroundStyle(.secondary)
                    } else {
                        ForEach(model.bonjourCandidates) { candidate in
                            Button {
                                Task { await model.selectBonjourCandidate(candidate) }
                            } label: {
                                VStack(alignment: .leading, spacing: 3) {
                                    Text(candidate.serviceName)
                                    Text(candidate.url.absoluteString)
                                        .font(.footnote.monospaced())
                                        .foregroundStyle(.secondary)
                                }
                            }
                            .disabled(model.isBusy)
                        }
                        if model.bonjourCandidates.count == 1 {
                            Text("One bridge is offered for explicit review; it was not selected automatically.")
                                .font(.footnote)
                                .foregroundStyle(.secondary)
                        } else {
                            Text("Select a bridge explicitly. Multiple addresses or identities are kept separate.")
                                .font(.footnote)
                                .foregroundStyle(.secondary)
                        }
                    }

                    if model.rejectedBonjourResultCount > 0 {
                        Text("Ignored \(model.rejectedBonjourResultCount) incompatible or malformed Bonjour result(s).")
                            .font(.footnote)
                            .foregroundStyle(.orange)
                    }
                }

                Section("Connection") {
                    Label(model.state.title, systemImage: statusSymbol)
                    if let message = model.message {
                        Text(message)
                            .foregroundStyle(.secondary)
                    }
                }

                Section("Developer connectivity") {
                    Text("Detect, Charge, and Reset live in the main Rides screen. This probe only checks authenticated bridge reachability.")
                        .font(.footnote)
                        .foregroundStyle(.secondary)

                    Button {
                        Task { await model.readBlock5() }
                    } label: {
                        Label("Read block 5", systemImage: "arrow.down.circle")
                    }
                    .disabled(model.isBusy || !model.isPaired)

                    if let value = model.lastBlock5Value {
                        LabeledContent("Block 5", value: value)
                            .fontDesign(.monospaced)
                    }
                }
            }
            .navigationTitle("Bridge connection")
        }
        .sheet(isPresented: $isScannerPresented) {
            BridgePairingScannerView(coordinator: scannerCoordinator) { payload in
                handleScannedPairingPayload(payload)
            }
        }
        .onDisappear {
            model.stopBonjourBrowse()
        }
        .task {
            guard !didApplyLaunchAddressOverride else { return }
            didApplyLaunchAddressOverride = true
            await model.applyLaunchAddressOverride(launchConfiguration.addressOverride)
            guard !Task.isCancelled else { return }
            await model.startAutomaticBonjourReconnect()

#if DEBUG
            if launchConfiguration.physicalAcceptanceEnabled {
                guard !didRunLaunchPhysicalAcceptance, !Task.isCancelled else { return }
                didRunLaunchPhysicalAcceptance = true
                await model.runLaunchPhysicalAcceptanceIfRequested()
            }
            if launchConfiguration.slice4PhysicalAcceptanceEnabled {
                guard !didRunLaunchSlice4PhysicalAcceptance, !Task.isCancelled else { return }
                didRunLaunchSlice4PhysicalAcceptance = true
                await model.runLaunchSlice4PhysicalAcceptanceIfRequested()
            }
#endif
        }
    }

    private func handleScannedPairingPayload(_ payload: String) {
        guard !scannerImportInFlight else { return }
        scannerImportInFlight = true
        // The scanner model stopped before invoking this one-shot callback.
        isScannerPresented = false
        Task { @MainActor in
            await model.importPairingPayload(payload)
            scannerImportInFlight = false
        }
    }

    private var statusSymbol: String {
        switch model.state {
        case .connected, .restored: "checkmark.circle.fill"
        case .searching, .pairing, .reading, .readingPage0, .scanningPage0, .settingPage0, .resettingPage0, .relocating: "arrow.triangle.2.circlepath"
        case .failed, .authenticationRequired: "exclamationmark.triangle.fill"
        case .unconfigured: "link.badge.plus"
        }
    }

    private var bonjourStatusSymbol: String {
        switch model.bonjourDiscoveryState {
        case .offered: "checkmark.circle"
        case .reconnecting: "arrow.triangle.2.circlepath"
        case .selectionRequired: "person.2"
        case .browsing: "dot.radiowaves.left.and.right"
        case .denied, .failed: "exclamationmark.triangle"
        case .idle, .stopped: "magnifyingglass"
        }
    }
}

#Preview {
    BridgeConnectionView(model: BridgeConnectionModel(credentialStore: InMemoryBridgeCredentialStore()))
}
