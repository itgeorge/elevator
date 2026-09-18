import SwiftUI

/// Owns connection state separately from token state and routes the operator
/// into Concept A once an authenticated bridge client is ready.
@MainActor
struct RidesRootView: View {
    @StateObject private var connection: BridgeConnectionModel
    @State private var ridesModel: RidesViewModel?
    @State private var isDiagnosticsPresented = false
    @State private var usesFakeReader = false
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
            refreshOperatorWorkflow()
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
#endif
        }
        .onChange(of: connection.state) { _, _ in
            refreshOperatorWorkflow()
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
}
