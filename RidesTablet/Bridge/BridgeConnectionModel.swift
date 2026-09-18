import Foundation
import SwiftUI

public enum BridgeConnectionState: Equatable, Sendable {
    case unconfigured
    case pairing
    case restored
    case searching
    case connected
    case reading
    case readingPage0
    case scanningPage0
    case settingPage0
    case resettingPage0
    case relocating
    case authenticationRequired
    case failed(String)

    public var title: String {
        switch self {
        case .unconfigured: "Pairing required"
        case .pairing: "Pairing…"
        case .restored: "Saved pairing restored"
        case .searching: "Searching for saved bridge…"
        case .connected: "Connected"
        case .reading: "Reading block 5…"
        case .readingPage0: "Reading page0 rides…"
        case .scanningPage0: "Scanning page0 token…"
        case .settingPage0: "Setting page0 rides…"
        case .resettingPage0: "Resetting page0 token…"
        case .relocating: "Checking new bridge address…"
        case .authenticationRequired: "Pairing required"
        case .failed: "Action failed"
        }
    }

    public var isBusy: Bool {
        switch self {
        case .pairing, .reading, .readingPage0, .scanningPage0, .settingPage0, .resettingPage0, .relocating: true
        default: false
        }
    }

    public var isPaired: Bool {
        switch self {
        case .restored, .searching, .connected, .reading, .readingPage0, .scanningPage0, .settingPage0, .resettingPage0, .relocating: true
        default: false
        }
    }
}

private struct Page0WriteSnapshot: Equatable, Sendable {
    let block5: String
    let block6: String
}

private struct AutomaticReconnectOperation: Equatable, Sendable {
    let generation: Int
    let token: Int
    let candidate: BridgeBonjourCandidate
}

@MainActor
public final class BridgeConnectionModel: ObservableObject {
    @Published public var bridgeURLText: String
    @Published public private(set) var state: BridgeConnectionState
    @Published public private(set) var message: String?
    @Published public private(set) var lastBlock5Value: String?
    @Published public private(set) var lastPage0Block5Value: String?
    @Published public private(set) var lastPage0Block6Value: String?
    @Published public private(set) var lastScanBlock4Value: String?
    @Published public private(set) var lastSignalMillivolts: Int?
    @Published public private(set) var lastUnknownDumpURL: URL?
    @Published public private(set) var lastPage0Read: RideRead?
    @Published public private(set) var bonjourDiscoveryState: BridgeBonjourDiscoveryState
    @Published public private(set) var bonjourCandidates: [BridgeBonjourCandidate]
    @Published public private(set) var offeredBonjourCandidate: BridgeBonjourCandidate?
    @Published public private(set) var rejectedBonjourResultCount: Int
    @Published public var targetPage0RidesText: String
    @Published public var selectedResetSequence: RideSequence?
    @Published public private(set) var lastResetBlockValues: [Int: String] = [:]
#if DEBUG
    @Published public private(set) var physicalAcceptanceSummary: BridgePhysicalAcceptanceSummary?
    @Published public private(set) var physicalAcceptanceFailure: BridgePhysicalAcceptanceFailure?
    @Published public private(set) var slice4PhysicalAcceptanceSummary: BridgeSlice4PhysicalAcceptanceSummary?
    @Published public private(set) var slice4PhysicalAcceptanceFailure: BridgeSlice4PhysicalAcceptanceFailure?
    @Published public private(set) var slice5ConceptASmokeSummary: ConceptAPhysicalSmokeSummary?
    @Published public private(set) var slice5ConceptASmokeFailure: ConceptAPhysicalSmokeFailure?
#endif

    private let credentialStore: any BridgeCredentialStore
    private let session: URLSession
    private let healthRetryDelay: @Sendable () async throws -> Void
    private let now: @Sendable () -> Date
    private let bonjourBrowserSource: any BridgeBonjourBrowserSource
    private let bonjourQuiescenceDelay: @Sendable () async throws -> Void
    private let relocationNonceGenerator: @Sendable () -> String
    private let dumpStore: UnknownDumpStore
    private var client: BridgeClient?
    private var bonjourBrowseGeneration = 0
    private var localOperationGeneration = 0
    private var automaticBonjourBrowseGeneration: Int?
    private var automaticBonjourBridgeId: String?
    private var automaticBonjourLaunchStarted = false
    private var automaticBonjourSnapshot: [BridgeBonjourCandidate] = []
    private var automaticBonjourAttemptedCandidateIDs: Set<String> = []
    /// The candidate whose proof and authenticated status last established the
    /// current Bonjour connection. A credential alone never establishes this.
    private var automaticBonjourConnectedCandidateID: String?
    private var stateBeforeBonjourRelocation: BridgeConnectionState?
    private var automaticReconnectTask: Task<Void, Never>?
    private var automaticReconnectPredecessorTask: Task<Void, Never>?
    private var automaticReconnectVerification: (operation: AutomaticReconnectOperation, task: Task<BridgePairStatusResponse, Error>)?
    private var automaticReconnectMigrationOperation: AutomaticReconnectOperation?
    private var automaticReconnectToken = 0
    private var explicitBonjourRelocationTask: Task<Bool, Never>?
    private var explicitBonjourRelocationToken = 0
    private var page0WriteSnapshot: Page0WriteSnapshot?
    private var credentialRestoreFailed = false
    private var launchAddressOverrideApplied = false
    private var launchAddressOverrideWasProvided = false
#if DEBUG
    private var launchPhysicalAcceptanceAttempted = false
    private var launchSlice4PhysicalAcceptanceAttempted = false
    private var launchSlice5ConceptASmokeAttempted = false
#endif

    public init(
        credentialStore: any BridgeCredentialStore = KeychainBridgeCredentialStore(),
        session: URLSession = .shared,
        defaultBridgeURL: String = "",
        healthRetryDelay: @escaping @Sendable () async throws -> Void = {
            try await Task.sleep(nanoseconds: 100_000_000)
        },
        now: @escaping @Sendable () -> Date = { Date() },
        bonjourBrowserSource: any BridgeBonjourBrowserSource = NetworkBonjourBrowserSource(),
        bonjourQuiescenceDelay: @escaping @Sendable () async throws -> Void = {
            try await Task.sleep(nanoseconds: 300_000_000)
        },
        relocationNonceGenerator: @escaping @Sendable () -> String = {
            BridgeRelocationProof.makeNonce()
        },
        dumpStore: UnknownDumpStore = UnknownDumpStore()
    ) {
        self.credentialStore = credentialStore
        self.session = session
        self.healthRetryDelay = healthRetryDelay
        self.now = now
        self.bonjourBrowserSource = bonjourBrowserSource
        self.bonjourQuiescenceDelay = bonjourQuiescenceDelay
        self.relocationNonceGenerator = relocationNonceGenerator
        self.dumpStore = dumpStore
        self.bridgeURLText = defaultBridgeURL
        self.state = .unconfigured
        self.message = nil
        self.lastBlock5Value = nil
        self.lastPage0Block5Value = nil
        self.lastPage0Block6Value = nil
        self.lastScanBlock4Value = nil
        self.lastSignalMillivolts = nil
        self.lastUnknownDumpURL = nil
        self.lastPage0Read = nil
        self.bonjourDiscoveryState = .idle
        self.bonjourCandidates = []
        self.offeredBonjourCandidate = nil
        self.rejectedBonjourResultCount = 0
        self.targetPage0RidesText = ""
        self.selectedResetSequence = nil
        self.lastResetBlockValues = [:]
#if DEBUG
        self.physicalAcceptanceSummary = nil
        self.physicalAcceptanceFailure = nil
        self.slice4PhysicalAcceptanceSummary = nil
        self.slice4PhysicalAcceptanceFailure = nil
        self.slice5ConceptASmokeSummary = nil
        self.slice5ConceptASmokeFailure = nil
#endif
        restore()
    }

    deinit {
        // Invalidate ownership before cancelling. A late URLSession/continuation
        // completion must not be able to publish through a task that is being torn
        // down, and the verification task must not outlive the model's lifecycle.
        automaticReconnectToken &+= 1
        automaticReconnectTask?.cancel()
        automaticReconnectVerification?.task.cancel()
        explicitBonjourRelocationToken &+= 1
        explicitBonjourRelocationTask?.cancel()
    }

