import Foundation
import SwiftUI

public enum BridgeConnectionState: Equatable, Sendable {
    case unconfigured
    case pairing
    case restored
    case connected
    case reading
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
        case .forgetting: "Revoking pairing…"
        case .authenticationRequired: "Pairing required"
        case .failed: "Action failed"
        }
    }

    public var isBusy: Bool {
        switch self {
        case .pairing, .reading, .forgetting: true
        default: false
        }
    }

    public var isPaired: Bool {
        switch self {
        case .restored, .connected, .reading, .forgetting: true
        default: false
        }
    }
}

@MainActor
public final class BridgeConnectionModel: ObservableObject {
    @Published public var bridgeURLText: String
    @Published public private(set) var state: BridgeConnectionState
    @Published public private(set) var message: String?
    @Published public private(set) var lastBlock5Value: String?

    private let credentialStore: any BridgeCredentialStore
    private let session: URLSession
    private var client: BridgeClient?

    public init(
        credentialStore: any BridgeCredentialStore = KeychainBridgeCredentialStore(),
        session: URLSession = .shared,
        defaultBridgeURL: String = ""
    ) {
        self.credentialStore = credentialStore
        self.session = session
        self.bridgeURLText = defaultBridgeURL
        self.state = .unconfigured
        self.message = nil
        self.lastBlock5Value = nil
        restore()
    }

    public var isBusy: Bool { state.isBusy }
    /// A saved credential remains usable for retry/forget even after a transient failure.
    public var isPaired: Bool { client?.hasCredential == true }

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
        guard !isBusy else { return }
        guard !isPaired else {
            state = .connected
            message = "This iPad is already paired. Use Forget before pairing another bridge."
            return
        }
        state = .pairing
        message = nil
        lastBlock5Value = nil

        let newClient: BridgeClient
        do {
            newClient = try BridgeClient(baseURLString: bridgeURLText, session: session)
        } catch {
            fail(with: error)
            return
        }

        do {
            let credential = try await newClient.pair(pin: pin)
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
        } catch let error as CancellationError {
            restoreStateAfterCancellation()
        } catch {
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
        } catch let error as CancellationError {
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
            try? credentialStore.remove()
            client.clearCredential()
            self.client = nil
            state = .authenticationRequired
            message = BridgeClientError.unauthorized.localizedDescription
        } catch let error as CancellationError {
            restoreStateAfterCancellation()
        } catch {
            fail(with: error)
        }
    }

    private func clearLocalCredential() {
        do {
            try credentialStore.remove()
            client?.clearCredential()
            client = nil
            lastBlock5Value = nil
            state = .unconfigured
            message = "Saved pairing revoked and removed."
        } catch {
            // Keep the client so Forget can be retried. The server-side revoke has already succeeded.
            let detail = "Pairing was revoked, but the saved credential could not be removed. Tap Forget to retry."
            state = .failed(detail)
            message = detail
        }
    }

    private func restoreStateAfterCancellation() {
        state = isPaired ? .connected : .unconfigured
        message = nil
    }

    private func fail(with error: Error) {
        let actionable: String
        if let bridgeError = error as? BridgeClientError {
            actionable = bridgeError.localizedDescription
        } else if let storeError = error as? BridgeCredentialStoreError {
            actionable = storeError.localizedDescription
        } else {
            actionable = "Bridge operation failed. Check the local connection and try again."
        }
        state = .failed(actionable)
        message = actionable
    }
}
