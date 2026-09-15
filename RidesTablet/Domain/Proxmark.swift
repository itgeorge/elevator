import Foundation

public enum DetectOutcome: Equatable, Sendable {
    case detected
    case noChip
    case failure(String)
}

public enum TuneOutcome: Equatable, Sendable {
    case measured(millivolts: Int)
    case failure(String)
}

public enum ReadOutcome: Equatable, Sendable {
    case known(Token)
    case unknown(UnknownToken)
    case noChip
    case failure(String)
}

/// The result of the operator-facing detect button. A real adapter can replace this
/// with one hardware transaction; the fake keeps the three phases deterministic.
public enum DetectTuneReadOutcome: Equatable, Sendable {
    case known(Token, signalMillivolts: Int)
    case unknown(UnknownToken, signalMillivolts: Int)
    case noChip
    case failure(String)
}

public enum WriteOutcome: Equatable, Sendable {
    case success
    case failure(String)
}

public enum OverwriteOutcome: Equatable, Sendable {
    case success
    case failure(String)
}

public enum SimulationScenario: String, CaseIterable, Identifiable, Sendable {
    case goodToken = "Good token"
    case weakSignal = "Weak signal"
    case noChip = "No chip"
    case unknownFamily = "Unknown family"
    case writeFailure = "Write failure"

    public var id: String { rawValue }
}

/// Hardware boundary for the prototype. The real Proxmark adapter can replace this later.
public protocol ProxmarkDevice: Sendable {
    func detect() async -> DetectOutcome
    func tune() async -> TuneOutcome
    func read() async -> ReadOutcome
    func detectTuneRead() async -> DetectTuneReadOutcome
    func write(_ token: Token) async -> WriteOutcome
    func overwrite(_ image: [UInt32]) async -> OverwriteOutcome
}

public extension ProxmarkDevice {
    /// Compatibility implementation for adapters that have not yet added a single
    /// hardware transaction. The view model still exposes this as one atomic flow.
    func detectTuneRead() async -> DetectTuneReadOutcome {
        switch await detect() {
        case .noChip: return .noChip
        case .failure(let error): return .failure(error)
        case .detected: break
        }
        let signal: Int
        switch await tune() {
        case .measured(let millivolts): signal = millivolts
        case .failure(let error): return .failure(error)
        }
        switch await read() {
        case .known(let token): return .known(token, signalMillivolts: signal)
        case .unknown(let token): return .unknown(token, signalMillivolts: signal)
        case .noChip: return .noChip
        case .failure(let error): return .failure(error)
        }
    }
}

/// Deterministic async fake used by the simulator and unit tests.
public final class FakeProxmark: ProxmarkDevice, @unchecked Sendable {
    private let lock = NSLock()
    private var storedDetect: DetectOutcome
    private var storedTune: TuneOutcome
    private var storedRead: ReadOutcome
    private var storedWrite: WriteOutcome
    private var storedOverwrite: OverwriteOutcome
    private var storedScenario: SimulationScenario
    private(set) public var detectCallCount = 0
    private(set) public var tuneCallCount = 0
    private(set) public var readCallCount = 0
    private(set) public var writeCallCount = 0
    private(set) public var overwriteCallCount = 0
    private(set) public var lastWrittenToken: Token?
    private(set) public var lastOverwrittenImage: [UInt32]?

    public init(
        detect: DetectOutcome = .detected,
        tune: TuneOutcome = .measured(millivolts: 420),
        read: ReadOutcome = .known(.sample()),
        write: WriteOutcome = .success,
        overwrite: OverwriteOutcome = .success
    ) {
        storedDetect = detect
        storedTune = tune
        storedRead = read
        storedWrite = write
        storedOverwrite = overwrite
        storedScenario = .goodToken
    }

    public var scenario: SimulationScenario {
        withLock { storedScenario }
    }

