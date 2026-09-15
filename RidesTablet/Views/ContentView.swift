import SwiftUI

@MainActor
struct ContentView: View {
    enum Concept: String, CaseIterable, Identifiable {
        case a = "A"
        case b = "B"
        case c = "C"
        var id: String { rawValue }
    }

    @StateObject private var model: RidesViewModel
    @State private var concept: Concept = .b

    init() {
        _model = StateObject(wrappedValue: RidesViewModel(configuration: .load()))
        let argument = ProcessInfo.processInfo.arguments.first(where: { $0.hasPrefix("-concept") })
        let requested = argument?.replacingOccurrences(of: "-concept", with: "").uppercased()
        _concept = State(initialValue: Concept(rawValue: requested ?? "") ?? .b)
    }

    init(model: RidesViewModel) {
        _model = StateObject(wrappedValue: model)
        _concept = State(initialValue: .b)
    }

    var body: some View {
        NavigationStack {
            ScrollView {
                VStack(spacing: 22) {
                    topBar
                    conceptContent
                }
                .padding(.horizontal, 34)
                .padding(.vertical, 22)
                .frame(maxWidth: 1180)
                .frame(maxWidth: .infinity)
            }
            .background(Color(uiColor: .systemGroupedBackground))
            .navigationTitle("Rides")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .topBarTrailing) {
                    Picker("Prototype concept", selection: $concept) {
                        ForEach(Concept.allCases) { item in
                            Text("Concept \(item.rawValue)").tag(item)
                        }
                    }
                    .pickerStyle(.menu)
                    .accessibilityLabel("Choose prototype concept A, B, or C")
                }
            }
        }
        .sheet(isPresented: $model.isResetSheetPresented) {
            ResetSheet(model: model)
        }
    }

    private var topBar: some View {
        HStack(alignment: .top, spacing: 18) {
            VStack(alignment: .leading, spacing: 5) {
                Text("Token rides")
                    .font(.system(size: 34, weight: .bold, design: .rounded))
                Text("Place one token on the reader, then tap Detect.")
                    .font(.title3)
                    .foregroundStyle(.secondary)
            }
            Spacer(minLength: 12)
            SimulationMenu(model: model)
        }
    }

    @ViewBuilder
    private var conceptContent: some View {
        switch concept {
        case .a:
            ConceptA(model: model)
        case .b:
            ConceptB(model: model)
        case .c:
            ConceptC(model: model)
        }
    }
}

private struct ConceptA: View {
    @ObservedObject var model: RidesViewModel

    var body: some View {
        VStack(spacing: 18) {
            StatusPanel(model: model, compact: false)
            ViewThatFits(in: .horizontal) {
                HStack(alignment: .top, spacing: 18) {
                    DetectPanel(model: model)
                        .frame(minWidth: 320, idealWidth: 370, maxWidth: 390)
                    RideEditor(model: model)
                        .frame(minWidth: 350, maxWidth: .infinity)
                }
                VStack(spacing: 18) {
                    DetectPanel(model: model)
                    RideEditor(model: model)
                }
            }
            AptControlRow(model: model)
            ChargeButton(model: model)
        }
    }
}

private struct ConceptB: View {
    @ObservedObject var model: RidesViewModel

    var body: some View {
        VStack(spacing: 18) {
            ConceptBDetectStrip(model: model)
            StatusPanel(model: model, compact: true)
            ConceptBRideEditor(model: model)
            AptControlRow(model: model)
                .frame(maxWidth: 620)
            ChargeButton(model: model)
                .frame(maxWidth: 620)
        }
    }
}

private struct ConceptBRideEditor: View {
    @ObservedObject var model: RidesViewModel

