import Foundation
import SwiftUI

public struct CompletedCharge: Codable, Equatable, Identifiable, Sendable {
    public let id: UUID
    public let timestamp: Date
    public let sequence: RideSequence
    public let initialRides: UInt
    public let addedRides: Int
    public let totalRides: UInt
    public let amountEUR: Decimal

    public init(timestamp: Date, sequence: RideSequence, initialRides: UInt, addedRides: Int, totalRides: UInt, amountEUR: Decimal) {
        self.id = UUID()
        self.timestamp = timestamp
        self.sequence = sequence
        self.initialRides = initialRides
        self.addedRides = addedRides
        self.totalRides = totalRides
        self.amountEUR = amountEUR
    }
}

public protocol ChargeHistoryStore {
    func load() -> [CompletedCharge]
    func save(_ charges: [CompletedCharge])
}

public struct UserDefaultsChargeHistoryStore: ChargeHistoryStore {
    private let defaults: UserDefaults
    private let key: String
    public let limit: Int

    public init(defaults: UserDefaults = .standard, key: String = "completedChargeHistory", limit: Int = 100) {
        self.defaults = defaults
        self.key = key
        self.limit = limit
    }

    public func load() -> [CompletedCharge] {
        guard let data = defaults.data(forKey: key),
              let charges = try? JSONDecoder().decode([CompletedCharge].self, from: data) else { return [] }
        return Array(charges.prefix(limit))
    }

    public func save(_ charges: [CompletedCharge]) {
        guard let data = try? JSONEncoder().encode(Array(charges.prefix(limit))) else { return }
        defaults.set(data, forKey: key)
    }
}

@MainActor
public final class RidesViewModel: ObservableObject {
    public enum State: Equatable {
        case idle
        case detected
        case working(String)
        case known(Token)
        case unknown(UnknownToken)
        case noChip
        case failed(String)

        public var title: String {
            switch self {
            case .idle: "Ready"
            case .detected: "Ready to adjust"
            case .working(let operation): operation
            case .known: "Token loaded"
            case .unknown: "Unknown family"
            case .noChip: "No token"
            case .failed: "Action failed"
            }
        }
    }

    public enum RoundingTarget: Sendable {
        case pendingRides
        case costDelta
    }

    public static let noChipMessage = "No T55xx chip detected. Place the token on the reader and try again."
    public static let unknownMessage = "Unknown, logged"

    @Published public private(set) var state: State = .idle
    @Published public private(set) var lastTune: TuneOutcome?
    @Published public private(set) var lastSignalMillivolts: Int?
    @Published public private(set) var lastDumpURL: URL?
    @Published public private(set) var message: String?
    @Published public private(set) var currentRides: UInt = 0
    @Published public private(set) var pendingRides: UInt = 0
    @Published public private(set) var completedCharges: [CompletedCharge]
    @Published public private(set) var loadedToken: Token?
    @Published public var isResetSheetPresented = false
    @Published public var selectedResetSequence: RideSequence?
    @Published public private(set) var simulationScenario: SimulationScenario = .goodToken
    public enum CalibrationStep: Equatable { case noToken, placeToken, ready }
    @Published public private(set) var calibrationStep: CalibrationStep = .noToken
    @Published public private(set) var noTokenSignalMillivolts: Int?
    @Published public private(set) var tokenSignalMillivolts: Int?

    public let configuration: RidesConfiguration
    private let device: any RideTokenDevice
    private let legacyDevice: (any ProxmarkDevice)?
    private let dumpStore: UnknownDumpStore
    private let chargeHistoryStore: any ChargeHistoryStore

    public init(
        device: any RideTokenDevice = FakeProxmark(),
        configuration: RidesConfiguration = .prototype,
        dumpStore: UnknownDumpStore = UnknownDumpStore(),
        chargeHistoryStore: any ChargeHistoryStore = UserDefaultsChargeHistoryStore()
    ) {
        self.device = device
        self.legacyDevice = device as? any ProxmarkDevice
        self.configuration = configuration
        self.dumpStore = dumpStore
        self.chargeHistoryStore = chargeHistoryStore
        self.completedCharges = chargeHistoryStore.load()
        if let fake = device as? FakeProxmark {
            simulationScenario = fake.scenario
        }
    }

    public var isBusy: Bool {
        if case .working = state { return true }
        return false
    }