    public var isBusy: Bool { state.isBusy }
    /// A browse stays live while its result set is being displayed. An offer or a
    /// required selection is not a terminal state: NWBrowser can still remove,
    /// replace, or add services until the operator explicitly stops it.
    public var isBonjourBrowsing: Bool {
        switch bonjourDiscoveryState {
        case .browsing, .offered, .reconnecting, .selectionRequired:
            true
        case .idle, .stopped, .denied, .failed:
            false
        }
    }
    /// A saved credential remains usable for local reset even after a transient failure.
    public var isPaired: Bool { client?.hasCredential == true }

    /// Concept A can start once an authenticated bridge client exists.
    /// Searching/restored/connected all count; pairing and hard failures do not.
    public var isReadyForOperatorWorkflow: Bool {
        guard isPaired else { return false }
        switch state {
        case .connected, .restored, .searching:
            return true
        case .reading, .readingPage0, .scanningPage0, .settingPage0, .resettingPage0, .relocating:
            return true
        case .unconfigured, .pairing, .authenticationRequired, .failed:
            return false
        }
    }

    /// Shared authenticated client for the hardware-neutral Concept A adapter.
    public func makeRideTokenDevice() -> NetworkRideTokenDevice? {
        guard let client, client.hasCredential else { return nil }
        return NetworkRideTokenDevice(client: client)
    }

    /// Forget is available for either the live bearer or a credential that is still in storage,
    /// including while Bonjour is relocating the live client.
    public var hasPairingToForget: Bool {
        isPaired || hasSavedCredential || credentialRestoreFailed
    }

    /// Address migration is only offered for a credential that is actually persisted.
    public var hasSavedCredential: Bool {
        do { return try credentialStore.load() != nil }
        catch { return false }
    }

    /// Starts launch-time reconnect only for a persisted credential whose public bridge
    /// identifier can be compared safely. Storage failures and legacy credentials without an
    /// identifier take
    /// the explicit/manual path and never start a browse.
    public func startAutomaticBonjourReconnect() async {
        guard !automaticBonjourLaunchStarted else { return }
        automaticBonjourLaunchStarted = true

        if launchAddressOverrideWasProvided {
            // The launch override is authoritative. The caller awaits it before invoking
            // this method, so a second migration can never race the override transaction.
            return
        }
        if credentialRestoreFailed {
            message = "Automatic bridge reconnect is unavailable because the saved pairing could not be read. Select a bridge or enter its private address manually."
            return
        }

        let credential: BridgeCredential?
        do {
            credential = try credentialStore.load()
        } catch {
            message = "Automatic bridge reconnect is unavailable because the saved pairing could not be read. Select a bridge or enter its private address manually."
            return
        }
        guard let credential, let bridgeId = credential.bridgeId,
              BridgeBonjourConstants.isValidBridgeID(bridgeId) else {
            message = credential == nil
                ? "No saved bridge pairing was found. Use manual entry, QR pairing, or Find bridges."
                : "Automatic reconnect requires a saved bridge identifier. Use manual entry or pair again."
            return
        }

        automaticBonjourBridgeId = bridgeId
        startBonjourBrowse(automatic: true)
    }

    /// True when the operator has entered a different normalized address while paired.
    public var hasEnteredBridgeAddressChange: Bool {
        guard let client, client.hasCredential else { return false }
        guard let entered = try? BridgeClient.normalizeBaseURL(bridgeURLText) else {
            return !bridgeURLText.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
        }
        return entered != client.baseURL
    }

    public var canUseEnteredBridgeAddress: Bool {
        !isBusy && isPaired && hasSavedCredential && hasEnteredBridgeAddressChange
    }

    public var hasFreshPage0Snapshot: Bool { page0WriteSnapshot != nil }
    public var resolvedPage0Rides: UInt? { lastPage0Read?.rides }
    public var page0SourceBlockNumber: Int? { lastPage0Read?.sourceBlockNumber }
    public var page0BlocksMatch: Bool? { lastPage0Read.map(\.blocksMatched) }
    public var page0WarningMessage: String? { lastPage0Read?.warningMessage }
    public var page0WarningDisplay: String? {
        guard let read = lastPage0Read else { return nil }
        if let warning = read.warningMessage { return warning }
        return read.status == .unknownEncodingSequence
            ? "Warning: page0 mirror encoding is unknown."
            : "None"
    }

    public var canSetPage0Rides: Bool {
        !isBusy && isPaired && isWritablePage0Snapshot && parsedTargetPage0Rides != nil
    }

    public func restore() {
        do {
            guard let credential = try credentialStore.load() else {
                credentialRestoreFailed = false
                client = nil
                state = .unconfigured
                return
            }
            let restoredClient = try BridgeClient(baseURL: credential.baseURL, session: session, credential: credential)
            credentialRestoreFailed = false
            client = restoredClient
            bridgeURLText = restoredClient.baseURL.absoluteString
            state = .restored
            message = "Saved pairing restored. Readiness will be checked on the next operation."
        } catch {
            credentialRestoreFailed = true
            client = nil
            let detail = "Saved pairing could not be restored. Pair again."
            state = .failed(detail)
            message = detail
        }
    }

    public func pair(pin: String) async {
        await pair(pin: pin, bridgeId: nil, retryHealthOnUnreachable: true)
    }

    /// Pairs once with an optional public bridge identifier. Manual pairing passes nil; QR pairing
    /// supplies the parsed public identifier so the initial Keychain save is complete and transactional.
    public func pair(pin: String, bridgeId: String?) async {
        // Supplying a public bridge identifier denotes QR pairing. Keep the legacy health
        // transition retry only for the manual, identifier-less entry point.
        await pair(pin: pin, bridgeId: bridgeId, retryHealthOnUnreachable: bridgeId == nil)
    }

    private func pair(pin: String, bridgeId: String?, retryHealthOnUnreachable: Bool) async {
        guard !isBusy else { return }
        guard !isPaired else {
            if state != .connected { state = .restored }
            message = "This iPad is already paired. Use Forget before pairing another bridge."
            return
        }
        state = .pairing
        message = nil
        clearPage0Snapshot()
        lastBlock5Value = nil
        let operationGeneration = localOperationGeneration

        let newClient: BridgeClient
        do {
            newClient = try BridgeClient(
                baseURLString: bridgeURLText,
                session: session,
                healthRetryDelay: healthRetryDelay
            )
        } catch {
            fail(with: error)
            return
        }

        do {
            let credential = try await newClient.pair(
                pin: pin,
                bridgeId: bridgeId,
                retryHealthOnUnreachable: retryHealthOnUnreachable
            )
            guard operationGeneration == localOperationGeneration else {
                // A local reset won while the pairing request was in flight. The bearer
                // was not securely saved, so use the only allowed server cleanup path,
                // then drop it regardless of the cleanup result.
                try? await newClient.revoke()
                newClient.clearCredential()
                return
            }
            do {
                try credentialStore.save(credential)
            } catch {
                // Pairing already created a valid bearer. Revoke it before dropping the only reference.
                do {
                    try await newClient.revoke()
                    newClient.clearCredential()
                    client = nil
                    let detail = "Pairing succeeded, but the secure credential could not be saved. The temporary server pairing was cleaned up; pair again."
                    state = .failed(detail)
                    message = detail
                } catch BridgeClientError.unauthorized {
                    newClient.clearCredential()
                    client = nil
                    let detail = "Pairing succeeded, but the secure credential could not be saved. The temporary server pairing was already invalid; pair again."
                    state = .failed(detail)
                    message = detail
                } catch {
                    // Never retain an active bearer when secure local storage failed.
                    newClient.clearCredential()
                    client = nil
                    let detail = "Pairing succeeded, but the secure credential could not be saved or cleaned up. No active local pairing was retained; pair again."
                    state = .failed(detail)
                    message = detail
                }
                return
            }
            guard operationGeneration == localOperationGeneration else {
                newClient.clearCredential()
                client = nil
                return
            }
            client = newClient
            bridgeURLText = credential.baseURL.absoluteString
            state = .connected
            message = "Paired with the local bridge."
        } catch is CancellationError {
            guard operationGeneration == localOperationGeneration else { return }
            restoreStateAfterCancellation()
        } catch {
            guard operationGeneration == localOperationGeneration else { return }
            fail(with: error)
        }
    }

