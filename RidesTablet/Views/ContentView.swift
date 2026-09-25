import SwiftUI

@MainActor
struct ContentView: View {
    @StateObject private var model: RidesViewModel
    var onOpenConnection: (() -> Void)?

    init(onOpenConnection: (() -> Void)? = nil) {
        _model = StateObject(wrappedValue: RidesViewModel(configuration: .load()))
        self.onOpenConnection = onOpenConnection
    }

    init(model: RidesViewModel, onOpenConnection: (() -> Void)? = nil) {
        _model = StateObject(wrappedValue: model)
        self.onOpenConnection = onOpenConnection
    }

    var body: some View {
        NavigationStack {
            ScrollView {
                VStack(spacing: 22) {
                    topBar
                    OneScreenLayout(model: model)
                    Color.clear
                        .frame(height: BottomChargeAction.scrollReservation)
                        .accessibilityHidden(true)
                }
                .padding(.horizontal, 34)
                .padding(.top, 22)
                .frame(maxWidth: 1180)
                .frame(maxWidth: .infinity)
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity)
            .background(Color(uiColor: .systemGroupedBackground))
            .overlay(alignment: .bottom) {
                BottomChargeAction(model: model)
                    .frame(height: BottomChargeAction.barHeight)
            }
            .navigationTitle("Rides")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                if let onOpenConnection {
                    ToolbarItem(placement: .topBarLeading) {
                        Button(action: onOpenConnection) {
                            Label("Connection", systemImage: "antenna.radiowaves.left.and.right")
                        }
                        .accessibilityLabel("Connection diagnostics")
                    }
                }
                if model.usesSimulationScenarios {
                    ToolbarItem(placement: .topBarTrailing) {
                        SimulationMenu(model: model)
                    }
                }
            }
        }
        .sheet(isPresented: $model.isResetSheetPresented) {
            ResetSheet(model: model)
        }
    }

    private var topBar: some View {
        VStack(alignment: .leading, spacing: 5) {
            Text("Token rides")
                .font(.system(size: 34, weight: .bold, design: .rounded))
            Text("Place one token on the reader, then tap Detect.")
                .font(.title3)
                .foregroundStyle(.secondary)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }
}

private struct OneScreenLayout: View {
    @ObservedObject var model: RidesViewModel

    var body: some View {
        // The landscape candidate has a real minimum width, so it cannot be
        // selected by a portrait iPad merely because its children are flexible.
        ViewThatFits(in: .horizontal) {
            HStack(alignment: .top, spacing: 18) {
                VStack(spacing: 14) {
                    DetectStrip(model: model, layout: .landscape)
                    AptControlRow(model: model)
                }
                .frame(minWidth: 365, idealWidth: 410, maxWidth: 445)

                MainPanel(model: model)
                    .frame(minWidth: 540, maxWidth: .infinity)
            }
            .frame(minWidth: 940, maxWidth: .infinity, alignment: .top)

            VStack(spacing: 14) {
                DetectStrip(model: model, layout: .portrait)
                MainPanel(model: model)
                AptControlRow(model: model)
            }
        }
    }
}

private struct BottomChargeAction: View {
    @ObservedObject var model: RidesViewModel

    // ChargeButton is 68pt high, with 12pt of breathing room on each side.
    // The scroll view reserves that whole bar plus the usual 22pt content gap.
    static let barHeight: CGFloat = 68 + 12 + 12
    static let scrollReservation: CGFloat = barHeight + 22

    var body: some View {
        // Keep the breakpoint identical to OneScreenLayout. The clear first
        // column preserves the landscape hierarchy without covering it.
        ViewThatFits(in: .horizontal) {
            HStack(alignment: .top, spacing: 18) {
                Color.clear
                    .frame(minWidth: 365, idealWidth: 410, maxWidth: 445)
                    .accessibilityHidden(true)

                ChargeButton(model: model, showsReset: false)
                    .frame(minWidth: 540, maxWidth: .infinity)
            }
            .frame(minWidth: 940, maxWidth: .infinity, alignment: .top)

            ChargeButton(model: model, showsReset: false)
        }
        .padding(.horizontal, 34)
        .padding(.top, 12)
        .padding(.bottom, 12)
        .frame(maxWidth: 1180)
        .frame(maxWidth: .infinity)
        .background(Color(uiColor: .systemGroupedBackground))
    }
}

private struct MainPanel: View {
    @ObservedObject var model: RidesViewModel

