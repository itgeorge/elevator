import Foundation
import SwiftUI

public enum BridgeConnectionState: Equatable, Sendable {
    case unconfigured
    case pairing
    case restored
    case connected
    case reading
    case readingMercury
    case settingMercury
    case relocating
    case forgetting
    case authenticationRequired
    case failed(String)

    public var title: String {
        switch self {
        case .unconfigured: "Pairing required"
        case .pairing: "Pairing…"
        case .restored: "Saved pairing restored"
        case .connected: "Connected"
        case .reading: "Reading block 5…"
        case .readingMercury: "Reading Mercury rides…"
        case .settingMercury: "Setting Mercury rides…"
        case .relocating: "Checking new bridge address…"
        case .forgetting: "Revoking pairing…"
        case .authenticationRequired: "Pairing required"
        case .failed: "Action failed"
        }
    }

    public var isBusy: Bool {
        switch self {
        case .pairing, .reading, .readingMercury, .settingMercury, .relocating, .forgetting: true
        default: false
        }
    }

    public var isPaired: Bool {
        switch self {
        case .restored, .connected, .reading, .readingMercury, .settingMercury, .relocating, .forgetting: true
        default: false
        }
    }
}

private struct MercuryWriteSnapshot: Equatable, Sendable {
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
    @Published public private(set) var lastMercuryBlock5Value: String?
    @Published public private(set) var lastMercuryBlock6Value: String?
    @Published public private(set) var lastMercuryRead: MercuryRideRead?
    @Published public private(set) var bonjourDiscoveryState: BridgeBonjourDiscoveryState
    @Published public private(set) var bonjourCandidates: [BridgeBonjourCandidate]
    @Published public private(set) var offeredBonjourCandidate: BridgeBonjourCandidate?
    @Published public private(set) var rejectedBonjourResultCount: Int
    @Published public var targetMercuryRidesText: String
#if DEBUG
    @Published public private(set) var physicalAcceptanceSummary: BridgePhysicalAcceptanceSummary?
    @Published public private(set) var physicalAcceptanceFailure: BridgePhysicalAcceptanceFailure?
#endif