    /// Imports one strict backend QR payload and immediately uses the existing one-shot health
    /// preflight plus PIN pairing path. A parsed payload is never replayed by this method.
    public func importPairingPayload(_ json: String) async {
        guard !isBusy else { return }
        guard !isPaired else {
            if state != .connected { state = .restored }
            message = "This iPad is already paired. Use Forget before importing another pairing QR."
            return
        }

        do {
            let payload = try BridgePairingPayload.parse(json, now: now())
            await importPairingPayload(payload)
        } catch {
            fail(with: error)
        }
    }

    /// Import overload used by scanner seams and deterministic tests. The value is revalidated
    /// before any URL or PIN state is changed.
    public func importPairingPayload(_ payload: BridgePairingPayload) async {
        guard !isBusy else { return }
        guard !isPaired else {
            if state != .connected { state = .restored }
            message = "This iPad is already paired. Use Forget before importing another pairing QR."
            return
        }

        do {
            let validated = try payload.validated(now: now())
            bridgeURLText = validated.bridgeURL.absoluteString
            // A QR is one-time input. Do not replay even the readiness preflight if
            // the local-network permission transition reports an initial unreachable.
            await pair(pin: validated.pin, bridgeId: validated.bridgeId, retryHealthOnUnreachable: false)
        } catch {
            fail(with: error)
        }
    }

    /// Starts an operator-controlled browse. Automatic reconnect uses the same live
    /// source but is enabled only by `startAutomaticBonjourReconnect()`.
    public func startBonjourBrowse() {
        startBonjourBrowse(automatic: false)
    }

    private func startBonjourBrowse(automatic: Bool) {
        guard !isBonjourBrowsing else { return }
        bonjourBrowseGeneration += 1
        let generation = bonjourBrowseGeneration
        automaticBonjourBrowseGeneration = automatic ? generation : nil
        if !automatic {
            automaticBonjourBridgeId = nil
        }
        automaticBonjourSnapshot = []
        automaticReconnectPredecessorTask = nil
        if automatic {
            automaticBonjourAttemptedCandidateIDs = []
            automaticBonjourConnectedCandidateID = nil
            state = .searching
        }
        cancelAutomaticReconnect()
        automaticBonjourConnectedCandidateID = nil
        bonjourCandidates = []
        offeredBonjourCandidate = nil
        rejectedBonjourResultCount = 0
        bonjourDiscoveryState = .browsing
        if isPaired {
            state = .searching
        }
        message = automatic
            ? "Searching for the saved bridge over Bonjour…"
            : "Searching for compatible local RidesBridge services…"

        bonjourBrowserSource.start(
            onState: { [weak self] sourceState in
                Task { @MainActor [weak self] in
                    self?.receiveBonjourState(sourceState, generation: generation)
                }
            },
            onResults: { [weak self] results in
                Task { @MainActor [weak self] in
                    self?.receiveBonjourResults(results, generation: generation)
                }
            }
        )
    }

    public func stopBonjourBrowse() {
        bonjourBrowseGeneration += 1
        automaticBonjourBrowseGeneration = nil
        automaticBonjourBridgeId = nil
        automaticBonjourSnapshot = []
        automaticReconnectPredecessorTask = nil
        if (automaticReconnectMigrationOperation != nil || explicitBonjourRelocationTask != nil), state == .relocating {
            bridgeURLText = client?.baseURL.absoluteString ?? bridgeURLText
            state = isPaired ? .restored : .unconfigured
        } else if isPaired && automaticBonjourConnectedCandidateID == nil {
            state = .restored
        } else if state == .searching {
            state = .unconfigured
        }
        stateBeforeBonjourRelocation = nil
        automaticBonjourConnectedCandidateID = nil
        automaticReconnectMigrationOperation = nil
        cancelExplicitBonjourRelocation()
        cancelAutomaticReconnect()
        bonjourBrowserSource.stop()
        bonjourCandidates = []
        offeredBonjourCandidate = nil
        rejectedBonjourResultCount = 0
        bonjourDiscoveryState = .stopped
        message = "Bridge discovery stopped. You can still enter a private address manually."
    }

    public func selectBonjourCandidate(_ candidate: BridgeBonjourCandidate) async {
        guard !isBusy else { return }
        guard bonjourCandidates.contains(where: { $0.id == candidate.id }) else { return }

        // Explicit selection supersedes an automatic attempt. In particular, do
        // not let an automatic task that is still in-flight compete with the
        // operator's migration or later clear its task slot.
        if automaticBonjourBrowseGeneration != nil {
            // Once the operator selects a result, this browse is manual. Keep
            // later identical browser snapshots from scheduling a competing
            // automatic migration.
            automaticBonjourBrowseGeneration = nil
            automaticBonjourBridgeId = nil
            automaticReconnectPredecessorTask = nil
            cancelAutomaticReconnect()
        }

        guard isPaired else {
            // Discovery is only an offer. It never pairs or performs a request.
            bridgeURLText = candidate.url.absoluteString
            message = "Bridge address filled from Bonjour. Pair with a PIN, scan a QR, or keep using manual entry."
            return
        }

        let savedCredential: BridgeCredential?
        do {
            savedCredential = try credentialStore.load()
        } catch {
            fail(with: error)
            return
        }
        guard let savedCredential, let savedBridgeId = savedCredential.bridgeId else {
            // A legacy/manual credential has no authenticated bridge binding. Do not
            // turn an unverified address change into identifier-safe relocation.
            fail(with: BridgeBonjourSelectionError.identityUnavailable)
            return
        }
        guard savedBridgeId == candidate.bridgeId else {
            fail(with: BridgeBonjourSelectionError.identityMismatch)
            return
        }

        let previousText = bridgeURLText
        bridgeURLText = candidate.url.absoluteString
        // Track explicit Bonjour work as well as automatic work so disappearing
        // views/candidates cancel both proof and status before they can commit.
        explicitBonjourRelocationToken &+= 1
        let relocationToken = explicitBonjourRelocationToken
        let relocationTask = Task { @MainActor [weak self] in
            guard let self else { return false }
            return await self.migrateEnteredBridgeAddress(
                candidate: candidate,
                explicitToken: relocationToken
            )
        }
        explicitBonjourRelocationTask = relocationTask
        let migrated = await withTaskCancellationHandler {
            await relocationTask.value
        } onCancel: {
            relocationTask.cancel()
        }
        guard explicitBonjourRelocationToken == relocationToken else { return }
        explicitBonjourRelocationTask = nil
        if !migrated {
            bridgeURLText = previousText
        }
    }