    public var usesSimulationScenarios: Bool { device is FakeProxmark }
    public var primaryActionTitle: String {
        switch calibrationStep { case .noToken: "Calibrate: NO token"; case .placeToken: "Calibrate: PLACE token"; case .ready: "Detect token" }
    }
    public var signalNormalizedFraction: Double? {
        guard let low = noTokenSignalMillivolts, let high = tokenSignalMillivolts, high != low,
              let signal = lastSignalMillivolts else { return nil }
        let fraction = Double(signal - low) / Double(high - low)
        return min(1, max(0, 0.1 + fraction * 0.8))
    }
    public var signalCalibrationReady: Bool { calibrationStep == .ready && noTokenSignalMillivolts != tokenSignalMillivolts }
    public var hasKnownToken: Bool { loadedToken != nil }
    public var hasRideChange: Bool { hasKnownToken && pendingRides != currentRides }
    public var canCharge: Bool { hasRideChange && canAdjust }
    public var canAdjust: Bool {
        guard hasKnownToken, !isBusy else { return false }
        if case .known = state { return true }
        return false
    }
    public var costEUR: Decimal {
        Decimal(Int(pendingRides) - Int(currentRides)) * configuration.pricePerRideEUR
    }

    public var costText: String {
        let number = NSDecimalNumber(decimal: costEUR)
        return number == .notANumber ? "€0.00" : String(format: "€%.2f", number.doubleValue)
    }

    public var aptBlock4Text: String {
        guard let loadedToken else { return "—" }
        return Token.hex(loadedToken.block4)
    }

    public func performPrimaryAction() async {
        guard !isBusy else { return }
        if calibrationStep == .ready { await detect(); return }
        state = .working(calibrationStep == .noToken ? "Measuring without token…" : "Measuring with token…")
        message = nil
        switch await device.measureCalibrationSignal() {
        case .failure(let error): state = .failed(error); message = error
        case .measured(let signal, let chipPresent):
            lastSignalMillivolts = signal
            lastTune = .measured(millivolts: signal)
            let expectsChip = calibrationStep == .placeToken
            guard chipPresent == expectsChip else {
                let error = expectsChip ? "No token detected. Place token and try again." : "Token detected. Remove token and try again."
                state = .failed(error); message = error; return
            }
            if calibrationStep == .noToken { noTokenSignalMillivolts = signal; calibrationStep = .placeToken }
            else {
                guard noTokenSignalMillivolts != signal else {
                    let error = "Calibration signals are equal. Adjust token placement and retry."
                    state = .failed(error); message = error; return
                }
                tokenSignalMillivolts = signal
                calibrationStep = .ready
            }
            state = .detected
            message = "Calibration sample recorded."
        }
    }

    public func detect() async {
        guard !isBusy else { return }
        resetForNewDetect()
        state = .working("Detecting, tuning and reading…")
        switch await device.scan() {
        case .known(let token, let signal):
            lastTune = .measured(millivolts: signal)
            lastSignalMillivolts = signal
            setKnown(token)
            message = "Token loaded."
        case .unknown(let token, let signal):
            lastTune = .measured(millivolts: signal)
            lastSignalMillivolts = signal
            logUnknown(token)
        case .noChip:
            state = .noChip
            message = Self.noChipMessage
        case .noChipWithSignal(let signal):
            lastTune = .measured(millivolts: signal)
            lastSignalMillivolts = signal
            state = .noChip
            message = Self.noChipMessage
        case .failure(let error):
            state = .failed(error)
            message = error
        }
    }

    // Kept as small compatibility actions for domain tests and future hardware screens.
    public func tune() async {
        guard !isBusy else { return }
        guard let legacyDevice else {
            state = .failed("Tune is unavailable for this reader.")
            message = "Tune is unavailable for this reader."
            return
        }
        state = .working("Tuning…")
        message = nil
        let outcome = await legacyDevice.tune()
        lastTune = outcome
        if case .measured(let signal) = outcome {
            lastSignalMillivolts = signal
            state = .detected
        } else if case .failure(let error) = outcome {
            state = .failed(error)
            message = error
        }
    }

    public func read() async {
        guard !isBusy else { return }
        guard let legacyDevice else {
            state = .failed("Read is unavailable for this reader.")
            message = "Read is unavailable for this reader."
            return
        }
        state = .working("Reading…")
        message = nil
        lastDumpURL = nil
        switch await legacyDevice.read() {
        case .known(let token): setKnown(token)
        case .noChip:
            state = .noChip
            message = Self.noChipMessage
        case .failure(let error):
            state = .failed(error)
            message = error
        case .unknown(let token):
            logUnknown(token)
        }
    }

    public func adjustRides(by amount: Int) {
        guard canAdjust else { return }
        pendingRides = clampedRides(Int(pendingRides) + amount)
    }

    public func clearPendingRides() {
        guard canAdjust else { return }
        pendingRides = 0
    }

