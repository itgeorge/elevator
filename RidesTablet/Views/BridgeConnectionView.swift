import SwiftUI

/// Temporary Slice 1 diagnostic screen. Concept A remains in ContentView for later integration.
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
                    TextField("http://<Mac private IP>:5080", text: $model.bridgeURLText)
                        .textInputAutocapitalization(.never)
                        .autocorrectionDisabled()
                        .keyboardType(.URL)

                    Text("Enter the URL printed by RidesBridge on the Mac.")
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
            }
            .navigationTitle("Bridge Diagnostic")
        }
    }

    private var statusSymbol: String {
        switch model.state {
        case .connected, .restored: "checkmark.circle.fill"
        case .pairing, .reading, .forgetting: "arrow.triangle.2.circlepath"
        case .failed, .authenticationRequired: "exclamationmark.triangle.fill"
        case .unconfigured: "link.badge.plus"
        }
    }
}

#Preview {
    BridgeConnectionView(model: BridgeConnectionModel(credentialStore: InMemoryBridgeCredentialStore()))
}