    private func receiveBonjourState(_ sourceState: BridgeBonjourSourceState, generation: Int) {
        guard generation == bonjourBrowseGeneration, isBonjourBrowsing else { return }
        switch sourceState {
        case .ready:
            if bonjourCandidates.isEmpty {
                automaticBonjourConnectedCandidateID = nil
                if explicitBonjourRelocationTask != nil {
                    bridgeURLText = client?.baseURL.absoluteString ?? bridgeURLText
                    cancelExplicitBonjourRelocation()
                }
                if automaticReconnectTask != nil || automaticReconnectVerification != nil {
                    cancelAutomaticReconnect()
                }
                if isPaired {
                    state = .searching
                }
                message = automaticBonjourBrowseGeneration == generation
                    ? "Searching for the saved bridge over Bonjour…"
                    : "Searching for compatible local RidesBridge services…"
            }
        case .denied:
            // Terminal source states own the end of this browse. Stop even when
            // an injected source reports the terminal state without cleaning up
            // its underlying browser itself.
            bonjourBrowseGeneration += 1
            automaticBonjourBrowseGeneration = nil
            automaticBonjourBridgeId = nil
            automaticReconnectPredecessorTask = nil
            if (automaticReconnectMigrationOperation != nil || explicitBonjourRelocationTask != nil), state == .relocating {
                bridgeURLText = client?.baseURL.absoluteString ?? bridgeURLText
            }
            state = isPaired ? .restored : .unconfigured
            stateBeforeBonjourRelocation = nil
            automaticBonjourConnectedCandidateID = nil
            automaticReconnectMigrationOperation = nil
            cancelExplicitBonjourRelocation()
            cancelAutomaticReconnect()
            bonjourBrowserSource.stop()
            bonjourDiscoveryState = .denied
            message = "Bonjour discovery was denied. Allow Local Network access or enter the bridge's private IP manually."
        case .failed:
            bonjourBrowseGeneration += 1
            automaticBonjourBrowseGeneration = nil
            automaticBonjourBridgeId = nil
            automaticReconnectPredecessorTask = nil
            if (automaticReconnectMigrationOperation != nil || explicitBonjourRelocationTask != nil), state == .relocating {
                bridgeURLText = client?.baseURL.absoluteString ?? bridgeURLText
            }
            state = isPaired ? .restored : .unconfigured
            stateBeforeBonjourRelocation = nil
            automaticBonjourConnectedCandidateID = nil
            automaticReconnectMigrationOperation = nil
            cancelExplicitBonjourRelocation()
            cancelAutomaticReconnect()
            bonjourBrowserSource.stop()
            bonjourDiscoveryState = .failed
            message = "Bonjour discovery failed. Check Local Network access and Wi-Fi, or enter the bridge's private IP manually."
        }
    }

    private func receiveBonjourResults(_ results: [BridgeBonjourRawResult], generation: Int) {
        guard generation == bonjourBrowseGeneration, isBonjourBrowsing else { return }
        let snapshot = BridgeBonjourCandidateParser.parseSnapshot(results)
        let isAutomatic = automaticBonjourBrowseGeneration == generation
        let previousAutomaticSnapshot = automaticBonjourSnapshot
        let previousBonjourCandidates = bonjourCandidates
        automaticBonjourSnapshot = isAutomatic ? snapshot.candidates : []
        let currentIDs = Set(snapshot.candidates.map(\.id))
        if let connectedCandidateID = automaticBonjourConnectedCandidateID,
           !currentIDs.contains(connectedCandidateID) {
            // A removed advertisement is a new lifecycle event, not a request
            // failure. A saved credential alone is not proof that the service is
            // still present, so keep the connection UI in the searching state.
            automaticBonjourConnectedCandidateID = nil
            if isPaired {
                state = .searching
            }
        }
        if isAutomatic {
            automaticBonjourAttemptedCandidateIDs.formIntersection(currentIDs)
        }
        if !isAutomatic,
           explicitBonjourRelocationTask != nil,
           previousBonjourCandidates != snapshot.candidates {
            // A selected TXT service is no longer the same candidate. Cancel its
            // proof/status owner before accepting the new browser snapshot.
            if state == .relocating {
                bridgeURLText = client?.baseURL.absoluteString ?? bridgeURLText
                state = isPaired ? .searching : .unconfigured
            }
            cancelExplicitBonjourRelocation()
        }
        bonjourCandidates = snapshot.candidates
        rejectedBonjourResultCount = snapshot.rejectedResultCount
        offeredBonjourCandidate = snapshot.candidates.count == 1 ? snapshot.candidates[0] : nil

        if isAutomatic && previousAutomaticSnapshot != snapshot.candidates {
            // A newer snapshot cancels the old owner. If its authenticated
            // request is already in flight, the newer operation waits for the
            // old task to finish rather than racing a second migration.
            automaticReconnectPredecessorTask = automaticReconnectVerification == nil
                ? nil
                : (automaticReconnectTask ?? automaticReconnectPredecessorTask)
            automaticReconnectMigrationOperation = nil
            if state == .relocating || state == .connected {
                // Until the new candidate proves itself, a saved credential is
                // only a pairing. It must not be presented as a live connection.
                if automaticBonjourConnectedCandidateID == nil {
                    state = .searching
                }
            }
            cancelAutomaticReconnect(preserveVerification: true)
        }

        switch snapshot.candidates.count {
        case 0:
            bonjourDiscoveryState = .browsing
            if isPaired {
                state = .searching
            }
            message = snapshot.rejectedResultCount == 0
                ? (isAutomatic ? "Searching for the saved bridge over Bonjour…" : "Searching for compatible local RidesBridge services…")
                : "No compatible RidesBridge service was found. Searching continues; check the bridge version and TXT record, or use manual entry."
        case 1:
            bonjourDiscoveryState = .offered
            if isAutomatic {
                if let expectedBridgeId = automaticBonjourBridgeId,
                   snapshot.candidates[0].bridgeId != expectedBridgeId {
                    message = "The discovered bridge identifier does not match the saved pairing. No request was sent; select a bridge manually or enter its private address."
                } else {
                    message = "The saved bridge was found. Waiting for a stable Bonjour result before reconnecting…"
                    scheduleAutomaticReconnectIfEligible(snapshot.candidates, generation: generation)
                }
            } else {
                message = "One compatible bridge was found. Select it to review the address; it will not be paired automatically."
            }
        default:
            bonjourDiscoveryState = .selectionRequired
            message = isAutomatic
                ? "Multiple local bridge candidates were found. No automatic reconnect was attempted; select one explicitly or enter an address manually."
                : "Multiple compatible bridges were found. Select one explicitly; no bridge will be chosen automatically."
        }
    }

    private func scheduleAutomaticReconnectIfEligible(
        _ candidates: [BridgeBonjourCandidate],
        generation: Int
    ) {
        guard candidates.count == 1,
              automaticBonjourBrowseGeneration == generation,
              let candidate = candidates.first,
              let expectedBridgeId = automaticBonjourBridgeId,
              candidate.bridgeId == expectedBridgeId,
              !automaticBonjourAttemptedCandidateIDs.contains(candidate.id),
              automaticReconnectTask == nil else { return }

        automaticReconnectToken &+= 1
        let operation = AutomaticReconnectOperation(
            generation: generation,
            token: automaticReconnectToken,
            candidate: candidate
        )
        let predecessor = automaticReconnectPredecessorTask
        automaticReconnectPredecessorTask = nil
        automaticReconnectTask = Task { @MainActor [weak self] in
            do {
                try await self?.bonjourQuiescenceDelay()
            } catch {
                self?.finishAutomaticReconnectIfOwner(operation)
                return
            }
            guard !Task.isCancelled else {
                self?.finishAutomaticReconnectIfOwner(operation)
                return
            }
            if let predecessor {
                await predecessor.value
            }
            guard !Task.isCancelled else {
                self?.finishAutomaticReconnectIfOwner(operation)
                return
            }
            await self?.automaticReconnectDelayElapsed(operation: operation)
        }
    }

    private func automaticReconnectDelayElapsed(operation: AutomaticReconnectOperation) async {
        guard isAutomaticReconnectOwner(operation),
              !Task.isCancelled,
              !automaticBonjourAttemptedCandidateIDs.contains(operation.candidate.id) else {
            finishAutomaticReconnectIfOwner(operation)
            return
        }

        // Mark before reading storage or starting the transaction. A failed or
        // cancelled automatic attempt is never retried by later identical snapshots.
        automaticBonjourAttemptedCandidateIDs.insert(operation.candidate.id)
        defer { finishAutomaticReconnectIfOwner(operation) }
        bonjourDiscoveryState = .reconnecting

        do {
            guard let savedCredential = try credentialStore.load(),
                  let savedBridgeId = savedCredential.bridgeId,
                  BridgeBonjourConstants.isValidBridgeID(savedBridgeId),
                  savedBridgeId == operation.candidate.bridgeId,
                  isAutomaticReconnectOwner(operation), !Task.isCancelled else {
                guard isAutomaticReconnectOwner(operation), !Task.isCancelled else { return }
                message = "Automatic reconnect was not authorized for this bridge. No request was sent; select it manually or enter an address."
                bonjourDiscoveryState = .offered
                return
            }
            let migrated = await migrateEnteredBridgeAddress(
                savedCredential: savedCredential,
                automatic: true,
                candidate: operation.candidate,
                automaticOperation: operation
            )
            guard isAutomaticReconnectOwner(operation), !Task.isCancelled else { return }
            automaticReconnectMigrationOperation = nil
            if migrated {
                bonjourDiscoveryState = .offered
            } else if bonjourDiscoveryState == .reconnecting {
                bonjourDiscoveryState = .offered
                message = "Automatic reconnect failed. Select the discovered bridge manually or enter its private address."
            }
        } catch is CancellationError {
            // The migration transaction restores the old address/client on cancellation.
            guard isAutomaticReconnectOwner(operation), !Task.isCancelled else { return }
            bonjourDiscoveryState = .offered
            message = "Automatic reconnect was cancelled. Select the bridge manually or enter its private address."
        } catch {
            guard isAutomaticReconnectOwner(operation), !Task.isCancelled else { return }
            bonjourDiscoveryState = .offered
            message = "Automatic reconnect could not verify the saved pairing. No request was sent; select the bridge manually or enter an address."
        }
    }