    private static let statusMinimumHeight: CGFloat = 374

    var body: some View {
        Group {
            if case .known = model.state {
                KnownControls(model: model)
                    .padding(16)
                    .frame(maxWidth: .infinity, alignment: .top)
            } else {
                StatusContent(model: model)
                    .padding(16)
                    .frame(maxWidth: .infinity, minHeight: Self.statusMinimumHeight, alignment: .center)
            }
        }
        .background(Color.white, in: RoundedRectangle(cornerRadius: 20))
        .overlay(RoundedRectangle(cornerRadius: 20).stroke(Color.black.opacity(0.08)))
        .accessibilityElement(children: .contain)
    }
}

private struct KnownControls: View {
    @ObservedObject var model: RidesViewModel

    var body: some View {
        VStack(spacing: 10) {
            HStack(spacing: 10) {
                ResetButton(model: model)
                    .frame(width: 72)
                CurrentRidesCard(model: model, centered: true, showReadOnlyCaption: false)
                Color.clear
                    .frame(width: 72)
                    .accessibilityHidden(true)
            }
            .fixedSize(horizontal: false, vertical: true)
            PendingRidesCard(
                model: model,
                emphasized: true,
                centered: true
            )
            EURCard(model: model)
                .padding(.top, 24)
        }
        .frame(maxWidth: .infinity)
    }
}

private struct StatusContent: View {
    @ObservedObject var model: RidesViewModel