    var body: some View {
        VStack(spacing: 17) {
            CurrentRidesCard(model: model, centered: true)
            PendingRidesCard(model: model, emphasized: true, centered: true)
            CostCard(model: model)
        }
        .padding(20)
        .frame(maxWidth: .infinity)
        .background(Color.white, in: RoundedRectangle(cornerRadius: 20))
        .overlay(RoundedRectangle(cornerRadius: 20).stroke(Color.black.opacity(0.08)))
    }
}

private struct ConceptBDetectStrip: View {
    @ObservedObject var model: RidesViewModel

    var body: some View {
        HStack(spacing: 0) {
            detectButton
                .frame(maxWidth: .infinity, minHeight: 104)
            SignalView(model: model)
                .frame(maxWidth: .infinity, minHeight: 104)
                .padding(.horizontal, 20)
        }
        .frame(maxWidth: .infinity, minHeight: 104)
        .background(Color.white)
        .clipShape(RoundedRectangle(cornerRadius: 20))
        .overlay {
            RoundedRectangle(cornerRadius: 20)
                .stroke(Color.black.opacity(0.08))
        }
    }

    private var detectButton: some View {
        Button {
            Task { await model.detect() }
        } label: {
            Label(model.isBusy ? "Working…" : "Detect token", systemImage: model.isBusy ? "hourglass" : "dot.radiowaves.left.and.right")
                .font(.title2.bold())
                .foregroundStyle(.white)
                .frame(maxWidth: .infinity, minHeight: 104)
                .background(Color.blue)
        }
        .buttonStyle(.plain)
        .disabled(model.isBusy)
        .accessibilityLabel("Detect token")
        .accessibilityHint("Detects, tunes, and reads the token in one step")
    }
}

private struct ConceptC: View {
    @ObservedObject var model: RidesViewModel

    var body: some View {
        VStack(spacing: 18) {
            ViewThatFits(in: .horizontal) {
                HStack(alignment: .top, spacing: 18) {
                    VStack(spacing: 18) {
                        DetectPanel(model: model)
                        StatusPanel(model: model, compact: true)
                    }
                    .frame(minWidth: 320, idealWidth: 370, maxWidth: 390)
                    RideEditor(model: model, emphasized: true)
                        .frame(minWidth: 350, maxWidth: .infinity)
                }
                VStack(spacing: 18) {
                    DetectPanel(model: model)
                    StatusPanel(model: model, compact: true)
                    RideEditor(model: model, emphasized: true)
                }
            }
            HStack(spacing: 18) {
                AptControlRow(model: model)
                ChargeButton(model: model)
            }
        }
    }
}

private struct DetectPanel: View {
    @ObservedObject var model: RidesViewModel
    var horizontal = false

    var body: some View {
        Group {
            if horizontal {
                HStack(spacing: 18) {
                    detectButton
                    SignalView(model: model)
                }
            } else {
                VStack(alignment: .leading, spacing: 15) {
                    detectButton
                    SignalView(model: model)
                }
            }
        }
        .padding(20)
        .frame(maxWidth: horizontal ? .infinity : 390, alignment: .leading)
        .background(Color.white, in: RoundedRectangle(cornerRadius: 20))
        .overlay(RoundedRectangle(cornerRadius: 20).stroke(Color.black.opacity(0.08)))
    }

    private var detectButton: some View {
        Button {
            Task { await model.detect() }
        } label: {
            Label(model.isBusy ? "Working…" : "Detect token", systemImage: model.isBusy ? "hourglass" : "dot.radiowaves.left.and.right")
                .font(.title2.bold())
                .frame(maxWidth: .infinity, minHeight: 72)
        }
        .buttonStyle(.borderedProminent)
        .tint(.blue)
        .disabled(model.isBusy)
        .accessibilityLabel("Detect token")
        .accessibilityHint("Detects, tunes, and reads the token in one step")
    }
}

private struct SignalView: View {
    @ObservedObject var model: RidesViewModel