    private func isAutomaticReconnectOwner(_ operation: AutomaticReconnectOperation) -> Bool {
        operation.token == automaticReconnectToken
            && operation.generation == bonjourBrowseGeneration
            && automaticBonjourBrowseGeneration == operation.generation
            && automaticBonjourSnapshot == [operation.candidate]
    }

    private func finishAutomaticReconnectIfOwner(_ operation: AutomaticReconnectOperation) {
        guard isAutomaticReconnectOwner(operation) else { return }
        automaticReconnectTask = nil
    }

    private func clearAutomaticReconnectVerificationIfOwner(_ operation: AutomaticReconnectOperation) {
        guard automaticReconnectVerification?.operation == operation,
              isAutomaticReconnectOwner(operation) else { return }
        automaticReconnectVerification = nil
    }

    private func clearAutomaticReconnectMigrationIfOwner(_ operation: AutomaticReconnectOperation) {
        guard automaticReconnectMigrationOperation == operation,
              isAutomaticReconnectOwner(operation) else { return }
        automaticReconnectMigrationOperation = nil
    }

    private func invalidateBonjourWorkForLocalReset() {
        bonjourBrowseGeneration &+= 1
        automaticBonjourBrowseGeneration = nil
        automaticBonjourBridgeId = nil
        automaticBonjourSnapshot = []
        automaticBonjourAttemptedCandidateIDs = []
        automaticBonjourConnectedCandidateID = nil
        stateBeforeBonjourRelocation = nil
        automaticReconnectMigrationOperation = nil

        automaticReconnectToken &+= 1
        automaticReconnectTask?.cancel()
        automaticReconnectTask = nil
        automaticReconnectPredecessorTask?.cancel()
        automaticReconnectPredecessorTask = nil
        automaticReconnectVerification?.task.cancel()
        automaticReconnectVerification = nil

        explicitBonjourRelocationToken &+= 1
        explicitBonjourRelocationTask?.cancel()
        explicitBonjourRelocationTask = nil

        bonjourBrowserSource.stop()
        bonjourCandidates = []
        offeredBonjourCandidate = nil
        rejectedBonjourResultCount = 0
        bonjourDiscoveryState = .stopped
    }

    private func cancelExplicitBonjourRelocation() {
        explicitBonjourRelocationToken &+= 1
        explicitBonjourRelocationTask?.cancel()
        explicitBonjourRelocationTask = nil
    }

    private func cancelAutomaticReconnect(preserveVerification: Bool = false) {
        automaticReconnectToken &+= 1
        automaticReconnectTask?.cancel()
        automaticReconnectTask = nil
        automaticReconnectVerification?.task.cancel()
        if !preserveVerification {
            automaticReconnectVerification = nil
            automaticReconnectPredecessorTask = nil
            automaticReconnectMigrationOperation = nil
        }
    }

    public func applyLaunchAddressOverride(_ value: String?) async {
        let override = value?.trimmingCharacters(in: .whitespacesAndNewlines) ?? ""
        guard !override.isEmpty, !launchAddressOverrideApplied else { return }
        launchAddressOverrideApplied = true
        launchAddressOverrideWasProvided = true
        bridgeURLText = override

        guard isPaired else { return }
        await useEnteredBridgeAddress()
    }

#if DEBUG
    /// Runs the launch-triggered physical acceptance only with a persisted pairing,
    /// a live bearer, and a nonbusy model. The attempted flag makes the launch
    /// trigger one-shot even if SwiftUI recreates or re-enters the task.
    public func runLaunchPhysicalAcceptanceIfRequested() async {
        guard !launchPhysicalAcceptanceAttempted else { return }
        launchPhysicalAcceptanceAttempted = true
        guard !isBusy, hasSavedCredential, let client, client.hasCredential else { return }

        let result = await BridgePhysicalAcceptanceCoordinator(client: client).run()
        switch result {
        case .success(let summary):
            physicalAcceptanceSummary = summary
            physicalAcceptanceFailure = nil
            message = summary.conciseDescription
        case .failure(let failure):
            physicalAcceptanceFailure = failure
            physicalAcceptanceSummary = nil
        }
    }

    /// Runs the launch-triggered Slice 4 scan + Venus reset acceptance only with a
    /// persisted pairing, a live bearer, and a nonbusy model.
    public func runLaunchSlice4PhysicalAcceptanceIfRequested() async {
        guard !launchSlice4PhysicalAcceptanceAttempted else { return }
        launchSlice4PhysicalAcceptanceAttempted = true
        guard !isBusy, hasSavedCredential, let client, client.hasCredential else { return }

        let result = await BridgeSlice4PhysicalAcceptanceCoordinator(client: client).run()
        switch result {
        case .success(let summary):
            slice4PhysicalAcceptanceSummary = summary
            slice4PhysicalAcceptanceFailure = nil
            message = summary.conciseDescription
        case .failure(let failure):
            slice4PhysicalAcceptanceFailure = failure
            slice4PhysicalAcceptanceSummary = nil
        }
    }

    /// Runs launch-triggered Concept A smoke through `NetworkRideTokenDevice` + `RidesViewModel`.
    public func runLaunchSlice5ConceptAPhysicalSmokeIfRequested() async {
        guard !launchSlice5ConceptASmokeAttempted else { return }
        launchSlice5ConceptASmokeAttempted = true

        var device: NetworkRideTokenDevice?
        for _ in 0..<100 {
            if Task.isCancelled { return }
            if !isBusy, hasSavedCredential, let ready = makeRideTokenDevice() {
                device = ready
                break
            }
            try? await Task.sleep(nanoseconds: 100_000_000)
        }
        guard let device else { return }

        let result = await ConceptAPhysicalSmokeCoordinator(device: device).run()
        switch result {
        case .success(let summary):
            slice5ConceptASmokeSummary = summary
            slice5ConceptASmokeFailure = nil
            message = summary.conciseDescription
        case .failure(let failure):
            slice5ConceptASmokeFailure = failure
            slice5ConceptASmokeSummary = nil
            if let detail = failure.detail, !detail.isEmpty {
                message = "Slice 5 Concept A smoke failed at \(failure.stage): \(detail)"
            } else {
                message = "Slice 5 Concept A smoke failed at \(failure.stage)."
            }
        }
    }
#endif

    /// Moves a saved bearer to a new local bridge address transactionally.
    /// Verification and persistence happen before replacing the active client; failures
    /// therefore leave the old client and stored credential available for recovery.
    public func useEnteredBridgeAddress() async {
        _ = await migrateEnteredBridgeAddress()
    }

