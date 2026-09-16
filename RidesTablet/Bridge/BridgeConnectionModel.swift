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

@MainActor
public final class BridgeConnectionModel: ObservableObject {
    @Published public var bridgeURLText: String
    @Published public private(set) var state: BridgeConnectionState
    @Published public private(set) var message: String?
    @Published public private(set) var lastBlock5Value: String?
    @Published public private(set) var lastMercuryBlock5Value: String?
    @Published public private(set) var lastMercuryBlock6Value: String?
    @Published public private(set) var lastMercuryRead: MercuryRideRead?
    @Published public var targetMercuryRidesText: String
#if DEBUG
    @Published public private(set) var physicalAcceptanceSummary: BridgePhysicalAcceptanceSummary?
    @Published public private(set) var physicalAcceptanceFailure: BridgePhysicalAcceptanceFailure?
#endif

    private let credentialStore: any BridgeCredentialStore
    private let session: URLSession
    private let healthRetryDelay: @Sendable () async throws -> Void
    private let now: @Sendable () -> Date
    private var client: BridgeClient?
    private var mercuryWriteSnapshot: MercuryWriteSnapshot?
    private var launchAddressOverrideApplied = false
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
        now: @escaping @Sendable () -> Date = { Date() }
    ) {
        self.credentialStore = credentialStore
        self.session = session
        self.healthRetryDelay = healthRetryDelay
        self.now = now
        self.bridgeURLText = defaultBridgeURL
        self.state = .unconfigured
        self.message = nil
        self.lastBlock5Value = nil
        self.lastMercuryBlock5Value = nil
        self.lastMercuryBlock6Value = nil
        self.lastMercuryRead = nil
        self.targetMercuryRidesText = ""
#if DEBUG
        self.physicalAcceptanceSummary = nil
        self.physicalAcceptanceFailure = nil
#endif
        restore()
    }

    public var isBusy: Bool { state.isBusy }
    /// A saved credential remains usable for retry/forget even after a transient failure.
    public var isPaired: Bool { client?.hasCredential == true }

    /// Address migration is only offered for a credential that is actually persisted.
    public var hasSavedCredential: Bool {
        do { return try credentialStore.load() != nil }
        catch { return false }
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
                client = nil
                state = .unconfigured
                return
            }
            let restoredClient = try BridgeClient(baseURL: credential.baseURL, session: session, credential: credential)
            client = restoredClient
            bridgeURLText = restoredClient.baseURL.absoluteString
            state = .restored
            message = "Saved pairing restored. Readiness will be checked on the next operation."
        } catch {
            client = nil
            let detail = "Saved pairing could not be restored. Pair again."
            state = .failed(detail)
            message = detail
        }
    }

    public func pair(pin: String) async {
        await pair(pin: pin, bridgeId: nil, retryHealthOnUnreachable: true)
    }

    /// Pairs once with an optional stable bridge identity. Manual pairing passes nil; QR pairing
    /// supplies the parsed identity so the initial Keychain save is complete and transactional.
    public func pair(pin: String, bridgeId: String?) async {
        // Supplying a bridge identity denotes QR pairing. Keep the legacy health
        // transition retry only for the manual, identity-less entry point.
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

    /// Applies the launch-only address override once. Unpaired sessions are only prefilled;
    /// paired sessions use the same transactional migration as the manual action.
    public func applyLaunchAddressOverride(_ value: String?) async {
        let override = value?.trimmingCharacters(in: .whitespacesAndNewlines) ?? ""
        guard !override.isEmpty, !launchAddressOverrideApplied else { return }
        launchAddressOverrideApplied = true
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
        guard !isBusy else { return }
        guard let oldClient = client, oldClient.hasCredential else {
            state = .authenticationRequired
            message = BridgeClientError.missingCredential.localizedDescription
            return
        }

        let previousBridgeURLText = oldClient.baseURL.absoluteString
        state = .relocating
        message = nil

        do {
            guard let savedCredential = try credentialStore.load() else {
                throw BridgeClientError.missingCredential
            }
            let candidateURL = try BridgeClient.normalizeBaseURL(bridgeURLText)
            let candidateCredential = BridgeCredential(
                baseURL: candidateURL,
                accessToken: savedCredential.accessToken,
                tokenType: savedCredential.tokenType,
                bridgeId: savedCredential.bridgeId
            )
            let candidateClient = try BridgeClient(
                baseURL: candidateURL,
                session: session,
                credential: candidateCredential
            )
            _ = try await candidateClient.verifyPairing()

            // Do not alter the active client until secure persistence succeeds.
            try credentialStore.save(candidateCredential)
            client = candidateClient
            bridgeURLText = candidateClient.baseURL.absoluteString
            state = .connected
            message = "Bridge address updated. The saved pairing was kept."
        } catch is CancellationError {
            bridgeURLText = previousBridgeURLText
            state = .connected
            message = "Address change was cancelled. The previous bridge address remains active."
        } catch {
            // oldClient is intentionally retained; it remains the recovery path for every
            // candidate network, authentication, response, and Keychain failure.
            _ = oldClient
            bridgeURLText = previousBridgeURLText
            fail(with: error)
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