    var body: some View {
        VStack(alignment: .leading, spacing: 7) {
            HStack {
                Label("Reader signal", systemImage: "wave.3.right")
                    .font(.headline)
                Spacer()
                if let signal = model.lastSignalMillivolts {
                    Text("\(signal) mV")
                        .font(.title2.bold().monospacedDigit())
                } else {
                    Text("—")
                        .font(.title2.bold())
                }
            }
            GeometryReader { proxy in
                Capsule()
                    .fill(Color.gray.opacity(0.18))
                    .overlay(alignment: .leading) {
                        Capsule()
                            .fill(signalColor)
                            .frame(width: max(10, proxy.size.width * signalFraction))
                    }
            }
            .frame(height: 14)
            Text("Reposition the token if the signal is weak.")
                .font(.callout)
                .foregroundStyle(.secondary)
        }
        .accessibilityElement(children: .combine)
        .accessibilityLabel("Reader signal")
        .accessibilityValue(model.lastSignalMillivolts.map { "\($0) millivolts" } ?? "Not measured")
    }

    private var signalFraction: CGFloat {
        guard let signal = model.lastSignalMillivolts else { return 0 }
        return min(1, max(0.04, CGFloat(signal) / 500))
    }

    private var signalColor: Color {
        guard let signal = model.lastSignalMillivolts else { return .gray }
        return signal < 200 ? .orange : .green
    }
}

private struct StatusPanel: View {
    @ObservedObject var model: RidesViewModel
    var compact: Bool

    var body: some View {
        HStack(alignment: .top, spacing: 13) {
            Image(systemName: icon)
                .font(.title)
                .foregroundStyle(iconColor)
            VStack(alignment: .leading, spacing: 4) {
                Text(model.state.title)
                    .font(compact ? .headline : .title3.bold())
                if let message = model.message {
                    Text(message)
                        .font(.body.weight(.semibold))
                } else if !compact {
                    Text("No token is loaded yet.")
                        .foregroundStyle(.secondary)
                }
                if let url = model.lastDumpURL {
                    Text("Dump saved: \(url.lastPathComponent)")
                        .font(.caption.monospaced())
                        .foregroundStyle(.secondary)
                }
                if let token = model.loadedToken {
                    Text("Family: \(token.sequence.rawValue.capitalized)")
                        .font(.caption)
                        .foregroundStyle(.secondary)
                }
            }
            Spacer()
        }
        .padding(16)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(Color.white, in: RoundedRectangle(cornerRadius: 16))
        .overlay(RoundedRectangle(cornerRadius: 16).stroke(Color.black.opacity(0.08)))
        .accessibilityElement(children: .combine)
        .accessibilityLabel("Operation status")
        .accessibilityValue([model.state.title, model.message].compactMap { $0 }.joined(separator: ". "))
    }

    private var icon: String {
        switch model.state {
        case .known: return "checkmark.circle.fill"
        case .unknown: return "questionmark.circle.fill"
        case .noChip: return "exclamationmark.triangle.fill"
        case .failed: return "xmark.octagon.fill"
        case .working: return "hourglass"
        default: return "info.circle.fill"
        }
    }

    private var iconColor: Color {
        switch model.state {
        case .known: return .green
        case .unknown, .noChip, .failed: return .orange
        default: return .blue
        }
    }
}

private struct RideEditor: View {
    @ObservedObject var model: RidesViewModel
    var emphasized = false

    var body: some View {
        VStack(spacing: 17) {
            ViewThatFits(in: .horizontal) {
                HStack(alignment: .top, spacing: 16) {
                    CurrentRidesCard(model: model)
                        .frame(width: 174, alignment: .leading)
                    PendingRidesCard(model: model, emphasized: emphasized)
                        .frame(minWidth: 308, maxWidth: .infinity)
                }
                VStack(alignment: .leading, spacing: 16) {
                    CurrentRidesCard(model: model)
                        .frame(maxWidth: .infinity, alignment: .leading)
                    PendingRidesCard(model: model, emphasized: emphasized)
                        .frame(maxWidth: .infinity, alignment: .leading)
                }
            }
            CostCard(model: model)
        }
        .padding(20)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(Color.white, in: RoundedRectangle(cornerRadius: 20))
        .overlay(RoundedRectangle(cornerRadius: 20).stroke(Color.black.opacity(0.08)))
    }
}