    /// The shared address migration transaction used by manual entry and Bonjour.
    /// The Boolean lets Bonjour restore the exact pre-selection text even when the
    /// HTTP request is cancelled and the model returns to its prior truthful state.
    @discardableResult
    private func migrateEnteredBridgeAddress(
        savedCredential: BridgeCredential? = nil,
        automatic: Bool = false,
        candidate: BridgeBonjourCandidate? = nil,
        automaticOperation: AutomaticReconnectOperation? = nil,
        explicitToken: Int? = nil
    ) async -> Bool {
        guard !isBusy else { return false }
        let operationGeneration = localOperationGeneration
        guard let oldClient = client, oldClient.hasCredential else {
            state = .authenticationRequired
            message = BridgeClientError.missingCredential.localizedDescription
            return false
        }

        let previousBridgeURLText = oldClient.baseURL.absoluteString
        stateBeforeBonjourRelocation = state
        if let explicitToken {
            guard explicitBonjourRelocationToken == explicitToken, !Task.isCancelled else { return false }
        }
        if let automaticOperation {
            guard isAutomaticReconnectOwner(automaticOperation), !Task.isCancelled else { return false }
            automaticReconnectMigrationOperation = automaticOperation
        }
        state = .relocating
        message = nil

        do {
            let persistedCredential: BridgeCredential
            if let savedCredential {
                persistedCredential = savedCredential
            } else {
                guard let loadedCredential = try credentialStore.load() else {
                    throw BridgeClientError.missingCredential
                }
                persistedCredential = loadedCredential
            }
            let candidateURL: URL
            if let candidate {
                candidateURL = try BridgeClient.normalizeBaseURL(candidate.url)
            } else {
                candidateURL = try BridgeClient.normalizeBaseURL(bridgeURLText)
            }
            let candidateCredential = BridgeCredential(
                baseURL: candidateURL,
                accessToken: persistedCredential.accessToken,
                tokenType: persistedCredential.tokenType,
                bridgeId: persistedCredential.bridgeId
            )
            let candidateClient = try BridgeClient(
                baseURL: candidateURL,
                session: session,
                credential: candidateCredential
            )
            if let automaticOperation {
                guard isAutomaticReconnectOwner(automaticOperation), !Task.isCancelled else { return false }
                let nonce = relocationNonceGenerator()
                let verificationTask = Task<BridgePairStatusResponse, Error> { @MainActor in
                    try await candidateClient.proveBonjourRelocation(
                        expectedBridgeId: automaticOperation.candidate.bridgeId,
                        nonce: nonce
                    )
                    // Churn/cancellation may arrive while the proof request is in
                    // flight. Never turn a late proof completion into a stale bearer
                    // status request.
                    try Task.checkCancellation()
                    return try await candidateClient.verifyPairing()
                }
                automaticReconnectVerification = (automaticOperation, verificationTask)
                _ = try await verificationTask.value
                clearAutomaticReconnectVerificationIfOwner(automaticOperation)
            } else if candidate != nil {
                try await candidateClient.proveBonjourRelocation(
                    expectedBridgeId: persistedCredential.bridgeId ?? "",
                    nonce: relocationNonceGenerator()
                )
                // A selected Bonjour result can disappear between proof and
                // status. Re-check ownership before sending the bearer.
                try Task.checkCancellation()
                guard operationGeneration == localOperationGeneration else { return false }
                if let explicitToken {
                    guard explicitBonjourRelocationToken == explicitToken else { return false }
                }
                _ = try await candidateClient.verifyPairing()
            } else {
                // Manual entry and launch overrides are operator-selected paths;
                // retain their existing authenticated status-only verification.
                guard operationGeneration == localOperationGeneration, !Task.isCancelled else { return false }
                _ = try await candidateClient.verifyPairing()
            }

            // Do not alter the active client until secure persistence succeeds.
            guard operationGeneration == localOperationGeneration, !Task.isCancelled else { return false }
            guard explicitToken == nil || (explicitBonjourRelocationToken == explicitToken! && !Task.isCancelled) else {
                return false
            }
            guard automaticOperation == nil || (isAutomaticReconnectOwner(automaticOperation!) && !Task.isCancelled) else {
                return false
            }
            try credentialStore.save(candidateCredential)
            guard operationGeneration == localOperationGeneration, !Task.isCancelled else { return false }
            guard explicitToken == nil || (explicitBonjourRelocationToken == explicitToken! && !Task.isCancelled) else {
                return false
            }
            guard automaticOperation == nil || (isAutomaticReconnectOwner(automaticOperation!) && !Task.isCancelled) else {
                return false
            }
            client = candidateClient
            bridgeURLText = candidateClient.baseURL.absoluteString
            if let candidate {
                automaticBonjourConnectedCandidateID = candidate.id
            }
            stateBeforeBonjourRelocation = nil
            state = .connected
            message = automatic
                ? "Automatic reconnect succeeded. The saved bridge address was updated."
                : "Bridge address updated. The saved pairing was kept."
            return true
        } catch is CancellationError {
            if operationGeneration != localOperationGeneration { return false }
            if let explicitToken, explicitBonjourRelocationToken != explicitToken || Task.isCancelled {
                return false
            }
            if let automaticOperation {
                clearAutomaticReconnectVerificationIfOwner(automaticOperation)
                guard isAutomaticReconnectOwner(automaticOperation), !Task.isCancelled else { return false }
            }
            bridgeURLText = previousBridgeURLText
            state = stateAfterBonjourRelocationEnds()
            stateBeforeBonjourRelocation = nil
            message = "Address change was cancelled. The previous bridge address remains active."
            return false
        } catch {
            if operationGeneration != localOperationGeneration { return false }
            if let explicitToken, explicitBonjourRelocationToken != explicitToken || Task.isCancelled {
                return false
            }
            if let automaticOperation {
                clearAutomaticReconnectVerificationIfOwner(automaticOperation)
                guard isAutomaticReconnectOwner(automaticOperation), !Task.isCancelled else { return false }
            }
            // oldClient is intentionally retained; it remains the recovery path for every
            // candidate network, authentication, response, and Keychain failure.
            _ = oldClient
            bridgeURLText = previousBridgeURLText
            stateBeforeBonjourRelocation = nil
            fail(with: error)
            return false
        }
    }

    /// Immediately drops the local pairing. This operation is deliberately local: Forget never
    /// contacts the bridge, so it remains usable while offline or while Bonjour relocation is busy.
    public func forget() async {
        localOperationGeneration &+= 1
        // A launch task may still be unwinding after relocation cancellation. Do not let
        // its deferred automatic-reconnect step start a new browse after this reset.
        automaticBonjourLaunchStarted = true
        invalidateBonjourWorkForLocalReset()

        // Clear the bearer before touching storage. Even a Keychain failure must not leave an
        // active client or a browser able to publish the old pairing again.
        client?.clearCredential()
        client = nil
        lastBlock5Value = nil
        clearPage0Snapshot()
        state = .unconfigured

        do {
            try credentialStore.remove()
            message = "Saved pairing removed from this iPad. Pair or scan a QR code to connect again."
        } catch {
            // The Keychain may still contain the credential, but the app has failed safe:
            // there is no active bearer, no browser, and QR pairing is enabled.
            message = "The saved pairing could not be removed from this iPad. No active bearer was retained; try Forget again or scan a new pairing QR."
        }
    }

    public func readBlock5() async {
        guard !isBusy else { return }
        guard let client, client.hasCredential else {
            state = .authenticationRequired
            message = BridgeClientError.missingCredential.localizedDescription
            return
        }

        state = .reading
        message = nil
        let operationGeneration = localOperationGeneration
        do {
            let response = try await client.readBlock5()
            guard operationGeneration == localOperationGeneration else { return }
            lastBlock5Value = response.value
            publishAuthenticatedConnection()
            message = "Block 5 read successfully."
        } catch BridgeClientError.unauthorized {
            guard operationGeneration == localOperationGeneration else { return }
            handleUnauthorized()
        } catch is CancellationError {
            guard operationGeneration == localOperationGeneration else { return }
            restoreStateAfterCancellation()
        } catch {
            guard operationGeneration == localOperationGeneration else { return }
            fail(with: error)
        }
    }