    public func set(detect: DetectOutcome? = nil, tune: TuneOutcome? = nil, read: ReadOutcome? = nil, write: WriteOutcome? = nil, overwrite: OverwriteOutcome? = nil) {
        withLock {
            if let detect { storedDetect = detect }
            if let tune { storedTune = tune }
            if let read { storedRead = read }
            if let write { storedWrite = write }
            if let overwrite { storedOverwrite = overwrite }
        }
    }

    public func apply(_ scenario: SimulationScenario) {
        let unknown = UnknownToken(blocks: [0x00148040, 1, 2, 3, 4, 0xDEAD1234, 0xDEAD1234, 0])
        withLock {
            storedScenario = scenario
            switch scenario {
            case .goodToken:
                storedDetect = .detected
                storedTune = .measured(millivolts: 420)
                storedRead = .known(.sample(rideCount: 73, sequence: .mercury))
                storedWrite = .success
            case .weakSignal:
                storedDetect = .detected
                storedTune = .measured(millivolts: 120)
                storedRead = .known(.sample(rideCount: 73, sequence: .mercury))
                storedWrite = .success
            case .noChip:
                storedDetect = .noChip
                storedTune = .measured(millivolts: 0)
                storedRead = .noChip
                storedWrite = .success
            case .unknownFamily:
                storedDetect = .detected
                storedTune = .measured(millivolts: 420)
                storedRead = .unknown(unknown)
                storedWrite = .success
            case .writeFailure:
                storedDetect = .detected
                storedTune = .measured(millivolts: 420)
                storedRead = .known(.sample(rideCount: 73, sequence: .mercury))
                storedWrite = .failure("Charge could not be written. Try again.")
            }
        }
    }

    public func detect() async -> DetectOutcome {
        await Task.yield()
        return withLock {
            detectCallCount += 1
            return storedDetect
        }
    }

    public func tune() async -> TuneOutcome {
        await Task.yield()
        return withLock {
            tuneCallCount += 1
            return storedTune
        }
    }

    public func read() async -> ReadOutcome {
        await Task.yield()
        return withLock {
            readCallCount += 1
            return storedRead
        }
    }

    public func detectTuneRead() async -> DetectTuneReadOutcome {
        await Task.yield()
        return withLock {
            detectCallCount += 1
            guard case .detected = storedDetect else {
                if case .noChip = storedDetect { return .noChip }
                if case .failure(let error) = storedDetect { return .failure(error) }
                return .failure("Could not detect token.")
            }
            tuneCallCount += 1
            guard case .measured(let millivolts) = storedTune else {
                if case .failure(let error) = storedTune { return .failure(error) }
                return .failure("Could not tune reader.")
            }
            readCallCount += 1
            switch storedRead {
            case .known(let token): return .known(token, signalMillivolts: millivolts)
            case .unknown(let token): return .unknown(token, signalMillivolts: millivolts)
            case .noChip: return .noChip
            case .failure(let error): return .failure(error)
            }
        }
    }

    public func write(_ token: Token) async -> WriteOutcome {
        await Task.yield()
        return withLock {
            writeCallCount += 1
            switch storedWrite {
            case .success:
                lastWrittenToken = token
                storedRead = .known(token)
                return .success
            case .failure(let error):
                return .failure(error)
            }
        }
    }

    public func overwrite(_ image: [UInt32]) async -> OverwriteOutcome {
        await Task.yield()
        return withLock {
            overwriteCallCount += 1
            switch storedOverwrite {
            case .success:
                lastOverwrittenImage = image
                switch TokenDecoder.decode(blocks: image) {
                case .known(let token):
                    storedRead = .known(token)
                case .unknown(let token):
                    storedRead = .unknown(token)
                case .noChip:
                    storedRead = .noChip
                }
                return .success
            case .failure(let error):
                return .failure(error)
            }
        }
    }

    private func withLock<T>(_ body: () -> T) -> T {
        lock.lock()
        defer { lock.unlock() }
        return body()
    }
}