    /// The cash controls are deliberately ride controls underneath: €1.50 is 50 rides.
    public func adjustCost(by amount: Decimal) {
        let rides = ((amount / configuration.pricePerRideEUR) as NSDecimalNumber).intValue
        adjustRides(by: rides)
    }

    public func round(_ target: RoundingTarget) {
        guard canAdjust else { return }
        switch target {
        case .pendingRides:
            pendingRides = nearestFifty(pendingRides)
        case .costDelta:
            let delta = Int(pendingRides) - Int(currentRides)
            let roundedDelta = nearestFiftySigned(delta)
            pendingRides = clampedRides(Int(currentRides) + roundedDelta)
        }
    }

    public func selectSimulation(_ scenario: SimulationScenario) {
        guard !isBusy, usesSimulationScenarios else { return }
        simulationScenario = scenario
        (device as? FakeProxmark)?.apply(scenario)
    }

    public func charge() async {
        guard canCharge, let token = loadedToken else { return }
        let chargedAt = Date()
        let initialRides = currentRides
        let desiredRides = pendingRides
        let addedRides = Int(desiredRides) - Int(initialRides)
        let sequence = token.sequence
        let amountEUR = Decimal(addedRides) * configuration.pricePerRideEUR
        state = .working("Charging…")
        message = nil
        let request = RideMirrorWriteRequest(token: token, desiredRides: desiredRides)
        switch await device.writeRideMirrors(request) {
        case .success:
            let updated = token.withRideCount(desiredRides)
            let charge = CompletedCharge(
                timestamp: chargedAt,
                sequence: sequence,
                initialRides: initialRides,
                addedRides: addedRides,
                totalRides: desiredRides,
                amountEUR: amountEUR
            )
            completedCharges.insert(charge, at: 0)
            completedCharges = Array(completedCharges.prefix(100))
            chargeHistoryStore.save(completedCharges)
            loadedToken = updated
            currentRides = desiredRides
            state = .known(updated)
            message = "Charge successful."
        case .failure(let error):
            state = .failed(error)
            message = error
        case .requiresRefresh(let error):
            clearLoadedTokenForRefresh()
            state = .failed(error)
            message = error
        }
    }

    public func openReset() {
        guard !isBusy else { return }
        selectedResetSequence = nil
        isResetSheetPresented = true
    }

    public var canConfirmReset: Bool {
        selectedResetSequence != nil && !isBusy
    }

    public func confirmReset() async {
        guard canConfirmReset, let sequence = selectedResetSequence else { return }
        state = .working("Resetting…")
        message = nil
        switch await device.reset(ResetMutationRequest(sequence: sequence)) {
        case .success(let token), .alreadyApplied(let token):
            loadedToken = token
            currentRides = min(token.rideCount, configuration.maxRides)
            pendingRides = currentRides
            state = .known(token)
            message = "Reset successful."
            isResetSheetPresented = false
        case .failure(let error):
            state = .failed(error)
            message = error
        case .requiresRefresh(let error):
            clearLoadedTokenForRefresh()
            state = .failed(error)
            message = error
            isResetSheetPresented = false
        }
    }

    private func logUnknown(_ token: UnknownToken) {
        do {
            lastDumpURL = try dumpStore.save(token)
            state = .unknown(token)
            message = Self.unknownMessage
        } catch {
            lastDumpURL = nil
            let detail = error.localizedDescription.isEmpty ? String(describing: error) : error.localizedDescription
            let failure = "Unknown token — log failed: \(detail)"
            state = .failed(failure)
            message = failure
        }
    }

    private func resetForNewDetect() {
        currentRides = 0
        pendingRides = 0
        loadedToken = nil
        lastTune = nil
        lastSignalMillivolts = nil
        lastDumpURL = nil
        message = nil
    }

    private func clearLoadedTokenForRefresh() {
        loadedToken = nil
        currentRides = 0
        pendingRides = 0
    }

    private func setKnown(_ token: Token) {
        loadedToken = token
        currentRides = min(token.rideCount, configuration.maxRides)
        pendingRides = currentRides
        state = .known(token)
    }

    private func clampedRides(_ value: Int) -> UInt {
        UInt(max(0, min(Int(configuration.maxRides), value)))
    }

    private func nearestFifty(_ value: UInt) -> UInt {
        clampedRides(((Int(value) + 25) / 50) * 50)
    }

    private func nearestFiftySigned(_ value: Int) -> Int {
        if value >= 0 { return ((value + 25) / 50) * 50 }
        return -(((-value + 25) / 50) * 50)
    }
}
