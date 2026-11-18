import SwiftUI

@MainActor
struct ApartmentEntrySheet: View {
    @ObservedObject var model: RidesViewModel
    let allowsSkip: Bool

    private let digits = [["1", "2", "3"], ["4", "5", "6"], ["7", "8", "9"], ["", "0", ""]]

    var body: some View {
        GeometryReader { geometry in
            let compact = geometry.size.height < 520
            let keyHeight = min(56, max(36, (geometry.size.height - (compact ? 230 : 250)) / 4))
            ZStack {
                Button {
                    Task { await model.dismissApartmentEntry() }
                } label: {
                    Color.black.opacity(0.42).ignoresSafeArea()
                        .contentShape(Rectangle())
                }
                .buttonStyle(.plain)
                .accessibilityLabel("Dismiss apartment entry")
                .accessibilityIdentifier("apartment-entry-backdrop")

                VStack(spacing: compact ? 7 : 12) {
                    HStack {
                        Text("Apartment #").font(.title3.bold())
                        Spacer()
                    }
                    HStack(spacing: 8) {
                        Text(model.apartmentInput)
                            .font(.system(size: compact ? 32 : 40, weight: .semibold, design: .rounded).monospacedDigit())
                            .frame(maxWidth: .infinity, minHeight: compact ? 48 : 58, alignment: .center)
                            .accessibilityIdentifier("apartment-number-input")
                            .accessibilityLabel("Apartment number input")
                            .accessibilityValue(model.apartmentInput.isEmpty ? "Empty" : model.apartmentInput)
                        if !model.apartmentInput.isEmpty {
                            Button {
                                model.apartmentInput = ""
                            } label: {
                                Image(systemName: "xmark.circle.fill")
                                    .font(.title2)
                                    .foregroundStyle(.secondary)
                                    .frame(width: 44, height: 44)
                                    .contentShape(Rectangle())
                            }
                            .buttonStyle(.plain)
                            .accessibilityLabel("Clear apartment number")
                            .accessibilityIdentifier("clear-apartment-number")
                        }
                    }
                    .padding(.horizontal, 12)
                    .background(.quaternary, in: RoundedRectangle(cornerRadius: 12))

                    if !model.apartmentInput.isEmpty && UInt8(model.apartmentInput) == nil {
                        Text("Apartment must be between 0 and 255.")
                            .font(.footnote)
                            .foregroundStyle(.red)
                            .accessibilityIdentifier("apartment-validation-error")
                    } else if compact {
                        Color.clear.frame(height: 0)
                    }

                    VStack(spacing: compact ? 5 : 8) {
                        ForEach(Array(digits.enumerated()), id: \.offset) { _, row in
                            HStack(spacing: compact ? 6 : 8) {
                                ForEach(Array(row.enumerated()), id: \.offset) { _, digit in
                                    if digit.isEmpty {
                                        Color.clear.frame(maxWidth: .infinity, minHeight: keyHeight)
                                            .accessibilityHidden(true)
                                    } else {
                                        key(digit, height: keyHeight)
                                    }
                                }
                            }
                        }
                    }
                    HStack(spacing: 8) {
                        actionButton("Cancel", identifier: "apartment-cancel", height: keyHeight) {
                            Task { await model.cancelApartmentEntry() }
                        }
                        if allowsSkip {
                            actionButton("Skip", identifier: "apartment-skip", height: keyHeight) {
                                Task { await model.skipApartmentEntry() }
                            }
                        }
                        Button {
                            Task { await model.saveApartmentEntry() }
                        } label: {
                            Text("Save")
                                .font(.headline)
                                .frame(maxWidth: .infinity, minHeight: keyHeight)
                        }
                        .buttonStyle(.borderedProminent)
                        .disabled(!model.canSaveApartment)
                        .accessibilityIdentifier("apartment-save")
                    }
                }
                .padding(compact ? 12 : 24)
                .frame(maxWidth: 520)
                .frame(maxWidth: .infinity, maxHeight: .infinity)
                .fixedSize(horizontal: false, vertical: true)
                .background(Color.white, in: RoundedRectangle(cornerRadius: 20))
                .environment(\.colorScheme, .light)
                .padding(16)
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity)
            .ignoresSafeArea()
        }
        .accessibilityAddTraits(.isModal)
    }

    private func key(_ digit: String, height: CGFloat) -> some View {
        Button {
            guard model.apartmentInput.count < 3 else { return }
            model.apartmentInput += digit
        } label: {
            Text(digit).font(.title2.weight(.medium)).frame(maxWidth: .infinity, minHeight: height)
        }
        .buttonStyle(.bordered)
        .accessibilityLabel(digit)
        .accessibilityIdentifier("apartment-key-\(digit)")
    }

    private func actionButton(_ title: String, identifier: String, height: CGFloat, action: @escaping () -> Void) -> some View {
        Button(action: action) {
            Text(title)
                .font(.headline)
                .frame(maxWidth: .infinity, minHeight: height)
        }
        .buttonStyle(.bordered)
        .accessibilityIdentifier(identifier)
    }
}

@MainActor
struct SessionLoaderSheet: View {
    @ObservedObject var model: RidesViewModel
    @Environment(\.dismiss) private var dismiss
    private static let dateFormatter: DateFormatter = {
        let formatter = DateFormatter()
        formatter.dateStyle = .medium
        formatter.timeStyle = .short
        return formatter
    }()

    var body: some View {
        NavigationStack {
            List(model.savedChargeSessions) { session in
                Button {
                    model.loadChargeSession(session)
                    dismiss()
                } label: {
                    VStack(alignment: .leading, spacing: 5) {
                        Text(Self.dateFormatter.string(from: session.startedAt)).font(.headline)
                        Text("\(session.rideTotal) rides · \(session.charges.count) charges · €\(NSDecimalNumber(decimal: session.amountTotal))")
                            .font(.subheadline).foregroundStyle(.secondary)
                    }
                }
                .disabled(model.isBusy)
                .accessibilityIdentifier("saved-session-\(session.id.uuidString)")
            }
            .overlay {
                if model.savedChargeSessions.isEmpty { ContentUnavailableView("No saved sessions", systemImage: "clock") }
            }
            .navigationTitle("Load session")
            .toolbar { ToolbarItem(placement: .confirmationAction) { Button("Done") { dismiss() } } }
        }
    }
}