    /// Targeted scan: block 4 + mirrors 5/6 + signal. Known tokens stop after decode;
    /// unknown tokens fetch the missing five blocks once and persist a local dump.
    public func scanPage0Token() async {
        guard !isBusy else { return }
        guard let client, client.hasCredential else {
            state = .authenticationRequired
            message = BridgeClientError.missingCredential.localizedDescription
            return
        }

        state = .scanningPage0
        message = nil
        lastUnknownDumpURL = nil
        let operationGeneration = localOperationGeneration
        do {
            let scan = try await client.scanPage0()
            guard operationGeneration == localOperationGeneration else { return }
            lastScanBlock4Value = scan.block4
            lastSignalMillivolts = scan.signalMillivolts
            lastPage0Block5Value = scan.block5
            lastPage0Block6Value = scan.block6

            guard let block5 = UInt32(scan.block5, radix: 16),
                  let block6 = UInt32(scan.block6, radix: 16) else {
                invalidatePage0Snapshot()
                failPage0("The bridge returned invalid page0 scan values.")
                return
            }

            let rideRead = RideBlockResolver.resolve(block5: block5, block6: block6)
            lastPage0Read = rideRead
            page0WriteSnapshot = rideRead.status == .success && rideRead.rides != nil
                ? Page0WriteSnapshot(block5: scan.block5, block6: scan.block6)
                : nil

            if rideRead.status == .success, let sequence = rideRead.sequence, let rides = rideRead.rides {
                publishAuthenticatedConnection()
                message = "Known \(sequence.rawValue) token with \(rides) rides. Block 4 \(scan.block4), signal \(scan.signalMillivolts) mV."
                return
            }

            let missing = try await client.readPage0MissingBlocks()
            guard operationGeneration == localOperationGeneration else { return }
            let blocks = try Page0ScanWorkflow.assemblePage0(scan: scan, missing: missing)
            let unknown = UnknownToken(blocks: blocks)
            do {
                lastUnknownDumpURL = try dumpStore.save(unknown)
                publishAuthenticatedConnection()
                message = RidesViewModel.unknownMessage
            } catch {
                lastUnknownDumpURL = nil
                let detail = (error as? LocalizedError)?.errorDescription ?? error.localizedDescription
                let failure = detail.isEmpty ? String(describing: error) : detail
                failPage0("Unknown token — log failed: \(failure)")
            }
        } catch BridgeClientError.unauthorized {
            guard operationGeneration == localOperationGeneration else { return }
            handleUnauthorized()
        } catch BridgeClientError.server(let code, _, _) where code == "no_chip" {
            guard operationGeneration == localOperationGeneration else { return }
            invalidatePage0Snapshot()
            state = .failed(RidesViewModel.noChipMessage)
            message = RidesViewModel.noChipMessage
        } catch BridgeClientError.server(let code, let serverMessage, _) where code == "lf_tune_failed" || code == "page0_read_failed" {
            guard operationGeneration == localOperationGeneration else { return }
            invalidatePage0Snapshot()
            failPage0("Page0 scan failed: \(serverMessage)")
        } catch is CancellationError {
            guard operationGeneration == localOperationGeneration else { return }
            invalidatePage0Snapshot()
            lastUnknownDumpURL = nil
            failPage0("Page0 scan was cancelled before completion.")
        } catch {
            guard operationGeneration == localOperationGeneration else { return }
            invalidatePage0Snapshot()
            lastUnknownDumpURL = nil
            failPage0("Page0 scan failed: \(errorDescription(error))")
        }
    }

    /// Reads a fresh pair of page0 mirrors. The raw pair is the only optimistic
    /// concurrency snapshot that can authorize a later set operation.
    public func readPage0Rides() async {
        guard !isBusy else { return }
        guard let client, client.hasCredential else {
            state = .authenticationRequired
            message = BridgeClientError.missingCredential.localizedDescription
            return
        }

        state = .readingPage0
        message = nil
        let operationGeneration = localOperationGeneration
        do {
            let response = try await client.readPage0Mirrors()
            guard operationGeneration == localOperationGeneration else { return }
            applyPage0Snapshot(block5: response.block5, block6: response.block6)
            publishAuthenticatedConnection()
            if lastPage0Read?.status == .success {
                message = "Page0 rides read successfully."
            } else {
                message = "Page0 mirrors read, but the ride encoding is unknown."
            }
        } catch BridgeClientError.unauthorized {
            guard operationGeneration == localOperationGeneration else { return }
            handleUnauthorized()
        } catch is CancellationError {
            guard operationGeneration == localOperationGeneration else { return }
            invalidatePage0Snapshot()
            failPage0("Page0 read was cancelled. Read page0 rides again before setting a value.")
        } catch {
            guard operationGeneration == localOperationGeneration else { return }
            invalidatePage0Snapshot()
            failPage0("Page0 read failed: \(errorDescription(error)). Read page0 rides again before setting a value.")
        }
    }

    /// Sets both page0 mirrors from the most recent successful raw snapshot.
    /// There is deliberately no retry or implicit refresh after an ambiguous result.
    public func setPage0Rides() async {
        guard !isBusy else { return }
        guard let client, client.hasCredential else {
            state = .authenticationRequired
            message = BridgeClientError.missingCredential.localizedDescription
            return
        }
        guard let read = lastPage0Read else {
            failPage0("Read page0 rides successfully first. A fresh mirror snapshot is required before setting rides.")
            return
        }
        guard read.status == .success, read.rides != nil else {
            failPage0("Setting page0 rides is disabled for unknown encoding. Read a known sequence token before setting rides.")
            return
        }
        guard let snapshot = page0WriteSnapshot, isWritablePage0Snapshot else {
            failPage0("Read page0 rides successfully first. A fresh mirror snapshot is required before setting rides.")
            return
        }
        guard let target = parsedTargetPage0Rides,
              let sequence = read.sequence,
              let desired = sequence.encode(target) else {
            failPage0("Target page0 rides must be a whole number from 0 through 500.")
            return
        }

        let operationGeneration = localOperationGeneration
        let desiredHex = String(format: "%08X", desired)
        do {
            let request = try BridgePage0MutationRequest(mutations: [
                try BridgePage0Mutation(block: 5, expected: snapshot.block5, desired: desiredHex),
                try BridgePage0Mutation(block: 6, expected: snapshot.block6, desired: desiredHex),
            ])
            state = .settingPage0
            message = nil
            let response = try await client.mutatePage0(request)
            guard operationGeneration == localOperationGeneration else { return }
            if response.status == "conflict" {
                invalidatePage0Snapshot()
                failPage0("Page0 set conflicted with a changed token. No blocks were written and no retry was sent; read page0 rides again before setting a value.")
                return
            }
            if response.status == "verifyFailed" {
                invalidatePage0Snapshot()
                failPage0("Page0 set verification failed (\(response.rollbackStatus)). No retry was sent; read page0 rides again before setting a value.")
                return
            }
            let actual = try actualValues(for: response, matching: request)
            applyPage0Snapshot(block5: actual.block5, block6: actual.block6)
            publishAuthenticatedConnection()
            if response.status == "alreadyApplied" {
                message = "Page0 rides already applied; no blocks were rewritten."
            } else {
                message = "Page0 rides written and verified."
            }
        } catch BridgeClientError.unauthorized {
            guard operationGeneration == localOperationGeneration else { return }
            invalidatePage0Snapshot()
            handleUnauthorized()
        } catch is CancellationError {
            guard operationGeneration == localOperationGeneration else { return }
            invalidatePage0Snapshot()
            failPage0("Page0 set was cancelled. No retry was sent; read page0 rides again before setting a value.")
        } catch {
            guard operationGeneration == localOperationGeneration else { return }
            // Conflict, verify failure, no chip, timeout, disconnect, and malformed
            // responses all invalidate the expected-value snapshot. Never replay them.
            invalidatePage0Snapshot()
            failPage0(mutationFailureMessage(error))
        }
    }

    /// Convenience for tests and non-text callers; the same bounded input path is used.
    public func setPage0Rides(_ rides: UInt) async {
        targetPage0RidesText = String(rides)
        await setPage0Rides()
    }

    public var canConfirmReset: Bool {
        selectedResetSequence != nil && !isBusy && isPaired
    }

    public func clearResetSelection() {
        selectedResetSequence = nil
    }

