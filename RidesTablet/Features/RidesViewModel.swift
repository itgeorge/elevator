import Combine
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

    public init(defaults: UserDefaults = .standard, key: String = "completedChargeHistory", limit: Int = .max) {
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
        case calibrated
        case working(String)
        case known(Token)
        case unknown(UnknownToken)
        case noChip
        case failed(String)

        public var title: String {
            switch self {
            case .idle: "Ready"
            case .detected: "Ready to adjust"
            case .calibrated: "Calibration complete"
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
    @Published public private(set) var message: String? = "Remove all tokens, then calibrate"
    @Published public private(set) var currentRides: UInt = 0
    @Published public private(set) var pendingRides: UInt = 0
    @Published public private(set) var completedCharges: [CompletedCharge]
    @Published public private(set) var activeChargeSession: ChargeSession?
    @Published public private(set) var savedChargeSessions: [ChargeSession] = []
    @Published public var sessionError: String?
    @Published public private(set) var loadedToken: Token?
    @Published public var isResetSheetPresented = false
    @Published public var selectedResetSequence: RideSequence?
    @Published public var isApartmentSheetPresented = false
    @Published public var apartmentInput = ""
    @Published public private(set) var apartmentNumber: UInt8?
    @Published public private(set) var simulationScenario: SimulationScenario = .goodToken
    public enum CalibrationStep: Equatable { case noToken, placeToken, ready }
    @Published public private(set) var calibrationStep: CalibrationStep = .noToken
    @Published public private(set) var noTokenSignalMillivolts: Int?
    @Published public private(set) var tokenSignalMillivolts: Int?
    @Published public private(set) var isAutoScanning = false
    @Published public private(set) var isAutoScanCycleActive = false
    @Published public private(set) var isAutoDetecting = false
    @Published public private(set) var isAutoSettling = false
    @Published public private(set) var isAutoSignalBlocked = false
    @Published public private(set) var autoScanStatus = ""

    public let configuration: RidesConfiguration
    private let device: any RideTokenDevice
    private let legacyDevice: (any ProxmarkDevice)?
    private let dumpStore: UnknownDumpStore
    // Legacy history injection is retained for isolated existing tests only.
    private let chargeHistoryStore: (any ChargeHistoryStore)?
    private let chargeSessionStore: FileChargeSessionStore?
    private let apartmentSecretStore: ApartmentSecretStore
    private var apartmentSecretSubscription: AnyCancellable?
    private var editingApartment = false
    private var apartmentSnapshotReady = false
    private let autoScanInterval: TimeInterval
    private let occupiedAutoScanInterval: TimeInterval
    private let periodicConfirmInterval: TimeInterval
    private let now: () -> Date
    /// Resting-token tune noise measured 18 Nov 2026 is about 30 mV per 100 ms probe.
    /// A slow placement creep stays around 100–200 mV per probe, so 80 mV per sample
    /// accepts a settled token and withholds a read while it is still moving.
    private static let settleSlopeLimitMillivoltsPerSample = 80.0
    private var settleProbes: [Int] = []
    private var settleDeadline: Date?
    private var lastUnconfirmedAttemptAt: Date?
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
        chargeHistoryStore: (any ChargeHistoryStore)? = nil,
        sessionStore: FileChargeSessionStore? = nil,
        autoScanInterval: TimeInterval = 0.1,
        occupiedAutoScanInterval: TimeInterval = 3,
        periodicConfirmInterval: TimeInterval = 5,
        now: @escaping () -> Date = Date.init,
        apartmentSecretStore: ApartmentSecretStore? = nil
    ) {
        self.device = device
        self.legacyDevice = device as? any ProxmarkDevice
        self.configuration = configuration
        self.dumpStore = dumpStore
        self.chargeHistoryStore = chargeHistoryStore
        self.chargeSessionStore = sessionStore ?? (chargeHistoryStore == nil ? FileChargeSessionStore() : nil)
        self.autoScanInterval = max(0, autoScanInterval)
        self.occupiedAutoScanInterval = max(0, occupiedAutoScanInterval)
        self.periodicConfirmInterval = max(0, periodicConfirmInterval)
        self.now = now
        let apartmentSecretStore = apartmentSecretStore ?? .shared
        self.apartmentSecretStore = apartmentSecretStore
        self.completedCharges = chargeHistoryStore?.load() ?? []
        if let store = self.chargeSessionStore {
            do {
                let session = try store.loadActiveSession()
                self.activeChargeSession = session
                self.completedCharges = session.charges
                self.savedChargeSessions = try store.listSessions()
            } catch {
                self.sessionError = "Could not open charge session: \(error.localizedDescription)"
            }
        }
        apartmentSecretSubscription = apartmentSecretStore.$secret.sink { [weak self] secret in
            guard let self else { return }
            Task { @MainActor in await self.apartmentSecretDidChange(secret) }
        }
        if let fake = device as? FakeProxmark {
            simulationScenario = fake.scenario
        }
    }

    public func refreshChargeSessions() {
        guard let store = chargeSessionStore else { return }
        do { savedChargeSessions = try store.listSessions() }
        catch { sessionError = "Could not list charge sessions: \(error.localizedDescription)" }
    }

    public func startNewChargeSession() {
        guard !isBusy, let store = chargeSessionStore else { return }
        do {
            try persistActiveSession()
            let session = try store.createSession()
            activeChargeSession = session
            completedCharges = []
            sessionError = nil
            refreshChargeSessions()
        } catch { sessionError = "Could not start a new session: \(error.localizedDescription)" }
    }

    public func loadChargeSession(_ session: ChargeSession) {
        guard !isBusy, let store = chargeSessionStore else { return }
        do {
            try persistActiveSession()
            let loaded = try store.load(id: session.id)
            try store.setActiveSession(id: loaded.id)
            activeChargeSession = loaded
            completedCharges = loaded.charges
            sessionError = nil
            refreshChargeSessions()
        } catch { sessionError = "Could not load charge session: \(error.localizedDescription)" }
    }

    private func persistActiveSession() throws {
        guard let store = chargeSessionStore, var session = activeChargeSession else { return }
        session.charges = completedCharges
        session.updatedAt = Date()
        activeChargeSession = session
        try store.save(session)
    }

    private func saveCompletedCharges() {
        chargeHistoryStore?.save(completedCharges)
        do {
            try persistActiveSession()
            if let store = chargeSessionStore { savedChargeSessions = try store.listSessions() }
            sessionError = nil
        } catch {
            sessionError = "Token charge succeeded, but the session file could not be saved: \(error.localizedDescription). Do not repeat the charge."
        }
    }

    /// Poll cadence based on the latest confirmed presence read, not local token data.
    var currentAutoScanInterval: TimeInterval {
        isAutoSignalBlocked || isAutoSettling || autoConfirmationIsStale || !confirmedTokenPresence
            ? autoScanInterval : occupiedAutoScanInterval
    }

    public var isBusy: Bool {
        if mutationInProgress || isApartmentSheetPresented { return true }
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
    public var canCharge: Bool { hasRideChange && canAdjust && !autoConfirmationIsStale && (chargeSessionStore == nil || activeChargeSession != nil) && (apartmentSecretStore.secret == nil || apartmentSnapshotReady) }
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

    public var isEditingApartment: Bool { editingApartment }
    public var canSaveApartment: Bool { UInt8(apartmentInput.trimmingCharacters(in: .whitespacesAndNewlines)) != nil && loadedToken != nil && !mutationInProgress }

    private func updateApartment(secret: String?) {
        guard apartmentSnapshotReady, let secret, let token = loadedToken,
              let payload = ApartmentBlockCodec.decode(secret: secret, block3: token.blocks[3], block4: token.block4), payload.building == 0 else {
            apartmentNumber = nil
            return
        }
        apartmentNumber = payload.apartment
    }

    public func requestCharge() async {
        guard hasRideChange, canAdjust, !autoConfirmationIsStale else { return }
        if apartmentSecretStore.secret != nil && !apartmentSnapshotReady {
            guard await refreshForApartmentSecret() else { return }
        }
        guard canCharge else { return }
        if apartmentSecretStore.secret != nil && apartmentNumber == nil {
            editingApartment = false
            apartmentInput = ""
            isApartmentSheetPresented = true
            await pauseAutoScanningForMutation()
            return
        }
        await charge()
    }

    public func editApartment() async {
        guard canAdjust, apartmentSecretStore.secret != nil else { return }
        if !apartmentSnapshotReady, !(await refreshForApartmentSecret()) { return }
        guard canAdjust, apartmentSnapshotReady else { return }
        editingApartment = true
        apartmentInput = apartmentNumber.map(String.init) ?? ""
        isApartmentSheetPresented = true
        await pauseAutoScanningForMutation()
    }

    public func cancelApartmentEntry() async { isApartmentSheetPresented = false; resumeAutoScanningAfterMutation() }
    public func skipApartmentEntry() async { isApartmentSheetPresented = false; await charge() }
    public func saveApartmentEntry() async {
        guard canSaveApartment, apartmentSnapshotReady, let token = loadedToken, let secret = apartmentSecretStore.secret,
              let apartment = UInt8(apartmentInput.trimmingCharacters(in: .whitespacesAndNewlines)) else { return }
        await performApartmentWrite(token: token, secret: secret, apartment: apartment, includeCharge: !editingApartment)
    }

    private func apartmentSecretDidChange(_ secret: String?) async {
        apartmentSnapshotReady = false
        apartmentNumber = nil
        guard secret != nil, loadedToken != nil else { return }
        _ = await refreshForApartmentSecret()
    }

    /// Re-read an already loaded token using the bridge's targeted block-3 path.
    /// Only a matching 4/5/6 snapshot preserves the operator's pending ride choice.
    private func refreshForApartmentSecret() async -> Bool {
        guard loadedToken != nil, !mutationInProgress else { return false }
        mutationInProgress = true
        await pauseAutoScanningForMutation()
        let before = loadedToken
        let result = await device.scanForApartmentSnapshot()
        var unchanged = false
        switch result {
        case .known(let fresh, let signal):
            if let before, before.block4 == fresh.block4, before.block5 == fresh.block5, before.block6 == fresh.block6 {
                var blocks = before.blocks
                blocks[3] = fresh.blocks[3]
                let upgraded = Token(blocks: blocks, rideCount: before.rideCount, sequence: before.sequence)
                loadedToken = upgraded
                apartmentSnapshotReady = true
                updateApartment(secret: apartmentSecretStore.secret)
                state = .known(upgraded)
                lastTune = .measured(millivolts: signal)
                lastSignalMillivolts = signal
                unchanged = true
            } else {
                setKnown(fresh)
                apartmentSnapshotReady = true
                updateApartment(secret: apartmentSecretStore.secret)
                message = "Token changed while apartment details were refreshed. Review rides and try again."
            }
        case .unknown, .noChip, .noChipWithSignal:
            apartmentSnapshotReady = false
            apartmentNumber = nil
            message = "Token changed or could not be read. Tap Detect before continuing."
        case .failure(let error):
            apartmentSnapshotReady = false
            apartmentNumber = nil
            message = "Apartment refresh failed: \(error)"
        }
        mutationInProgress = false
        resumeAutoScanningAfterMutation()
        return unchanged
    }


    private func performApartmentWrite(token: Token, secret: String, apartment: UInt8, includeCharge: Bool) async {
        mutationInProgress = true
        await pauseAutoScanningForMutation()
        let aptBlock = ApartmentBlockCodec.encode(secret: secret, block3: token.blocks[3], apartment: apartment)
        let desired = includeCharge ? pendingRides : nil
        let chargeAt = Date(), initial = currentRides
        state = .working(includeCharge ? "Charging…" : "Saving apartment…")
        let outcome = await device.writeApartment(ApartmentMutationRequest(token: token, apartmentBlock: aptBlock, desiredRides: desired))
        switch outcome {
        case .success:
            var newBlocks = token.blocks; newBlocks[4] = aptBlock
            if let desired, let encoded = token.sequence.encode(desired) { newBlocks[5] = encoded; newBlocks[6] = encoded }
            let updated = Token(blocks: newBlocks, rideCount: desired ?? token.rideCount, sequence: token.sequence)
            loadedToken = updated; apartmentNumber = apartment; isApartmentSheetPresented = false
            if includeCharge {
                let delta = Int(pendingRides) - Int(initial)
                completedCharges.insert(CompletedCharge(timestamp: chargeAt, sequence: token.sequence, initialRides: initial, addedRides: delta, totalRides: pendingRides, amountEUR: Decimal(delta) * configuration.pricePerRideEUR), at: 0)
                saveCompletedCharges()
                currentRides = pendingRides
            }
            state = .known(updated); message = includeCharge ? "Charge successful." : "Apartment saved."
            if mutationNeedsPostWriteRefresh { await refreshAfterMutation() }
        case .failure(let error): state = .failed(error); message = error
        case .requiresRefresh(let error): clearLoadedTokenForRefresh(); state = .failed(error); message = error
        }
        mutationInProgress = false; resumeAutoScanningAfterMutation()
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
        isAutoSettling = false
        clearSettleWindow()
        lastUnconfirmedAttemptAt = nil
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
                isAutoSettling = false
                stableEdgeProbes = 0
                clearSettleWindow()
                lastUnconfirmedAttemptAt = nil
                autoConfirmationIsStale = true
                autoScanStatus = "Weak signal — move token into calibrated range."
                return
            }
            if isAutoSignalBlocked {
                isAutoSignalBlocked = false
                autoConfirmationIsStale = true
                lastFullScanAt = nil
                clearSettleWindow()
                lastUnconfirmedAttemptAt = nil
                autoScanStatus = "Signal recovered — waiting for signal to settle."
            }

            let initialOrUnconfirmed = lastFullScanAt == nil || autoConfirmationIsStale || !confirmedTokenPresence
            if initialOrUnconfirmed {
                settleProbes.append(signal)
                if settleProbes.count > 4 { settleProbes.removeFirst(settleProbes.count - 4) }
                if quality >= 0.5 && settleDeadline == nil {
                    settleDeadline = now().addingTimeInterval(periodicConfirmInterval)
                }
                isAutoSettling = true
                autoScanStatus = "Waiting for signal to settle."
                let settled = Self.settleSlopeIsFlat(settleProbes)
                let deadlineReached = settleDeadline.map { now() >= $0 } == true
                let retryDue = lastUnconfirmedAttemptAt.map { now().timeIntervalSince($0) >= periodicConfirmInterval } == true
                let attemptDue = lastUnconfirmedAttemptAt == nil || retryDue
                if attemptDue && (settled || deadlineReached) {
                    isAutoSettling = false
                    stableEdgeProbes = 0
                    await performAutoConfirmation(signal: signal, generation: generation)
                }
                return
            }

            isAutoSettling = false
            let edge = isMeaningfulSignalEdge(signal)
            stableEdgeProbes = edge ? stableEdgeProbes + 1 : 0
            isAutoDetecting = false
            if stableEdgeProbes >= 2 {
                stableEdgeProbes = 0
                await performAutoConfirmation(signal: signal, generation: generation)
            }
        case .failure(let error):
            stableEdgeProbes = 0
            isAutoDetecting = false
            isAutoSettling = false
            clearSettleWindow()
            autoFailureCount += 1
            autoScanStatus = "Signal check failed: \(error). Retrying."
        }
    }

    private var calibrationSpan: Int {
        abs((tokenSignalMillivolts ?? 0) - (noTokenSignalMillivolts ?? 0))
    }

    private func clearSettleWindow() {
        settleProbes.removeAll()
        settleDeadline = nil
    }

    /// Least-squares slope of four equally spaced probes, in millivolts per sample.
    /// For samples y0…y3 the slope is (-3y0 - y1 + y2 + 3y3) / 10.
    static func settleSlopeIsFlat(_ samples: [Int]) -> Bool {
        guard samples.count >= 4 else { return false }
        let recent = Array(samples.suffix(4))
        let slope = (-3 * Double(recent[0]) - Double(recent[1]) + Double(recent[2]) + 3 * Double(recent[3])) / 10
        return abs(slope) <= settleSlopeLimitMillivoltsPerSample
    }

    private func signalQuality(_ signal: Int) -> Double {
        guard let empty = noTokenSignalMillivolts, let present = tokenSignalMillivolts,
              empty != present else { return 0 }
        return Double(signal - empty) / Double(present - empty)
    }

    private func isMeaningfulSignalEdge(_ signal: Int) -> Bool {
        guard let baseline = lastConfirmedSignal else { return false }
        let calibratedSpan = calibrationSpan
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
        let hadArmedDeadline = settleDeadline != nil
        let attemptStartedAt = now()
        clearSettleWindow()
        lastUnconfirmedAttemptAt = attemptStartedAt
        let worseningProbe = isMeaningfulSignalEdge(signal)
        let shouldShowDetecting = loadedToken == nil || autoConfirmationIsStale
        if shouldShowDetecting { isAutoDetecting = true }
        defer { if shouldShowDetecting { isAutoDetecting = false } }
        let result = await device.scan()
        guard isAutoScanning, generation == cycleGeneration else { return }
        lastFullScanAt = now()
        switch result {
        case .known(let token, let measuredSignal):
            confirmedTokenPresence = true
            lastTune = .measured(millivolts: measuredSignal)
            lastSignalMillivolts = measuredSignal
            lastConfirmedSignal = worseningProbe ? signal : measuredSignal
            lastPresentSignal = measuredSignal
            lastConfirmedUnknownBlocks = nil
            autoConfirmationIsStale = false
            settleDeadline = nil
            lastUnconfirmedAttemptAt = nil
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
            settleDeadline = nil
            lastUnconfirmedAttemptAt = nil
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
            autoConfirmationIsStale = true
            lastUnconfirmedAttemptAt = attemptStartedAt
            settleDeadline = hadArmedDeadline ? attemptStartedAt.addingTimeInterval(periodicConfirmInterval) : nil
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
            autoConfirmationIsStale = true
            lastUnconfirmedAttemptAt = attemptStartedAt
            settleDeadline = hadArmedDeadline ? attemptStartedAt.addingTimeInterval(periodicConfirmInterval) : nil
            clearLoadedTokenForRefresh()
            loadedToken = nil
            state = .noChip
            message = Self.noChipMessage
            autoScanStatus = "No token confirmed."
        case .failure(let error):
            autoFailureCount += 1
            autoConfirmationIsStale = true
            lastUnconfirmedAttemptAt = attemptStartedAt
            settleDeadline = hadArmedDeadline ? attemptStartedAt.addingTimeInterval(periodicConfirmInterval) : nil
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
        guard isAutoScanning else { return }
        autoWasPausedForMutation = true
        mutationNeedsPostWriteRefresh = true
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

    public func restartCalibration() async {
        guard !isBusy else { return }
        await stopAndDrainAutoScanning()
        guard !isBusy else { return }
        noTokenSignalMillivolts = nil
        tokenSignalMillivolts = nil
        calibrationStep = .noToken
        loadedToken = nil
        apartmentNumber = nil
        currentRides = 0
        pendingRides = 0
        lastSignalMillivolts = nil
        lastTune = nil
        lastDumpURL = nil
        confirmedTokenPresence = false
        lastFullScanAt = nil
        lastConfirmedSignal = nil
        lastEmptySignal = nil
        lastPresentSignal = nil
        lastConfirmedUnknownBlocks = nil
        stableEdgeProbes = 0
        autoConfirmationIsStale = false
        autoFailureCount = 0
        isAutoSignalBlocked = false
        isAutoSettling = false
        isAutoDetecting = false
        clearSettleWindow()
        lastUnconfirmedAttemptAt = nil
        state = .idle
        message = "Remove all tokens, then calibrate"
        autoScanStatus = ""
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
            if loadedToken == nil {
                if calibrationStep == .ready {
                    state = .calibrated
                    message = "Tap Detect or enable Auto."
                } else {
                    state = .idle
                    message = calibrationStep == .placeToken ? "Place token" : "Remove all tokens, then calibrate"
                }
            } else {
                message = "Calibration sample recorded."
            }
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
            lastFullScanAt = now()
            lastConfirmedSignal = signal
            lastPresentSignal = signal
            lastTune = .measured(millivolts: signal)
            lastSignalMillivolts = signal
            setKnown(token)
            message = "Token loaded."
        case .unknown(let token, let signal):
            confirmedTokenPresence = true
            lastFullScanAt = now()
            lastConfirmedSignal = signal
            lastPresentSignal = signal
            lastTune = .measured(millivolts: signal)
            lastSignalMillivolts = signal
            logUnknown(token)
        case .noChip:
            confirmedTokenPresence = false
            lastFullScanAt = now()
            lastConfirmedSignal = lastSignalMillivolts
            lastEmptySignal = lastSignalMillivolts
            state = .noChip
            message = Self.noChipMessage
        case .noChipWithSignal(let signal):
            confirmedTokenPresence = false
            lastFullScanAt = now()
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
        isAutoSettling = false
        clearSettleWindow()
        lastUnconfirmedAttemptAt = nil
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
        pendingRides = currentRides
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
        guard canCharge, apartmentSecretStore.secret == nil || apartmentSnapshotReady else { return }
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
            saveCompletedCharges()
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
            updateApartment(secret: apartmentSecretStore.secret)
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
        defer { lastFullScanAt = now() }
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
        clearSettleWindow()
        isAutoSettling = false
        lastUnconfirmedAttemptAt = nil
        lastFullScanAt = nil
        lastConfirmedSignal = nil
        lastEmptySignal = nil
        lastPresentSignal = nil
        stableEdgeProbes = 0
        currentRides = 0
        pendingRides = 0
        loadedToken = nil
        apartmentSnapshotReady = false
        apartmentNumber = nil
        lastTune = nil
        lastSignalMillivolts = nil
        lastDumpURL = nil
        message = nil
    }

    private func clearLoadedTokenForRefresh() {
        loadedToken = nil
        apartmentSnapshotReady = false
        apartmentNumber = nil
        currentRides = 0
        pendingRides = 0
    }

    private func setKnown(_ token: Token) {
        apartmentSnapshotReady = apartmentSecretStore.secret != nil
        autoConfirmationIsStale = false
        loadedToken = token
        updateApartment(secret: apartmentSecretStore.secret)
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
