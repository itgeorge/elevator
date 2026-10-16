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
    @Published public private(set) var isAutoScanning = false
    @Published public private(set) var isAutoScanCycleActive = false
    @Published public private(set) var isAutoDetecting = false
    @Published public private(set) var isAutoSignalBlocked = false
    @Published public private(set) var autoScanStatus = ""

    public let configuration: RidesConfiguration
    private let device: any RideTokenDevice
    private let legacyDevice: (any ProxmarkDevice)?
    private let dumpStore: UnknownDumpStore
    private let chargeHistoryStore: any ChargeHistoryStore
    private let autoScanInterval: TimeInterval
    private let occupiedAutoScanInterval: TimeInterval
    private let periodicConfirmInterval: TimeInterval
    private var confirmedTokenPresence = false
    private var autoScanTask: Task<Void, Never>?
    private var drainingAutoScanTask: Task<Void, Never>?
    private var cycleGeneration = 0
    private var lastFullScanAt: Date?
    private var lastConfirmedSignal: Int?
    private var lastEmptySignal: Int?
    private var lastPresentSignal: Int?
    private var lastConfirmedUnknownBlocks: [UInt32]?
    private var stableEdgeProbes = 0
    private var autoWasPausedForMutation = false
    private var mutationNeedsPostWriteRefresh = false
    private var mutationInProgress = false
    private var autoFailureCount = 0
    private var autoConfirmationIsStale = false

    public init(
        device: any RideTokenDevice = FakeProxmark(),
        configuration: RidesConfiguration = .prototype,
        dumpStore: UnknownDumpStore = UnknownDumpStore(),
        chargeHistoryStore: any ChargeHistoryStore = UserDefaultsChargeHistoryStore(),
        autoScanInterval: TimeInterval = 0.75,
        occupiedAutoScanInterval: TimeInterval = 3,
        periodicConfirmInterval: TimeInterval = 8
    ) {
        self.device = device
        self.legacyDevice = device as? any ProxmarkDevice
        self.configuration = configuration
        self.dumpStore = dumpStore
        self.chargeHistoryStore = chargeHistoryStore
        self.autoScanInterval = max(0, autoScanInterval)
        self.occupiedAutoScanInterval = max(0, occupiedAutoScanInterval)
        self.periodicConfirmInterval = max(0, periodicConfirmInterval)
        self.completedCharges = chargeHistoryStore.load()
        if let fake = device as? FakeProxmark {
            simulationScenario = fake.scenario
        }
    }

    /// Poll cadence based on the latest confirmed presence read, not local token data.
    var currentAutoScanInterval: TimeInterval {
        isAutoSignalBlocked ? autoScanInterval : (confirmedTokenPresence ? occupiedAutoScanInterval : autoScanInterval)
    }

    public var isBusy: Bool {
        if mutationInProgress { return true }
        if case .working = state { return true }
        return false
    }

    public var usesSimulationScenarios: Bool { device is FakeProxmark }
    public var primaryActionTitle: String {
        if isAutoScanning || hasKnownToken || calibrationStep == .ready { return "Detect" }
        return switch calibrationStep { case .noToken: "Calibrate: NO token"; case .placeToken: "Calibrate: PLACE token"; case .ready: "Detect" }
    }
    public var signalNormalizedFraction: Double? {
        guard let low = noTokenSignalMillivolts, let high = tokenSignalMillivolts, high != low,
              let signal = lastSignalMillivolts else { return nil }
        let fraction = Double(signal - low) / Double(high - low)
        return min(1, max(0, 0.1 + fraction * 0.8))
    }
    public var signalCalibrationReady: Bool { calibrationStep == .ready && noTokenSignalMillivolts != tokenSignalMillivolts }
    public var canEnableAutoScanning: Bool { signalCalibrationReady && (!isBusy || isAutoScanning) }
    public var hasKnownToken: Bool { loadedToken != nil }
    public var hasRideChange: Bool { hasKnownToken && pendingRides != currentRides }
    public var canCharge: Bool { hasRideChange && canAdjust && !autoConfirmationIsStale }
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

    public func toggleAutoScanning() {
        if isAutoScanning { stopAutoScanning() } else { startAutoScanning() }
    }

    public func stopAutoScanning() {
        isAutoScanning = false
        isAutoDetecting = false
        cycleGeneration += 1
        autoScanStatus = "Auto scan stopped."
        autoWasPausedForMutation = false
        autoScanTask?.cancel()
        let task = autoScanTask
        drainingAutoScanTask = task
        Task { await task?.value }
    }

    private func startAutoScanning() {
        guard canEnableAutoScanning, !isAutoScanning else { return }
        isAutoScanning = true
        cycleGeneration += 1
        let generation = cycleGeneration
        autoScanStatus = "Auto scan active."
        let previousTask = drainingAutoScanTask
        drainingAutoScanTask = nil
        autoScanTask = Task { [weak self] in
            await previousTask?.value
            while !Task.isCancelled {
                let delay: TimeInterval? = await {
                    guard let self, self.isAutoScanning, self.cycleGeneration == generation else { return nil }
                    let started = Date()
                    await self.runAutoScanCycle(generation: generation)
                    guard !Task.isCancelled, self.isAutoScanning, self.cycleGeneration == generation else { return nil }
                    let elapsed = Date().timeIntervalSince(started)
                    return max(0, self.currentAutoScanInterval * pow(2.0, Double(min(self.autoFailureCount, 4))) - elapsed)
                }()
                guard let delay else { break }
                if delay > 0 { try? await Task.sleep(nanoseconds: UInt64(delay * 1_000_000_000)) }
            }
        }
    }

    /// One deterministic polling iteration; useful for tests and never overlaps another cycle.
    public func runAutoScanCycleForTesting() async { await runAutoScanCycle(generation: cycleGeneration) }

    private func runAutoScanCycle(generation: Int) async {
        guard isAutoScanning, generation == cycleGeneration, !isAutoScanCycleActive, !isBusy else { return }
        isAutoScanCycleActive = true
        defer { isAutoScanCycleActive = false }
        let outcome = await device.measureSignal()
        guard isAutoScanning, generation == cycleGeneration else { return }
        switch outcome {
        case .measured(let signal):
            autoFailureCount = 0
            lastTune = outcome
            lastSignalMillivolts = signal
            autoScanStatus = "Signal \(signal) mV"
            let quality = signalQuality(signal)
            if isAutoSignalBlocked ? quality < 0.20 : quality <= 0.15 {
                isAutoSignalBlocked = true
                isAutoDetecting = false
                stableEdgeProbes = 0
                autoConfirmationIsStale = true
                autoScanStatus = "Weak signal — move token into calibrated range."
                return
            }
            if isAutoSignalBlocked {
                isAutoSignalBlocked = false
                autoConfirmationIsStale = true
                lastFullScanAt = nil
                isAutoDetecting = true
                autoScanStatus = "Signal recovered — confirming token."
            }
            let initialScan = lastFullScanAt == nil
            let edge = isMeaningfulSignalEdge(signal)
            let retry = autoConfirmationIsStale
            stableEdgeProbes = (edge || retry || initialScan) ? stableEdgeProbes + 1 : 0
            if loadedToken == nil && !confirmedTokenPresence && edge {
                isAutoDetecting = true
            } else if stableEdgeProbes == 0 && !retry {
                isAutoDetecting = false
            }
            let periodicEmptyCheck = !confirmedTokenPresence && !initialScan
                && lastFullScanAt.map { Date().timeIntervalSince($0) >= periodicConfirmInterval } == true
            if initialScan || stableEdgeProbes >= 2 || periodicEmptyCheck {
                stableEdgeProbes = 0
                await performAutoConfirmation(signal: signal, generation: generation)
            }
        case .failure(let error):
            stableEdgeProbes = 0
            isAutoDetecting = false
            autoFailureCount += 1
            autoScanStatus = "Signal check failed: \(error). Retrying."
        }
    }

    private func signalQuality(_ signal: Int) -> Double {
        guard let empty = noTokenSignalMillivolts, let present = tokenSignalMillivolts,
              empty != present else { return 0 }
        return Double(signal - empty) / Double(present - empty)
    }

    private func isMeaningfulSignalEdge(_ signal: Int) -> Bool {
        guard let baseline = lastConfirmedSignal else { return false }
        let calibratedSpan = abs((tokenSignalMillivolts ?? 0) - (noTokenSignalMillivolts ?? 0))
        let threshold = max(1_000, calibratedSpan == 0 ? 1_500 : calibratedSpan * 15 / 100)
        let delta = signal - baseline
        guard confirmedTokenPresence else { return abs(delta) >= threshold }
        guard let direction = tokenSignalDirection else { return false }
        return delta * direction <= -threshold
    }

    private var tokenSignalDirection: Int? {
        if let empty = noTokenSignalMillivolts, let present = tokenSignalMillivolts, empty != present {
            return present > empty ? 1 : -1
        }
        if let empty = lastEmptySignal, let present = lastPresentSignal, empty != present {
            return present > empty ? 1 : -1
        }
        return -1 // Native readers typically report a lower raw value with a token present.
    }

    private func performAutoConfirmation(signal: Int, generation: Int) async {
        guard signalQuality(signal) > 0.15 else { return }
        let shouldShowDetecting = loadedToken == nil || autoConfirmationIsStale
        if shouldShowDetecting { isAutoDetecting = true }
        defer { if shouldShowDetecting { isAutoDetecting = false } }
        let worseningProbe = isMeaningfulSignalEdge(signal)
        let result = await device.scan()
        guard isAutoScanning, generation == cycleGeneration else { return }
        lastFullScanAt = Date()
        switch result {
        case .known(let token, let measuredSignal):
            confirmedTokenPresence = true
            lastTune = .measured(millivolts: measuredSignal)
            lastSignalMillivolts = measuredSignal
            lastConfirmedSignal = worseningProbe ? signal : measuredSignal
            lastPresentSignal = measuredSignal
            lastConfirmedUnknownBlocks = nil
            autoConfirmationIsStale = false
            adoptConfirmed(token)
            autoFailureCount = 0
            autoScanStatus = "Token confirmed."
        case .unknown(let token, let measuredSignal):
            confirmedTokenPresence = true
            lastTune = .measured(millivolts: measuredSignal)
            lastSignalMillivolts = measuredSignal
            lastConfirmedSignal = worseningProbe ? signal : measuredSignal
            lastPresentSignal = measuredSignal
            autoConfirmationIsStale = false
            let sameUnknown = lastConfirmedUnknownBlocks == token.blocks
                && loadedToken == nil
                && ({ if case .unknown(let old) = state { return old.blocks == token.blocks }; return false }())
            if !sameUnknown {
                lastConfirmedUnknownBlocks = token.blocks
                loadedToken = nil
                clearLoadedTokenForRefresh()
                logUnknown(token)
            }
            autoScanStatus = "Unknown token confirmed."
        case .noChip:
            confirmedTokenPresence = false
            lastConfirmedSignal = signal
            lastEmptySignal = signal
            lastConfirmedUnknownBlocks = nil
            autoConfirmationIsStale = false
            clearLoadedTokenForRefresh()
            loadedToken = nil
            state = .noChip
            message = Self.noChipMessage
            autoScanStatus = "No token confirmed."
        case .noChipWithSignal(let measuredSignal):
            confirmedTokenPresence = false
            lastTune = .measured(millivolts: measuredSignal)
            lastSignalMillivolts = measuredSignal
            lastConfirmedSignal = measuredSignal
            lastEmptySignal = measuredSignal
            lastConfirmedUnknownBlocks = nil
            autoConfirmationIsStale = false
            clearLoadedTokenForRefresh()
            loadedToken = nil
            state = .noChip
            message = Self.noChipMessage
            autoScanStatus = "No token confirmed."
        case .failure(let error):
            autoFailureCount += 1
            autoConfirmationIsStale = true
            autoScanStatus = "Confirmation failed: \(error). Retrying."
        }
    }

    private func adoptConfirmed(_ token: Token) {
        if let old = loadedToken, old.block4 == token.block4, old.block5 == token.block5,
           old.block6 == token.block6, old.sequence == token.sequence, old.rideCount == token.rideCount {
            return
        }
        setKnown(token)
        message = "Token loaded."
    }

    private func pauseAutoScanningForMutation() async {
        autoWasPausedForMutation = isAutoScanning
        mutationNeedsPostWriteRefresh = isAutoScanning
        guard isAutoScanning else { return }
        isAutoScanning = false
        cycleGeneration += 1
        autoScanTask?.cancel()
        let task = autoScanTask ?? drainingAutoScanTask
        autoScanTask = nil
        drainingAutoScanTask = nil
        await task?.value
    }

    private func resumeAutoScanningAfterMutation() {
        let shouldResume = autoWasPausedForMutation
        autoWasPausedForMutation = false
        mutationNeedsPostWriteRefresh = false
        if shouldResume { startAutoScanning() }
    }

    public func performPrimaryAction() async {
        let shouldForceScan = isAutoScanning || hasKnownToken
        await stopAndDrainAutoScanning()
        guard !isBusy else { return }
        if calibrationStep == .ready || shouldForceScan { await detect(); return }
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
            if loadedToken == nil { state = .detected }
            message = "Calibration sample recorded."
        }
    }

    public func detect() async {
        await stopAndDrainAutoScanning()
        guard !isBusy else { return }
        isAutoDetecting = true
        defer { isAutoDetecting = false }
        resetForNewDetect()
        state = .working("Detecting, tuning and reading…")
        switch await device.scan() {
        case .known(let token, let signal):
            confirmedTokenPresence = true
            lastFullScanAt = Date()
            lastConfirmedSignal = signal
            lastPresentSignal = signal
            lastTune = .measured(millivolts: signal)
            lastSignalMillivolts = signal
            setKnown(token)
            message = "Token loaded."
        case .unknown(let token, let signal):
            confirmedTokenPresence = true
            lastFullScanAt = Date()
            lastConfirmedSignal = signal
            lastPresentSignal = signal
            lastTune = .measured(millivolts: signal)
            lastSignalMillivolts = signal
            logUnknown(token)
        case .noChip:
            confirmedTokenPresence = false
            lastFullScanAt = Date()
            lastConfirmedSignal = lastSignalMillivolts
            lastEmptySignal = lastSignalMillivolts
            state = .noChip
            message = Self.noChipMessage
        case .noChipWithSignal(let signal):
            confirmedTokenPresence = false
            lastFullScanAt = Date()
            lastConfirmedSignal = signal
            lastEmptySignal = signal
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
    private func stopAndDrainAutoScanning() async {
        autoWasPausedForMutation = false
        isAutoScanning = false
        isAutoDetecting = false
        cycleGeneration += 1
        autoScanTask?.cancel()
        let task = autoScanTask ?? drainingAutoScanTask
        autoScanTask = nil
        drainingAutoScanTask = nil
        await task?.value
        autoScanStatus = "Auto scan stopped."
    }

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
        case .known(let token):
            confirmedTokenPresence = true
            setKnown(token)
        case .noChip:
            confirmedTokenPresence = false
            state = .noChip
            message = Self.noChipMessage
        case .failure(let error):
            state = .failed(error)
            message = error
        case .unknown(let token):
            confirmedTokenPresence = true
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
        guard canCharge else { return }
        mutationInProgress = true
        await pauseAutoScanningForMutation()
        guard hasRideChange, let token = loadedToken else {
            mutationInProgress = false
            resumeAutoScanningAfterMutation()
            return
        }
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
            if mutationNeedsPostWriteRefresh { await refreshAfterMutation() }
        case .failure(let error):
            state = .failed(error)
            message = error
        case .requiresRefresh(let error):
            clearLoadedTokenForRefresh()
            state = .failed(error)
            message = error
        }
        mutationInProgress = false
        resumeAutoScanningAfterMutation()
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
        guard canConfirmReset else { return }
        mutationInProgress = true
        await pauseAutoScanningForMutation()
        guard let sequence = selectedResetSequence else {
            mutationInProgress = false
            resumeAutoScanningAfterMutation()
            return
        }
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
            if mutationNeedsPostWriteRefresh { await refreshAfterMutation() }
        case .failure(let error):
            state = .failed(error)
            message = error
        case .requiresRefresh(let error):
            clearLoadedTokenForRefresh()
            state = .failed(error)
            message = error
            isResetSheetPresented = false
        }
        mutationInProgress = false
        resumeAutoScanningAfterMutation()
    }

    private func refreshAfterMutation() async {
        defer { lastFullScanAt = Date() }
        switch await device.scan() {
        case .known(let token, let signal):
            confirmedTokenPresence = true
            lastTune = .measured(millivolts: signal)
            lastSignalMillivolts = signal
            adoptConfirmed(token)
        case .unknown(let token, let signal):
            confirmedTokenPresence = true
            lastTune = .measured(millivolts: signal)
            lastSignalMillivolts = signal
            clearLoadedTokenForRefresh()
            logUnknown(token)
        case .noChip, .noChipWithSignal:
            confirmedTokenPresence = false
            clearLoadedTokenForRefresh()
            loadedToken = nil
            state = .noChip
            message = Self.noChipMessage
        case .failure(let error):
            // The mutation already succeeded; retain its authoritative local result and surface refresh trouble.
            autoScanStatus = "Post-write confirmation failed: \(error). Retrying."
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
        isAutoSignalBlocked = false
        autoConfirmationIsStale = false
        lastFullScanAt = nil
        lastConfirmedSignal = nil
        lastEmptySignal = nil
        lastPresentSignal = nil
        stableEdgeProbes = 0
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
        autoConfirmationIsStale = false
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
