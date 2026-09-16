import SwiftUI

/// Temporary Slice 2 Mercury diagnostic screen. Concept A remains in ContentView for later integration.
public struct BridgeConnectionView: View {
    @StateObject private var model: BridgeConnectionModel
    @State private var pin = ""

    @MainActor
    public init() {
        _model = StateObject(wrappedValue: BridgeConnectionModel())
    }

    @MainActor
    public init(model: BridgeConnectionModel) {
        _model = StateObject(wrappedValue: model)
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
                        .disabled(model.isBusy || !model.isPaired)
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

                Section("Mercury rides") {
                    Button {
                        Task { await model.readMercuryRides() }
                    } label: {
                        Label("Read Mercury rides", systemImage: "arrow.down.circle")
                    }
                    .disabled(model.isBusy || !model.isPaired)

                    if let value = model.lastMercuryBlock5Value {
                        LabeledContent("Raw block 5", value: value)
                            .fontDesign(.monospaced)
                    }
                    if let value = model.lastMercuryBlock6Value {
                        LabeledContent("Raw block 6", value: value)
                            .fontDesign(.monospaced)
                    }
                    if let rides = model.resolvedMercuryRides {
                        LabeledContent("Resolved rides", value: String(rides))
                    } else if model.lastMercuryRead != nil {
                        Text("Resolved rides: unknown encoding")
                            .foregroundStyle(.secondary)
                    }
                    if model.lastMercuryRead != nil {
                        LabeledContent(
                            "Source",
                            value: model.mercurySourceBlockNumber.map { "block \($0)" } ?? "unknown"
                        )
                        LabeledContent(
                            "Mirrors",
                            value: model.mercuryBlocksMatch == true ? "matched" : "mismatched"
                        )
                        LabeledContent("Warning", value: model.mercuryWarningDisplay ?? "None")
                            .foregroundStyle(.orange)
                    }

                    TextField("Target rides (0–500)", text: $model.targetMercuryRidesText)
                        .keyboardType(.numberPad)
                        .textInputAutocapitalization(.never)
                        .autocorrectionDisabled()

                    Button {
                        Task { await model.setMercuryRides() }
                    } label: {
                        Label("Set Mercury rides", systemImage: "arrow.up.circle")
                    }
                    .buttonStyle(.borderedProminent)
                    .disabled(!model.canSetMercuryRides)
                }
            }
            .navigationTitle("Bridge Diagnostic")
        }
    }

    private var statusSymbol: String {
        switch model.state {
        case .connected, .restored: "checkmark.circle.fill"
        case .pairing, .reading, .readingMercury, .settingMercury, .forgetting: "arrow.triangle.2.circlepath"
        case .failed, .authenticationRequired: "exclamationmark.triangle.fill"
        case .unconfigured: "link.badge.plus"
        }
    }
}

#Preview {
    BridgeConnectionView(model: BridgeConnectionModel(credentialStore: InMemoryBridgeCredentialStore()))
}