private struct CurrentRidesCard: View {
    @ObservedObject var model: RidesViewModel
    var centered = false

    var body: some View {
        VStack(alignment: centered ? .center : .leading, spacing: 8) {
            Label("Current rides", systemImage: "lock.fill")
                .font(.headline)
            Text(model.hasKnownToken ? "\(model.currentRides)" : "—")
                .font(.system(size: 52, weight: .bold, design: .rounded).monospacedDigit())
            Text("Read-only")
                .font(.callout)
                .foregroundStyle(.secondary)
        }
        .frame(maxWidth: .infinity, alignment: centered ? .center : .leading)
        .accessibilityElement(children: .combine)
        .accessibilityLabel("Current rides, read-only")
        .accessibilityValue(model.hasKnownToken ? "\(model.currentRides)" : "Not loaded")
    }
}

private struct PendingRidesCard: View {
    @ObservedObject var model: RidesViewModel
    var emphasized: Bool
    var centered = false

    var body: some View {
        VStack(alignment: centered ? .center : .leading, spacing: 8) {
            Text("Pending rides")
                .font(.headline)
            HStack(alignment: .top, spacing: 12) {
                AdjustmentRail(model: model, direction: -1)
                pendingCenter
                AdjustmentRail(model: model, direction: 1)
            }
            Text("Tap − or + by 1, 10, or 100")
                .font(.callout)
                .foregroundStyle(.secondary)
        }
        .frame(minWidth: 308, maxWidth: .infinity, alignment: centered ? .center : .leading)
        .accessibilityElement(children: .contain)
    }

    private var pendingCenter: some View {
        VStack(spacing: 8) {
            VStack(spacing: 2) {
                Text("PENDING")
                    .font(.caption.bold())
                    .tracking(1.2)
                Text(model.hasKnownToken ? "\(model.pendingRides)" : "—")
                    .font(.system(size: emphasized ? 64 : 58, weight: .bold, design: .rounded).monospacedDigit())
                    .lineLimit(1)
                    .minimumScaleFactor(0.8)
                    .frame(maxWidth: .infinity, minHeight: 76)
                    .layoutPriority(2)
            }
            .foregroundStyle(.white)
            .frame(maxWidth: .infinity, minHeight: 108)
            .background(Color.blue, in: RoundedRectangle(cornerRadius: 16))
            .overlay {
                RoundedRectangle(cornerRadius: 16)
                    .stroke(Color.white.opacity(0.8), lineWidth: 2)
            }
            .accessibilityElement(children: .ignore)
            .accessibilityLabel("Pending rides")
            .accessibilityValue(pendingAccessibilityValue)
            .accessibilityIdentifier("pending-rides-display")

            RoundingControl(
                caption: "Nearest 50 rides",
                accessibilityLabel: "Round pending rides",
                accessibilityHint: "Rounds the pending target directly to the nearest 50 rides",
                disabled: !model.canAdjust
            ) {
                model.round(.pendingRides)
            }
        }
        .padding(10)
        .frame(minWidth: 150, maxWidth: .infinity)
        .background(Color.blue.opacity(0.08), in: RoundedRectangle(cornerRadius: 20))
        .overlay {
            RoundedRectangle(cornerRadius: 20)
                .stroke(Color.blue.opacity(0.35), lineWidth: 1.5)
        }
    }

    private var pendingAccessibilityValue: String {
        model.hasKnownToken ? "\(model.pendingRides) rides" : "Not loaded"
    }
}

private struct AdjustmentRail: View {
    @ObservedObject var model: RidesViewModel
    let direction: Int

