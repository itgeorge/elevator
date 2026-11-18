import SwiftUI

@MainActor
struct ApartmentEntrySheet: View {
    @ObservedObject var model: RidesViewModel
    let allowsSkip: Bool
    @Environment(\.dismiss) private var dismiss

    private let digits = [["1", "2", "3"], ["4", "5", "6"], ["7", "8", "9"]]

    var body: some View {
        VStack(spacing: 14) {
            HStack {
                Text("Apartment #").font(.title3.bold())
                Spacer()
                Button { Task { await model.cancelApartmentEntry() } } label: {
                    Image(systemName: "xmark.circle.fill").font(.title2).foregroundStyle(.secondary)
                }
                .accessibilityLabel("Cancel apartment entry")
            }
            Text(model.apartmentInput)
                .font(.system(size: 40, weight: .semibold, design: .rounded).monospacedDigit())
                .frame(maxWidth: .infinity, minHeight: 58)
                .background(.quaternary, in: RoundedRectangle(cornerRadius: 12))
                .accessibilityIdentifier("apartment-number-input")
                .accessibilityLabel("Apartment number input")
                .accessibilityValue(model.apartmentInput.isEmpty ? "Empty" : model.apartmentInput)
            if !model.apartmentInput.isEmpty && UInt8(model.apartmentInput) == nil {
                Text("Apartment must be between 0 and 255.")
                    .font(.footnote)
                    .foregroundStyle(.red)
                    .accessibilityIdentifier("apartment-validation-error")
            }
            VStack(spacing: 8) {
                ForEach(digits, id: \.self) { row in
                    HStack(spacing: 8) { ForEach(row, id: \.self) { digit in key(digit) } }
                }
                HStack(spacing: 8) {
                    Color.clear.frame(maxWidth: .infinity, minHeight: 56)
                    key("0")
                    Button { if !model.apartmentInput.isEmpty { model.apartmentInput.removeLast() } } label: {
                        Image(systemName: "delete.left").frame(maxWidth: .infinity, minHeight: 56)
                    }.buttonStyle(.bordered).accessibilityLabel("Backspace")
                }
            }
            HStack {
                Button("Cancel") { Task { await model.cancelApartmentEntry() } }.buttonStyle(.bordered).frame(minHeight: 40)
                Spacer()
                if allowsSkip {
                    Button("Skip") { Task { await model.skipApartmentEntry() } }.buttonStyle(.bordered).frame(minHeight: 40)
                }
                Button("Save") { Task { await model.saveApartmentEntry() } }
                    .buttonStyle(.borderedProminent).frame(minHeight: 40).disabled(!model.canSaveApartment)
            }
        }
        .padding(24)
        .frame(maxWidth: 520)
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .background(Color.white)
        .environment(\.colorScheme, .light)
        .presentationDetents([.large])
        .presentationBackground(Color.white)
        .presentationDragIndicator(.visible)
        .interactiveDismissDisabled()
    }

    private func key(_ digit: String) -> some View {
        Button {
            guard model.apartmentInput.count < 3 else { return }
            model.apartmentInput += digit
        } label: {
            Text(digit).font(.title2.weight(.medium)).frame(maxWidth: .infinity, minHeight: 56)
        }
        .buttonStyle(.bordered)
        .accessibilityLabel(digit)
        .accessibilityIdentifier("apartment-key-\(digit)")
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
                        Text(Self.dateFormatter.string(from: session.updatedAt)).font(.headline)
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
