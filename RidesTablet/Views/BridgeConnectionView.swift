import SwiftUI

public struct BridgeConnectionLaunchConfiguration: Equatable, Sendable {
    public static let addressOverrideEnvironmentKey = "RIDES_BRIDGE_ADDRESS_OVERRIDE"
    public static let physicalAcceptanceEnvironmentKey = "RIDES_PHASE2_PHYSICAL_ACCEPTANCE"
    public static let slice4PhysicalAcceptanceEnvironmentKey = "RIDES_PHASE4_PHYSICAL_ACCEPTANCE"

    public let addressOverride: String?
    public let physicalAcceptanceEnabled: Bool
    public let slice4PhysicalAcceptanceEnabled: Bool

    public init(
        addressOverride: String? = nil,
        physicalAcceptanceEnabled: Bool = false,
        slice4PhysicalAcceptanceEnabled: Bool = false
    ) {
        self.addressOverride = addressOverride
        self.physicalAcceptanceEnabled = physicalAcceptanceEnabled
        self.slice4PhysicalAcceptanceEnabled = slice4PhysicalAcceptanceEnabled
    }

    public init(environment: [String: String]) {
        self.init(
            addressOverride: environment[Self.addressOverrideEnvironmentKey],
            physicalAcceptanceEnabled: environment[Self.physicalAcceptanceEnvironmentKey] == "1",
            slice4PhysicalAcceptanceEnabled: environment[Self.slice4PhysicalAcceptanceEnvironmentKey] == "1"
        )
    }

    public init(environmentLookup: @escaping @Sendable (String) -> String?) {
        self.init(
            addressOverride: environmentLookup(Self.addressOverrideEnvironmentKey),
            physicalAcceptanceEnabled: environmentLookup(Self.physicalAcceptanceEnvironmentKey) == "1",
            slice4PhysicalAcceptanceEnabled: environmentLookup(Self.slice4PhysicalAcceptanceEnvironmentKey) == "1"
        )
    }

    public static var processEnvironment: Self {
        Self(environment: ProcessInfo.processInfo.environment)
    }
}

/// Temporary Slice 3 page0 diagnostic screen. Concept A remains in ContentView for later integration.
public struct BridgeConnectionView: View {
    public static let addressOverrideEnvironmentKey = BridgeConnectionLaunchConfiguration.addressOverrideEnvironmentKey
    public static let physicalAcceptanceEnvironmentKey = BridgeConnectionLaunchConfiguration.physicalAcceptanceEnvironmentKey
    public static let slice4PhysicalAcceptanceEnvironmentKey = BridgeConnectionLaunchConfiguration.slice4PhysicalAcceptanceEnvironmentKey

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
        launchConfiguration = BridgeConnectionLaunchConfiguration(
            addressOverride: environmentLookup(),
            physicalAcceptanceEnabled: ProcessInfo.processInfo.environment[BridgeConnectionLaunchConfiguration.physicalAcceptanceEnvironmentKey] == "1",
            slice4PhysicalAcceptanceEnabled: ProcessInfo.processInfo.environment[BridgeConnectionLaunchConfiguration.slice4PhysicalAcceptanceEnvironmentKey] == "1"
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

                Section("Diagnostic read") {
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

                Section("Page0 scan") {
                    Button {
                        Task { await model.scanPage0Token() }
                    } label: {
                        Label("Scan page0 token", systemImage: "dot.radiowaves.left.and.right")
                    }
                    .disabled(model.isBusy || !model.isPaired)

                    if let value = model.lastScanBlock4Value {
                        LabeledContent("Block 4 (Apt #)", value: value)
                            .fontDesign(.monospaced)
                    }
                    if let signal = model.lastSignalMillivolts {
                        LabeledContent("Signal", value: "\(signal) mV")
                    }
                    if let url = model.lastUnknownDumpURL {
                        LabeledContent("Unknown dump", value: url.lastPathComponent)
                            .fontDesign(.monospaced)
                    }
                }

                Section("Page0 rides") {
                    Button {
                        Task { await model.readPage0Rides() }
                    } label: {
                        Label("Read page0 rides", systemImage: "arrow.down.circle")
                    }
                    .disabled(model.isBusy || !model.isPaired)

                    if let value = model.lastPage0Block5Value {
                        LabeledContent("Raw block 5", value: value)
                            .fontDesign(.monospaced)
                    }
                    if let value = model.lastPage0Block6Value {
                        LabeledContent("Raw block 6", value: value)
                            .fontDesign(.monospaced)
                    }
                    if let rides = model.resolvedPage0Rides {
                        LabeledContent("Resolved rides", value: String(rides))
                    } else if model.lastPage0Read != nil {
                        Text("Resolved rides: unknown encoding")
                            .foregroundStyle(.secondary)
                    }
                    if model.lastPage0Read != nil {
                        LabeledContent(
                            "Source",
                            value: model.page0SourceBlockNumber.map { "block \($0)" } ?? "unknown"
                        )
                        LabeledContent(
                            "Mirrors",
                            value: model.page0BlocksMatch == true ? "matched" : "mismatched"
                        )
                        LabeledContent("Warning", value: model.page0WarningDisplay ?? "None")
                            .foregroundStyle(.orange)
                    }

                    TextField("Target rides (0–500)", text: $model.targetPage0RidesText)
                        .keyboardType(.numberPad)
                        .textInputAutocapitalization(.never)
                        .autocorrectionDisabled()

                    Button {
                        Task { await model.setPage0Rides() }
                    } label: {
                        Label("Set page0 rides", systemImage: "arrow.up.circle")
                    }
                    .buttonStyle(.borderedProminent)
                    .disabled(!model.canSetPage0Rides)
                }

                Section("Page0 reset") {
                    Text("Choose a reset profile explicitly. Nothing is selected by default.")
                        .font(.footnote)
                        .foregroundStyle(.secondary)

                    Picker("Reset profile", selection: $model.selectedResetSequence) {
                        Text("None").tag(Optional<RideSequence>.none)
                        ForEach(ResetSequence.all) { profile in
                            Text(profile.sequence.rawValue.capitalized).tag(Optional(profile.sequence))
                        }
                    }
                    .disabled(model.isBusy || !model.isPaired)

                    Button {
                        Task { await model.confirmResetProfile() }
                    } label: {
                        Label("Confirm reset", systemImage: "arrow.counterclockwise")
                    }
                    .buttonStyle(.borderedProminent)
                    .disabled(!model.canConfirmReset)

                    if !model.lastResetBlockValues.isEmpty {
                        ForEach(model.lastResetBlockValues.keys.sorted(), id: \.self) { block in
                            if let value = model.lastResetBlockValues[block] {
                                LabeledContent("Verified block \(block)", value: value)
                                    .fontDesign(.monospaced)
                            }
                        }
                    }
                }
            }
            .navigationTitle("Bridge Diagnostic")
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