    var body: some View {
        VStack(spacing: 7) {
            ForEach([1, 10, 100], id: \.self) { amount in
                Button {
                    model.adjustRides(by: direction * amount)
                } label: {
                    Text(direction < 0 ? "−\(amount)" : "+\(amount)")
                        .font(.headline.monospacedDigit())
                        .frame(width: 67, height: 45)
                }
                .buttonStyle(.bordered)
                .tint(direction < 0 ? .orange : .blue)
                .disabled(!model.canAdjust)
                .accessibilityLabel(direction < 0 ? "Remove \(amount) rides" : "Add \(amount) rides")
                .accessibilityValue(model.hasKnownToken ? "Pending rides: \(model.pendingRides)" : "Pending rides not loaded")
                .accessibilityHint("Adjusts the pending ride count by \(amount)")
                .accessibilityIdentifier("pending-rides-\(direction < 0 ? "minus" : "plus")-\(amount)")
            }
        }
    }
}

private struct CostCard: View {
    @ObservedObject var model: RidesViewModel

    var body: some View {
        VStack(spacing: 9) {
            HStack {
                Label("Change in cost", systemImage: "eurosign.circle")
                    .font(.headline)
                Spacer()
                Text(model.costText)
                    .font(.system(size: 35, weight: .bold, design: .rounded).monospacedDigit())
                    .foregroundStyle(model.costEUR < 0 ? .green : .primary)
            }
            RoundingControl(
                caption: "Nearest €1.50",
                accessibilityLabel: "Round cost change",
                accessibilityHint: "Rounds the signed ride delta directly to the nearest 1.50 euros, or 50 rides",
                disabled: !model.canAdjust
            ) {
                model.round(.costDelta)
            }
            Text(model.costEUR < 0 ? "Refund / decrease" : "Amount to charge")
                .frame(maxWidth: .infinity, alignment: .leading)
                .font(.callout)
                .foregroundStyle(.secondary)
            HStack(spacing: 12) {
                costButton(title: "− €1.50", amount: -model.configuration.cashIncrementEUR)
                costButton(title: "+ €1.50", amount: model.configuration.cashIncrementEUR)
            }
        }
        .padding(.top, 4)
        .accessibilityElement(children: .contain)
    }

    private func costButton(title: String, amount: Decimal) -> some View {
        Button(title) {
            model.adjustCost(by: amount)
        }
        .font(.headline)
        .frame(maxWidth: .infinity, minHeight: 48)
        .buttonStyle(.bordered)
        .disabled(!model.canAdjust)
        .accessibilityLabel("Adjust cost by \(title.replacingOccurrences(of: "€", with: "euros "))")
    }
}

private struct RoundingControl: View {
    let caption: String
    let accessibilityLabel: String
    let accessibilityHint: String
    let disabled: Bool
    let action: () -> Void

    var body: some View {
        VStack(spacing: 3) {
            Button(action: action) {
                Image(systemName: "arrow.triangle.2.circlepath")
                    .font(.title3.bold())
                    .frame(width: 48, height: 48)
            }
            .buttonStyle(.bordered)
            .clipShape(Circle())
            .disabled(disabled)
            .accessibilityLabel(accessibilityLabel)
            .accessibilityHint(accessibilityHint)
            Text(caption)
                .font(.caption)
                .foregroundStyle(.secondary)
        }
        .frame(maxWidth: .infinity)
    }
}

private struct AptControlRow: View {
    @ObservedObject var model: RidesViewModel