    private let credentialStore: any BridgeCredentialStore
    private let session: URLSession
    private let healthRetryDelay: @Sendable () async throws -> Void
    private let now: @Sendable () -> Date
    private let bonjourBrowserSource: any BridgeBonjourBrowserSource
    private let bonjourQuiescenceDelay: @Sendable () async throws -> Void
    private let relocationNonceGenerator: @Sendable () -> String
    private var client: BridgeClient?
    private var bonjourBrowseGeneration = 0
    private var automaticBonjourBrowseGeneration: Int?
    private var automaticBonjourBridgeId: String?
    private var automaticBonjourLaunchStarted = false
    private var automaticBonjourSnapshot: [BridgeBonjourCandidate] = []
    private var automaticBonjourAttemptedCandidateIDs: Set<String> = []
    private var automaticReconnectTask: Task<Void, Never>?
    private var automaticReconnectPredecessorTask: Task<Void, Never>?
    private var automaticReconnectVerification: (operation: AutomaticReconnectOperation, task: Task<BridgePairStatusResponse, Error>)?
    private var automaticReconnectMigrationOperation: AutomaticReconnectOperation?
    private var automaticReconnectToken = 0
    private var explicitBonjourRelocationTask: Task<Bool, Never>?
    private var explicitBonjourRelocationToken = 0
    private var mercuryWriteSnapshot: MercuryWriteSnapshot?
    private var credentialRestoreFailed = false
    private var launchAddressOverrideApplied = false
    private var launchAddressOverrideWasProvided = false
#if DEBUG
    private var launchPhysicalAcceptanceAttempted = false
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
        }
    ) {
        self.credentialStore = credentialStore
        self.session = session
        self.healthRetryDelay = healthRetryDelay
        self.now = now
        self.bonjourBrowserSource = bonjourBrowserSource
        self.bonjourQuiescenceDelay = bonjourQuiescenceDelay
        self.relocationNonceGenerator = relocationNonceGenerator
        self.bridgeURLText = defaultBridgeURL
        self.state = .unconfigured
        self.message = nil
        self.lastBlock5Value = nil
        self.lastMercuryBlock5Value = nil
        self.lastMercuryBlock6Value = nil
        self.lastMercuryRead = nil
        self.bonjourDiscoveryState = .idle
        self.bonjourCandidates = []
        self.offeredBonjourCandidate = nil
        self.rejectedBonjourResultCount = 0
        self.targetMercuryRidesText = ""
#if DEBUG
        self.physicalAcceptanceSummary = nil
        self.physicalAcceptanceFailure = nil
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
    /// A saved credential remains usable for retry/forget even after a transient failure.
    public var isPaired: Bool { client?.hasCredential == true }

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

    public var hasFreshMercurySnapshot: Bool { mercuryWriteSnapshot != nil }
    public var resolvedMercuryRides: UInt? { lastMercuryRead?.rides }
    public var mercurySourceBlockNumber: Int? { lastMercuryRead?.sourceBlockNumber }
    public var mercuryBlocksMatch: Bool? { lastMercuryRead.map(\.blocksMatched) }
    public var mercuryWarningMessage: String? { lastMercuryRead?.warningMessage }
    public var mercuryWarningDisplay: String? {
        guard let read = lastMercuryRead else { return nil }
        if let warning = read.warningMessage { return warning }
        return read.status == .unknownEncodingSequence
            ? "Warning: Mercury mirror encoding is unknown."
            : "None"
    }

    public var canSetMercuryRides: Bool {
        !isBusy && isPaired && isWritableMercurySnapshot && parsedTargetMercuryRides != nil
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
            state = .connected
            message = "This iPad is already paired. Use Forget before pairing another bridge."
            return
        }
        state = .pairing
        message = nil
        clearMercurySnapshot()
        lastBlock5Value = nil

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
            do {
                try credentialStore.save(credential)
            } catch {
                // Pairing already created a valid bearer. Revoke it before dropping the only reference.
                do {
                    try await newClient.revoke()
                    newClient.clearCredential()
                    client = nil
                    let detail = "Pairing succeeded, but the secure credential could not be saved. The server pairing was revoked; pair again."
                    state = .failed(detail)
                    message = detail
                } catch BridgeClientError.unauthorized {
                    newClient.clearCredential()
                    client = nil
                    let detail = "Pairing succeeded, but the secure credential could not be saved. The server pairing was already invalid; pair again."
                    state = .failed(detail)
                    message = detail
                } catch {
                    // Keep the live bearer reachable through Forget so it can be revoked on retry.
                    client = newClient
                    bridgeURLText = credential.baseURL.absoluteString
                    let detail = "Pairing could not be saved or revoked. Keep this app open and tap Forget to retry revocation."
                    state = .failed(detail)
                    message = detail
                }
                return
            }
            client = newClient
            bridgeURLText = credential.baseURL.absoluteString
            state = .connected
            message = "Paired with the local bridge."
        } catch is CancellationError {
            restoreStateAfterCancellation()
        } catch {
            fail(with: error)
        }
    }

    /// Imports one strict backend QR payload and immediately uses the existing one-shot health
    /// preflight plus PIN pairing path. A parsed payload is never replayed by this method.
    public func importPairingPayload(_ json: String) async {
        guard !isBusy else { return }
        guard !isPaired else {
            state = .connected
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
            state = .connected
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
        }
        cancelAutomaticReconnect()
        bonjourCandidates = []
        offeredBonjourCandidate = nil
        rejectedBonjourResultCount = 0
        bonjourDiscoveryState = .browsing
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
            state = isPaired ? .connected : .unconfigured
        }
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
                state = isPaired ? .connected : .unconfigured
            }
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
                state = isPaired ? .connected : .unconfigured
            }
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
        if !isAutomatic,
           explicitBonjourRelocationTask != nil,
           previousBonjourCandidates != snapshot.candidates {
            // A selected TXT service is no longer the same candidate. Cancel its
            // proof/status owner before accepting the new browser snapshot.
            if state == .relocating {
                bridgeURLText = client?.baseURL.absoluteString ?? bridgeURLText
                state = isPaired ? .connected : .unconfigured
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
            if state == .relocating {
                state = isPaired ? .connected : .unconfigured
            }
            cancelAutomaticReconnect(preserveVerification: true)
        }

        switch snapshot.candidates.count {
        case 0:
            bonjourDiscoveryState = .browsing
            message = snapshot.rejectedResultCount == 0
                ? (isAutomatic ? "Searching for the saved bridge over Bonjour…" : "Searching for compatible local RidesBridge services…")
                : "No compatible RidesBridge service was found. Check the bridge version and TXT record, or use manual entry."
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
#endif

    /// Moves a saved bearer to a new local bridge address transactionally.
    /// Verification and persistence happen before replacing the active client; failures
    /// therefore leave the old client and stored credential available for recovery.
    public func useEnteredBridgeAddress() async {
        _ = await migrateEnteredBridgeAddress()
    }

    /// The shared address migration transaction used by manual entry and Bonjour.
    /// The Boolean lets Bonjour restore the exact pre-selection text even when the
    /// HTTP request is cancelled and the model returns to `.connected`.
    @discardableResult
    private func migrateEnteredBridgeAddress(
        savedCredential: BridgeCredential? = nil,
        automatic: Bool = false,
        candidate: BridgeBonjourCandidate? = nil,
        automaticOperation: AutomaticReconnectOperation? = nil,
        explicitToken: Int? = nil
    ) async -> Bool {
        guard !isBusy else { return false }
        guard let oldClient = client, oldClient.hasCredential else {
            state = .authenticationRequired
            message = BridgeClientError.missingCredential.localizedDescription
            return false
        }

        let previousBridgeURLText = oldClient.baseURL.absoluteString
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
                if let explicitToken {
                    guard explicitBonjourRelocationToken == explicitToken else { return false }
                }
                _ = try await candidateClient.verifyPairing()
            } else {
                // Manual entry and launch overrides are operator-selected paths;
                // retain their existing authenticated status-only verification.
                _ = try await candidateClient.verifyPairing()
            }

            // Do not alter the active client until secure persistence succeeds.
            guard explicitToken == nil || (explicitBonjourRelocationToken == explicitToken! && !Task.isCancelled) else {
                return false
            }
            guard automaticOperation == nil || (isAutomaticReconnectOwner(automaticOperation!) && !Task.isCancelled) else {
                return false
            }
            try credentialStore.save(candidateCredential)
            guard explicitToken == nil || (explicitBonjourRelocationToken == explicitToken! && !Task.isCancelled) else {
                return false
            }
            guard automaticOperation == nil || (isAutomaticReconnectOwner(automaticOperation!) && !Task.isCancelled) else {
                return false
            }
            client = candidateClient
            bridgeURLText = candidateClient.baseURL.absoluteString
            state = .connected
            message = automatic
                ? "Automatic reconnect succeeded. The saved bridge address was updated."
                : "Bridge address updated. The saved pairing was kept."
            return true
        } catch is CancellationError {
            if let explicitToken, explicitBonjourRelocationToken != explicitToken || Task.isCancelled {
                return false
            }
            if let automaticOperation {
                clearAutomaticReconnectVerificationIfOwner(automaticOperation)
                guard isAutomaticReconnectOwner(automaticOperation), !Task.isCancelled else { return false }
            }
            bridgeURLText = previousBridgeURLText
            state = .connected
            message = "Address change was cancelled. The previous bridge address remains active."
            return false
        } catch {
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
            fail(with: error)
            return false
        }
    }

    public func forget() async {
        guard !isBusy else { return }
        guard let client, client.hasCredential else {
            clearLocalCredential()
            return
        }

        state = .forgetting
        message = nil
        do {
            try await client.revoke()
            clearLocalCredential()
        } catch BridgeClientError.unauthorized {
            // The server already considers this credential invalid; local cleanup is safe.
            clearLocalCredential()
        } catch is CancellationError {
            restoreStateAfterCancellation()
        } catch {
            // Keep the client and store intact so the operator can retry Forget and revoke later.
            fail(with: error)
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
        do {
            let response = try await client.readBlock5()
            lastBlock5Value = response.value
            state = .connected
            message = "Block 5 read successfully."
        } catch BridgeClientError.unauthorized {
            handleUnauthorized()
        } catch is CancellationError {
            restoreStateAfterCancellation()
        } catch {
            fail(with: error)
        }
    }

    /// Reads a fresh pair of Mercury mirrors. The raw pair is the only optimistic
    /// concurrency snapshot that can authorize a later set operation.
    public func readMercuryRides() async {
        guard !isBusy else { return }
        guard let client, client.hasCredential else {
            state = .authenticationRequired
            message = BridgeClientError.missingCredential.localizedDescription
            return
        }

        state = .readingMercury
        message = nil
        do {
            let response = try await client.readMercuryMirrors()
            applyMercurySnapshot(block5: response.block5, block6: response.block6)
            state = .connected
            if lastMercuryRead?.status == .success {
                message = "Mercury rides read successfully."
            } else {
                message = "Mercury mirrors read, but the ride encoding is unknown."
            }
        } catch BridgeClientError.unauthorized {
            handleUnauthorized()
        } catch is CancellationError {
            invalidateMercurySnapshot()
            failMercury("Mercury read was cancelled. Read Mercury rides again before setting a value.")
        } catch {
            invalidateMercurySnapshot()
            failMercury("Mercury read failed: \(errorDescription(error)). Read Mercury rides again before setting a value.")
        }
    }

    /// Sets both Mercury mirrors from the most recent successful raw snapshot.
    /// There is deliberately no retry or implicit refresh after an ambiguous result.
    public func setMercuryRides() async {
        guard !isBusy else { return }
        guard let client, client.hasCredential else {
            state = .authenticationRequired
            message = BridgeClientError.missingCredential.localizedDescription
            return
        }
        guard let read = lastMercuryRead else {
            failMercury("Read Mercury rides successfully first. A fresh mirror snapshot is required before setting rides.")
            return
        }
        guard read.status == .success, read.rides != nil else {
            failMercury("Setting Mercury rides is disabled for unknown encoding. Read a known Mercury token before setting rides.")
            return
        }
        guard let snapshot = mercuryWriteSnapshot, isWritableMercurySnapshot else {
            failMercury("Read Mercury rides successfully first. A fresh mirror snapshot is required before setting rides.")
            return
        }
        guard let target = parsedTargetMercuryRides,
              let desired = MercuryRideCodec.encode(target) else {
            failMercury("Target Mercury rides must be a whole number from 0 through 500.")
            return
        }

        let desiredHex = String(format: "%08X", desired)
        do {
            let request = try BridgeMercuryMutationRequest(mutations: [
                try BridgeMercuryMutation(block: 5, expected: snapshot.block5, desired: desiredHex),
                try BridgeMercuryMutation(block: 6, expected: snapshot.block6, desired: desiredHex),
            ])
            state = .settingMercury
            message = nil
            let response = try await client.mutateMercury(request)
            if response.status == "conflict" {
                invalidateMercurySnapshot()
                failMercury("Mercury set conflicted with a changed token. No blocks were written and no retry was sent; read Mercury rides again before setting a value.")
                return
            }
            if response.status == "verifyFailed" {
                invalidateMercurySnapshot()
                failMercury("Mercury set verification failed (\(response.rollbackStatus)). No retry was sent; read Mercury rides again before setting a value.")
                return
            }
            let actual = try actualValues(for: response, matching: request)
            applyMercurySnapshot(block5: actual.block5, block6: actual.block6)
            state = .connected
            if response.status == "alreadyApplied" {
                message = "Mercury rides already applied; no blocks were rewritten."
            } else {
                message = "Mercury rides written and verified."
            }
        } catch BridgeClientError.unauthorized {
            invalidateMercurySnapshot()
            handleUnauthorized()
        } catch is CancellationError {
            invalidateMercurySnapshot()
            failMercury("Mercury set was cancelled. No retry was sent; read Mercury rides again before setting a value.")
        } catch {
            // Conflict, verify failure, no chip, timeout, disconnect, and malformed
            // responses all invalidate the expected-value snapshot. Never replay them.
            invalidateMercurySnapshot()
            failMercury(mutationFailureMessage(error))
        }
    }

    /// Convenience for tests and non-text callers; the same bounded input path is used.
    public func setMercuryRides(_ rides: UInt) async {
        targetMercuryRidesText = String(rides)
        await setMercuryRides()
    }

    private var parsedTargetMercuryRides: UInt? {
        guard !targetMercuryRidesText.isEmpty,
              targetMercuryRidesText.utf8.allSatisfy({ $0 >= 48 && $0 <= 57 }),
              let value = UInt(targetMercuryRidesText),
              (MercuryRideCodec.minimumRides...MercuryRideCodec.maximumRides).contains(value) else {
            return nil
        }
        return value
    }

    private func actualValues(
        for response: BridgeMercuryMutationResponse,
        matching request: BridgeMercuryMutationRequest
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

    private func applyMercurySnapshot(block5: String, block6: String) {
        guard let raw5 = UInt32(block5, radix: 16), let raw6 = UInt32(block6, radix: 16) else {
            invalidateMercurySnapshot()
            failMercury("The bridge returned invalid Mercury mirror values. Read Mercury rides again.")
            return
        }
        lastMercuryBlock5Value = block5
        lastMercuryBlock6Value = block6
        let read = MercuryMirrorResolver.resolve(block5: raw5, block6: raw6)
        lastMercuryRead = read
        // Keep unknown/malformed diagnostics visible, but never let them authorize
        // a write. The resolver must positively identify a Mercury ride value.
        mercuryWriteSnapshot = read.status == .success && read.rides != nil
            ? MercuryWriteSnapshot(block5: block5, block6: block6)
            : nil
    }

    private var isWritableMercurySnapshot: Bool {
        mercuryWriteSnapshot != nil && lastMercuryRead?.status == .success && lastMercuryRead?.rides != nil
    }

    private func clearLocalCredential() {
        do {
            try credentialStore.remove()
            client?.clearCredential()
            client = nil
            lastBlock5Value = nil
            clearMercurySnapshot()
            state = .unconfigured
            message = "Saved pairing revoked and removed."
        } catch {
            // Keep the client so Forget can be retried. The server-side revoke has already succeeded.
            let detail = "Pairing was revoked, but the saved credential could not be removed. Tap Forget to retry."
            state = .failed(detail)
            message = detail
        }
    }

    private func handleUnauthorized() {
        try? credentialStore.remove()
        client?.clearCredential()
        client = nil
        lastBlock5Value = nil
        clearMercurySnapshot()
        state = .authenticationRequired
        message = BridgeClientError.unauthorized.localizedDescription
    }

    private func clearMercurySnapshot() {
        mercuryWriteSnapshot = nil
        lastMercuryBlock5Value = nil
        lastMercuryBlock6Value = nil
        lastMercuryRead = nil
    }

    private func invalidateMercurySnapshot() {
        clearMercurySnapshot()
    }

    private func restoreStateAfterCancellation() {
        state = isPaired ? .connected : .unconfigured
        message = nil
    }

    private func failMercury(_ detail: String) {
        state = .failed(detail)
        message = detail
    }

    private func mutationFailureMessage(_ error: Error) -> String {
        if case let BridgeClientError.server(code, _, _) = error, code == "conflict" {
            return "Mercury set conflicted with a changed token. No blocks were written and no retry was sent; read Mercury rides again before setting a value."
        }
        if case let BridgeClientError.server(code, _, _) = error, code == "no_chip" {
            return "No supported T55xx chip was found. No retry was sent; read Mercury rides again after checking the token."
        }
        if let bridgeError = error as? BridgeClientError {
            return "Mercury set failed: \(bridgeError.localizedDescription) No retry was sent; read Mercury rides again before setting a value."
        }
        return "Mercury set failed. No retry was sent; read Mercury rides again before setting a value."
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