    var body: some View {
        VStack(spacing: 9) {
            Image(systemName: icon)
                .font(.system(size: 34, weight: .semibold))
                .foregroundStyle(iconColor)
            Text(model.state.title)
                .font(.title3.bold())
                .multilineTextAlignment(.center)
            if let message = model.message {
                Text(message)
                    .font(.body.weight(.semibold))
                    .multilineTextAlignment(.center)
                    .fixedSize(horizontal: false, vertical: true)
            } else if case .idle = model.state {
                Text("No token is loaded yet.")
                    .foregroundStyle(.secondary)
            }
            if case .working = model.state {
                ProgressView()
                    .controlSize(.regular)
                    .accessibilityLabel("Operation in progress")
            }
            if case .unknown(let token) = model.state {
                Text(token.reason)
                    .font(.caption)
                    .foregroundStyle(.secondary)
                    .multilineTextAlignment(.center)
                if let url = model.lastDumpURL {
                    Text("Dump saved: \(url.lastPathComponent)")
                        .font(.caption.monospaced())
                        .foregroundStyle(.secondary)
                        .multilineTextAlignment(.center)
                        .textSelection(.enabled)
                }
            }
            if case .failed = model.state, let token = model.loadedToken {
                Text("Family: \(token.sequence.rawValue.capitalized)")
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }
            if case .unknown = model.state {
                ResetButton(model: model)
                    .padding(.top, 3)
            }
        }
        .frame(maxWidth: 560)
        .frame(maxWidth: .infinity)
        .multilineTextAlignment(.center)
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

private struct DetectStrip: View {
    enum Layout { case portrait, landscape }

    @ObservedObject var model: RidesViewModel
    let layout: Layout

    var body: some View {
        Group {
            if layout == .portrait {
                HStack(spacing: 0) {
                    detectButton
                    SignalView(model: model)
                        .padding(.horizontal, 16)
                }
            } else {
                VStack(spacing: 0) {
                    detectButton
                    SignalView(model: model)
                        .padding(14)
                        .frame(maxWidth: .infinity)
                        .background(Color.white)
                }
            }
        }
        .frame(maxWidth: .infinity, minHeight: layout == .portrait ? 92 : 184)
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
                .frame(maxWidth: .infinity, minHeight: layout == .portrait ? 92 : 82)
                .background(Color.blue)
        }
        .buttonStyle(.plain)
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

private struct CurrentRidesCard: View {
    @ObservedObject var model: RidesViewModel
    var centered = false
    var showReadOnlyCaption = true

    var body: some View {
        VStack(alignment: centered ? .center : .leading, spacing: 8) {
            Label("Current rides", systemImage: "lock.fill")
                .font(.headline)
            Text(model.hasKnownToken ? "\(model.currentRides)" : "—")
                .font(.system(size: 52, weight: .bold, design: .rounded).monospacedDigit())
            if showReadOnlyCaption {
                Text("Read-only")
                    .font(.callout)
                    .foregroundStyle(.secondary)
            }
        }
        .frame(maxWidth: .infinity, alignment: centered ? .center : .leading)
        .accessibilityElement(children: .combine)
        .accessibilityLabel(showReadOnlyCaption ? "Current rides, read-only" : "Current rides")
        .accessibilityValue(model.hasKnownToken ? "\(model.currentRides)" : "Not loaded")
    }
}

private struct PendingRidesCard: View {
    @ObservedObject var model: RidesViewModel
    var emphasized: Bool
    var centered = false

    var body: some View {
        HStack(alignment: .top, spacing: 12) {
            AdjustmentRail(model: model, direction: -1)
            pendingCenter
            AdjustmentRail(model: model, direction: 1)
        }
        .frame(minWidth: 308, maxWidth: .infinity, alignment: centered ? .center : .leading)
        .accessibilityElement(children: .contain)
    }

    private var pendingCenter: some View {
        PendingRidesDisplay(
            model: model,
            emphasized: emphasized
        )
        .frame(minWidth: 150, maxWidth: .infinity)
        .frame(height: AdjustmentButtonMetrics.railHeight)
    }
}

private struct PendingRidesDisplay: View {
    @ObservedObject var model: RidesViewModel
    let emphasized: Bool

    var body: some View {
        VStack(spacing: 4) {
            Text("PENDING")
                .font(.caption.bold())
                .tracking(1.2)
            Text(equationText)
                .font(.title3.weight(.semibold).monospacedDigit())
                .foregroundStyle(Color.white.opacity(0.78))
                .accessibilityHidden(true)
            Text(model.hasKnownToken ? "\(model.pendingRides)" : "—")
                .font(.system(size: emphasized ? 72 : 64, weight: .bold, design: .rounded).monospacedDigit())
                .lineLimit(1)
                .minimumScaleFactor(0.8)
                .frame(maxWidth: .infinity)
                .layoutPriority(2)
        }
        .foregroundStyle(.white)
        .padding(.horizontal, 8)
        .padding(.vertical, 12)
        .frame(maxWidth: .infinity, minHeight: AdjustmentButtonMetrics.railHeight, maxHeight: AdjustmentButtonMetrics.railHeight, alignment: .center)
        .background(Color.blue, in: RoundedRectangle(cornerRadius: 16))
        .overlay {
            RoundedRectangle(cornerRadius: 16)
                .stroke(Color.white.opacity(0.85), lineWidth: 2)
        }
        .accessibilityElement(children: .ignore)
        .accessibilityLabel("Pending rides")
        .accessibilityValue(accessibilityValue)
        .accessibilityIdentifier("pending-rides-display")
    }

    private var equationText: String {
        let delta = Int(model.pendingRides) - Int(model.currentRides)
        return delta < 0 ? "− \(-delta) =" : "+ \(delta) ="
    }

    private var accessibilityValue: String {
        guard model.hasKnownToken else { return "Not loaded" }
        let delta = Int(model.pendingRides) - Int(model.currentRides)
        let change: String
        if delta < 0 {
            change = "Decrease of \(-delta) rides"
        } else if delta > 0 {
            change = "Increase of \(delta) rides"
        } else {
            change = "No ride change"
        }
        return "\(change). Pending total \(model.pendingRides) rides"
    }
}

private enum AdjustmentButtonMetrics {
    static let width: CGFloat = 92
    static let height: CGFloat = 48
    static let spacing: CGFloat = 7
    static let railHeight = height * 3 + spacing * 2
}

private struct AdjustmentButton: View {
    let title: String
    let tint: Color
    let isDisabled: Bool
    let action: () -> Void
    let accessibilityLabel: String
    let accessibilityValue: String
    let accessibilityHint: String
    var accessibilityIdentifier: String?

    var body: some View {
        Button(action: action) {
            Text(title)
                .font(.headline.monospacedDigit())
                .lineLimit(1)
                .minimumScaleFactor(0.8)
                .frame(maxWidth: .infinity, maxHeight: .infinity)
        }
        .buttonStyle(.plain)
        .frame(width: AdjustmentButtonMetrics.width, height: AdjustmentButtonMetrics.height)
        .foregroundStyle(tint)
        .background(tint.opacity(0.16), in: Capsule())
        .overlay(Capsule().stroke(tint.opacity(0.08)))
        .clipShape(Capsule())
        .opacity(isDisabled ? 0.4 : 1)
        .disabled(isDisabled)
        .accessibilityLabel(accessibilityLabel)
        .accessibilityValue(accessibilityValue)
        .accessibilityHint(accessibilityHint)
        .accessibilityIdentifier(accessibilityIdentifier ?? "")
    }
}

private struct AdjustmentRail: View {
    @ObservedObject var model: RidesViewModel
    let direction: Int

    var body: some View {
        VStack(spacing: AdjustmentButtonMetrics.spacing) {
            ForEach([1, 10, 100], id: \.self) { amount in
                AdjustmentButton(
                    title: direction < 0 ? "−\(amount)" : "+\(amount)",
                    tint: direction < 0 ? .orange : .blue,
                    isDisabled: !model.canAdjust,
                    action: { model.adjustRides(by: direction * amount) },
                    accessibilityLabel: direction < 0 ? "Remove \(amount) rides" : "Add \(amount) rides",
                    accessibilityValue: model.hasKnownToken ? "Pending rides: \(model.pendingRides)" : "Pending rides not loaded",
                    accessibilityHint: "Adjusts the pending ride count by \(amount)",
                    accessibilityIdentifier: "pending-rides-\(direction < 0 ? "minus" : "plus")-\(amount)"
                )
            }
        }
        .frame(height: AdjustmentButtonMetrics.railHeight)
    }
}

private struct EURCard: View {
    @ObservedObject var model: RidesViewModel

    var body: some View {
        HStack(alignment: .center, spacing: 10) {
            eurButton(title: "−€1.50", amount: -model.configuration.cashIncrementEUR)
            HStack(spacing: 5) {
                VStack(spacing: 0) {
                    Text("EUR")
                        .font(.caption2.bold())
                        .tracking(1.1)
                    Text(model.costText)
                        .font(.system(size: 28, weight: .bold, design: .rounded).monospacedDigit())
                        .foregroundStyle(model.costEUR < 0 ? .green : .primary)
                        .lineLimit(1)
                        .minimumScaleFactor(0.75)
                }
                RoundingControl(
                    accessibilityLabel: "Round cost change",
                    accessibilityHint: "Rounds the signed ride delta directly to the nearest 1.50 euros, or 50 rides",
                    disabled: !model.canAdjust
                ) {
                    model.round(.costDelta)
                }
            }
            .frame(maxWidth: .infinity)
            .frame(height: AdjustmentButtonMetrics.height)
            eurButton(title: "+€1.50", amount: model.configuration.cashIncrementEUR)
        }
        .frame(height: AdjustmentButtonMetrics.height)
        .accessibilityElement(children: .contain)
    }

    private func eurButton(title: String, amount: Decimal) -> some View {
        AdjustmentButton(
            title: title,
            tint: amount < 0 ? RidesPalette.eurMinus : RidesPalette.eurPlus,
            isDisabled: !model.canAdjust,
            action: { model.adjustCost(by: amount) },
            accessibilityLabel: "Adjust EUR by \(title)",
            accessibilityValue: model.costText,
            accessibilityHint: "Adjusts the pending ride count by 50 rides"
        )
    }
}

private enum RidesPalette {
    static let eurMinus = Color(red: 0.80, green: 0.22, blue: 0.20)
    static let eurPlus = Color(red: 0.05, green: 0.42, blue: 0.48)
}

private struct ResetButton: View {
    @ObservedObject var model: RidesViewModel

    var body: some View {
        Button("RESET") {
            model.openReset()
        }
        .font(.caption.bold())
        .frame(minWidth: 64, minHeight: 44)
        .buttonStyle(.bordered)
        .disabled(model.isBusy)
        .accessibilityLabel("Reset token")
        .accessibilityHint("Opens reset profiles without selecting one")
    }
}

private struct RoundingControl: View {
    let accessibilityLabel: String
    let accessibilityHint: String
    let disabled: Bool
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            Image(systemName: "arrow.triangle.2.circlepath")
                .font(.callout.bold())
                .frame(width: 32, height: 32)
                .background(Color.secondary.opacity(0.12), in: Circle())
        }
        .frame(width: 44, height: 44)
        .buttonStyle(.plain)
        .contentShape(Circle())
        .disabled(disabled)
        .accessibilityLabel(accessibilityLabel)
        .accessibilityHint(accessibilityHint)
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
    var showsReset = true

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

            if showsReset {
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
            Label("Simulation", systemImage: "slider.horizontal.3")
                .labelStyle(.iconOnly)
        }
        .disabled(model.isBusy)
        .accessibilityLabel("Simulation scenarios")
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