    var body: some View {
        HStack(spacing: 10) {
            Button("Cancel") {}
                .frame(width: 76, height: 48)
                .disabled(true)
            HStack(spacing: 7) {
                Text("Apt #")
                    .font(.headline)
                Spacer(minLength: 4)
                Text("Block 4")
                    .font(.subheadline)
                Text(model.aptBlock4Text)
                    .font(.caption.monospaced())
                    .foregroundStyle(.secondary)
            }
            .padding(.horizontal, 12)
            .frame(minHeight: 52)
            .frame(maxWidth: .infinity)
            .background(Color.white, in: RoundedRectangle(cornerRadius: 14))
            .overlay(RoundedRectangle(cornerRadius: 14).stroke(Color.black.opacity(0.08)))
            .lineLimit(1)
            .minimumScaleFactor(0.7)
            Button("Save") {}
                .frame(width: 76, height: 48)
                .disabled(true)
        }
        .accessibilityElement(children: .contain)
        .accessibilityLabel("Apartment number row")
        .accessibilityHint("Cancel is on the left and Save is on the right; the apartment number is read-only")
    }
}

private struct ChargeButton: View {
    @ObservedObject var model: RidesViewModel

    var body: some View {
        VStack(spacing: 8) {
            Button {
                Task { await model.charge() }
            } label: {
                Label(model.isBusy ? "Working…" : "Charge token", systemImage: "bolt.fill")
                    .font(.title2.bold())
                    .frame(maxWidth: .infinity, minHeight: 68)
            }
            .buttonStyle(.borderedProminent)
            .tint(.green)
            .disabled(!model.canCharge)
            .accessibilityLabel("Charge token")
            .accessibilityHint("Writes the pending ride count to the token")
            Button("RESET") {
                model.openReset()
            }
            .font(.caption.bold())
            .buttonStyle(.borderless)
            .disabled(model.isBusy)
            .accessibilityLabel("Reset token")
        }
    }
}

private struct SimulationMenu: View {
    @ObservedObject var model: RidesViewModel

    var body: some View {
        Menu {
            ForEach(SimulationScenario.allCases) { scenario in
                Button {
                    model.selectSimulation(scenario)
                } label: {
                    if model.simulationScenario == scenario {
                        Label(scenario.rawValue, systemImage: "checkmark")
                    } else {
                        Text(scenario.rawValue)
                    }
                }
            }
        } label: {
            Label("SIMULATION", systemImage: "slider.horizontal.3")
                .font(.caption.bold())
        }
        .buttonStyle(.bordered)
        .disabled(model.isBusy)
        .accessibilityLabel("SIMULATION scenarios")
        .accessibilityHint("Prototype-only fake reader scenarios")
    }
}

private struct ResetSheet: View {
    @ObservedObject var model: RidesViewModel
    @Environment(\.dismiss) private var dismiss

    private let gridColumns = [
        GridItem(.adaptive(minimum: 130, maximum: 220), spacing: 10)
    ]

    var body: some View {
        VStack(spacing: 0) {
            header
            Divider()
            profileGrid
            footer
        }
        .background(Color(uiColor: .systemGroupedBackground))
        .presentationDetents([.height(500), .large])
        .presentationDragIndicator(.visible)
    }

    private var header: some View {
        VStack(spacing: 8) {
            HStack {
                Button("Cancel") { dismiss() }
                    .font(.body.weight(.semibold))
                    .frame(minWidth: 76, minHeight: 44, alignment: .leading)
                    .accessibilityLabel("Cancel reset")
                    .accessibilityHint("Closes reset without changing the token")
                Spacer(minLength: 8)
                Text("Reset token")
                    .font(.title3.bold())
                    .lineLimit(1)
                Spacer(minLength: 8)
                // Match the leading control's width so the title stays centered.
                Color.clear
                    .frame(width: 76, height: 44)
                    .accessibilityHidden(true)
            }
            .frame(maxWidth: .infinity)

            VStack(alignment: .leading, spacing: 3) {
                Text("Choose a reset profile")
                    .font(.headline)
                Text("Nothing is selected until you tap a profile. Reset overwrites the token and sets rides to zero.")
                    .font(.subheadline)
                    .foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
            }
            .frame(maxWidth: .infinity, alignment: .leading)
        }
        .padding(.horizontal, 20)
        .padding(.top, 12)
        .padding(.bottom, 12)
    }