    /// Explicit profile reset: read blocks 1...6, plan conditional mutations, and apply only changed targets.
    public func confirmResetProfile() async {
        guard !isBusy else { return }
        guard let client, client.hasCredential else {
            state = .authenticationRequired
            message = BridgeClientError.missingCredential.localizedDescription
            return
        }
        guard let sequence = selectedResetSequence else {
            failPage0("Choose a reset profile before confirming.")
            return
        }

        let profile = ResetSequence.for(sequence)
        let operationGeneration = localOperationGeneration
        state = .resettingPage0
        message = nil
        do {
            let current = try await client.readPage0Blocks1To6()
            guard operationGeneration == localOperationGeneration else { return }
            let planned = try Page0ResetPlanningWorkflow.planMutations(
                currentBlocks: current.blocks,
                profile: profile
            )
            if planned.isEmpty {
                publishAuthenticatedConnection()
                message = "Reset profile already applied; no blocks were rewritten."
                return
            }

            let request = try BridgePage0MutationRequest(mutations: planned.map {
                try BridgePage0Mutation(block: $0.block, expected: $0.expected, desired: $0.desired)
            })
            let response = try await client.mutatePage0(request)
            guard operationGeneration == localOperationGeneration else { return }
            if response.status == "conflict" {
                invalidatePage0Snapshot()
                failPage0("Reset conflicted with a changed token. No blocks were written and no retry was sent; read page0 blocks again before resetting.")
                return
            }
            if response.status == "verifyFailed" {
                invalidatePage0Snapshot()
                failPage0("Reset verification failed (\(response.rollbackStatus)). No retry was sent; read page0 blocks again before resetting.")
                return
            }

            applyVerifiedResetResults(response, matching: request)
            publishAuthenticatedConnection()
            if response.status == "alreadyApplied" {
                message = "Reset profile already applied; no blocks were rewritten."
            } else {
                message = "Reset profile written and verified."
            }
        } catch BridgeClientError.unauthorized {
            guard operationGeneration == localOperationGeneration else { return }
            invalidatePage0Snapshot()
            handleUnauthorized()
        } catch is CancellationError {
            guard operationGeneration == localOperationGeneration else { return }
            invalidatePage0Snapshot()
            failPage0("Reset was cancelled. No retry was sent; read page0 blocks again before resetting.")
        } catch {
            guard operationGeneration == localOperationGeneration else { return }
            invalidatePage0Snapshot()
            failPage0(resetFailureMessage(error))
        }
    }

    private var parsedTargetPage0Rides: UInt? {
        guard !targetPage0RidesText.isEmpty,
              targetPage0RidesText.utf8.allSatisfy({ $0 >= 48 && $0 <= 57 }),
              let value = UInt(targetPage0RidesText),
              (RideBlockResolver.minimumRides...RideBlockResolver.maximumRides).contains(value) else {
            return nil
        }
        return value
    }

    private func actualValues(
        for response: BridgePage0MutationResponse,
        matching request: BridgePage0MutationRequest
    ) throws -> (block5: String, block6: String) {
        guard Set(response.results.map(\.block)) == Set(request.mutations.map(\.block)),
              response.results.allSatisfy({ result in
                  request.mutations.contains {
                      $0.block == result.block && $0.expected == result.expected && $0.desired == result.desired
                  }
              }),
              let block5 = response.results.first(where: { $0.block == 5 })?.actual,
              let block6 = response.results.first(where: { $0.block == 6 })?.actual else {
            throw BridgeClientError.invalidResponse
        }
        return (block5, block6)
    }

    private func applyPage0Snapshot(block5: String, block6: String) {
        guard let raw5 = UInt32(block5, radix: 16), let raw6 = UInt32(block6, radix: 16) else {
            invalidatePage0Snapshot()
            failPage0("The bridge returned invalid page0 mirror values. Read page0 rides again.")
            return
        }
        lastPage0Block5Value = block5
        lastPage0Block6Value = block6
        let read = RideBlockResolver.resolve(block5: raw5, block6: raw6)
        lastPage0Read = read
        // Keep unknown/malformed diagnostics visible, but never let them authorize
        // a write. The resolver must positively identify a known ride-sequence value.
        page0WriteSnapshot = read.status == .success && read.rides != nil
            ? Page0WriteSnapshot(block5: block5, block6: block6)
            : nil
    }

    private var isWritablePage0Snapshot: Bool {
        page0WriteSnapshot != nil && lastPage0Read?.status == .success && lastPage0Read?.rides != nil
    }

    private func handleUnauthorized() {
        try? credentialStore.remove()
        client?.clearCredential()
        client = nil
        lastBlock5Value = nil
        clearPage0Snapshot()
        state = .authenticationRequired
        message = BridgeClientError.unauthorized.localizedDescription
    }

    private func clearPage0Snapshot() {
        page0WriteSnapshot = nil
        lastPage0Block5Value = nil
        lastPage0Block6Value = nil
        lastPage0Read = nil
    }

    private func invalidatePage0Snapshot() {
        clearPage0Snapshot()
    }

    private func stateAfterBonjourRelocationEnds() -> BridgeConnectionState {
        guard isPaired else { return .unconfigured }
        return stateBeforeBonjourRelocation == .connected ? .connected : .restored
    }

    private func publishAuthenticatedConnection() {
        if isBonjourBrowsing,
           automaticBonjourConnectedCandidateID == nil,
           isPaired {
            state = .searching
        } else {
            state = .connected
        }
    }

    private func restoreStateAfterCancellation() {
        if isBonjourBrowsing,
           automaticBonjourConnectedCandidateID == nil,
           isPaired {
            state = .searching
        } else {
            state = isPaired ? .connected : .unconfigured
        }
        message = nil
    }

    private func failPage0(_ detail: String) {
        state = .failed(detail)
        message = detail
    }

    private func applyVerifiedResetResults(
        _ response: BridgePage0MutationResponse,
        matching request: BridgePage0MutationRequest
    ) {
        var verified: [Int: String] = [:]
        for result in response.results {
            guard let actual = result.actual else { continue }
            verified[result.block] = actual
        }
        lastResetBlockValues = verified
        if let block5 = verified[5], let block6 = verified[6] {
            applyPage0Snapshot(block5: block5, block6: block6)
        }
    }

    private func resetFailureMessage(_ error: Error) -> String {
        if case let BridgeClientError.server(code, _, _) = error, code == "conflict" {
            return "Reset conflicted with a changed token. No blocks were written and no retry was sent; read page0 blocks again before resetting."
        }
        if case let BridgeClientError.server(code, _, _) = error, code == "no_chip" {
            return "No supported T55xx chip was found. No retry was sent; read page0 blocks again after checking the token."
        }
        if let bridgeError = error as? BridgeClientError {
            return "Reset failed: \(bridgeError.localizedDescription) No retry was sent; read page0 blocks again before resetting."
        }
        return "Reset failed. No retry was sent; read page0 blocks again before resetting."
    }

    private func mutationFailureMessage(_ error: Error) -> String {
        if case let BridgeClientError.server(code, _, _) = error, code == "conflict" {
            return "Page0 set conflicted with a changed token. No blocks were written and no retry was sent; read page0 rides again before setting a value."
        }
        if case let BridgeClientError.server(code, _, _) = error, code == "no_chip" {
            return "No supported T55xx chip was found. No retry was sent; read page0 rides again after checking the token."
        }
        if let bridgeError = error as? BridgeClientError {
            return "Page0 set failed: \(bridgeError.localizedDescription) No retry was sent; read page0 rides again before setting a value."
        }
        return "Page0 set failed. No retry was sent; read page0 rides again before setting a value."
    }

    private func errorDescription(_ error: Error) -> String {
        if let bridgeError = error as? BridgeClientError { return bridgeError.localizedDescription }
        return "Check the local connection."
    }

    private func fail(with error: Error) {
        let actionable: String
        if let bridgeError = error as? BridgeClientError {
            actionable = bridgeError.localizedDescription
        } else if let storeError = error as? BridgeCredentialStoreError {
            actionable = storeError.localizedDescription
        } else if let localized = error as? LocalizedError, let description = localized.errorDescription {
            actionable = description
        } else {
            actionable = "Bridge operation failed. Check the local connection and try again."
        }
        state = .failed(actionable)
        message = actionable
    }
}