    private var profileGrid: some View {
        ScrollView {
            LazyVGrid(columns: gridColumns, alignment: .center, spacing: 10) {
                ForEach(RideSequence.allCases, id: \.self) { sequence in
                    profileChip(for: sequence)
                }
            }
            .padding(.horizontal, 20)
            .padding(.vertical, 14)
        }
        .scrollIndicators(.visible)
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .accessibilityLabel("Reset profiles")
    }

    private func profileChip(for sequence: RideSequence) -> some View {
        let isSelected = model.selectedResetSequence == sequence

        return Button {
            model.selectedResetSequence = sequence
        } label: {
            HStack(spacing: 8) {
                Text(sequence.rawValue.capitalized)
                    .font(.headline)
                    .lineLimit(2)
                    .minimumScaleFactor(0.85)
                Spacer(minLength: 2)
                Image(systemName: isSelected ? "checkmark.circle.fill" : "circle")
                    .font(.title3)
                    .accessibilityHidden(true)
            }
            .foregroundStyle(isSelected ? Color.accentColor : Color.primary)
            .padding(.horizontal, 12)
            .frame(maxWidth: .infinity, minHeight: 56)
            .background(
                isSelected
                    ? Color.accentColor.opacity(0.14)
                    : Color(uiColor: .secondarySystemGroupedBackground),
                in: RoundedRectangle(cornerRadius: 12)
            )
            .overlay {
                RoundedRectangle(cornerRadius: 12)
                    .stroke(
                        isSelected ? Color.accentColor : Color.secondary.opacity(0.45),
                        lineWidth: isSelected ? 2 : 1
                    )
            }
        }
        .buttonStyle(.plain)
        .accessibilityLabel("\(sequence.rawValue.capitalized) reset profile")
        .accessibilityValue(isSelected ? "Selected" : "Not selected")
        .accessibilityAddTraits(isSelected ? .isSelected : [])
        .accessibilityIdentifier("reset-profile-\(sequence.rawValue)")
    }

    private var footer: some View {
        VStack(spacing: 10) {
            Divider()
            HStack(spacing: 12) {
                VStack(alignment: .leading, spacing: 2) {
                    Text("Selected profile")
                        .font(.caption)
                        .foregroundStyle(.secondary)
                    Text(model.selectedResetSequence?.rawValue.capitalized ?? "Choose a profile")
                        .font(.headline)
                        .lineLimit(1)
                        .minimumScaleFactor(0.8)
                }
                .frame(maxWidth: .infinity, alignment: .leading)

                Button {
                    Task { await model.confirmReset() }
                } label: {
                    Label("Confirm reset", systemImage: "arrow.counterclockwise")
                        .font(.headline)
                        .frame(minWidth: 150, minHeight: 52)
                        .foregroundStyle(model.canConfirmReset ? Color.white : Color.secondary)
                        .background(
                            model.canConfirmReset
                                ? Color.accentColor
                                : Color(uiColor: .tertiarySystemFill),
                            in: RoundedRectangle(cornerRadius: 12)
                        )
                        .overlay {
                            RoundedRectangle(cornerRadius: 12)
                                .stroke(
                                    model.canConfirmReset
                                        ? Color.clear
                                        : Color.secondary.opacity(0.45),
                                    lineWidth: 1
                                )
                        }
                }
                .buttonStyle(.plain)
                .disabled(!model.canConfirmReset)
                .accessibilityLabel("Confirm reset")
                .accessibilityHint("Overwrites the token with the selected reset profile")
            }
        }
        .padding(.horizontal, 20)
        .padding(.top, 10)
        .padding(.bottom, 12)
        .background(.regularMaterial)
    }
}

#Preview {
    ContentView(model: RidesViewModel(device: FakeProxmark()))
}
