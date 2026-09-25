import Foundation

/// Translates bridge raw page0 results into Concept A domain outcomes.
/// The view model never sees HTTP DTOs, bearer tokens, or PM3 command names.
public final class NetworkRideTokenDevice: RideTokenDevice, @unchecked Sendable {
    private let client: BridgeClient

    public init(client: BridgeClient) {
        self.client = client
    }

    public func scan() async -> ScanOutcome {
        do {
            let scan = try await client.scanPage0()
            guard let block4 = UInt32(scan.block4, radix: 16),
                  let block5 = UInt32(scan.block5, radix: 16),
                  let block6 = UInt32(scan.block6, radix: 16) else {
                return .failure("The bridge returned invalid scan values.")
            }

            let rideRead = RideBlockResolver.resolve(block5: block5, block6: block6)
            if rideRead.status == .success,
               let sequence = rideRead.sequence,
               let rides = rideRead.rides {
                var blocks = [UInt32](repeating: 0, count: 8)
                blocks[4] = block4
                blocks[5] = block5
                blocks[6] = block6
                let token = Token(blocks: blocks, rideCount: rides, sequence: sequence)
                return .known(token, signalMillivolts: scan.signalMillivolts)
            }

            let missing = try await client.readPage0MissingBlocks()
            let assembled = try Page0ScanWorkflow.assemblePage0(scan: scan, missing: missing)
            return .unknown(UnknownToken(blocks: assembled), signalMillivolts: scan.signalMillivolts)
        } catch BridgeClientError.server(let code, _, _) where code == "no_chip" {
            return .noChip
        } catch BridgeClientError.server(let code, let message, _) where code == "lf_tune_failed" || code == "page0_read_failed" {
            return .failure("Scan failed: \(message)")
        } catch is CancellationError {
            return .failure("Scan was cancelled before completion.")
        } catch {
            return .failure(Self.describe(error, operation: "Scan"))
        }
    }

    public func writeRideMirrors(_ request: RideMirrorWriteRequest) async -> WriteOutcome {
        guard let desired = request.token.sequence.encode(request.desiredRides) else {
            return .failure("Target rides must be a whole number from 0 through 500.")
        }
        let desiredHex = Token.hex(desired)
        let expected5 = Token.hex(request.expectedBlock5)
        let expected6 = Token.hex(request.expectedBlock6)

        do {
            let mutationRequest = try BridgePage0MutationRequest(mutations: [
                try BridgePage0Mutation(block: 5, expected: expected5, desired: desiredHex),
                try BridgePage0Mutation(block: 6, expected: expected6, desired: desiredHex),
            ])
            let response = try await client.mutatePage0(mutationRequest)
            switch response.status {
            case "written", "alreadyApplied":
                let actual = try Self.verifiedMirrorValues(for: response, matching: mutationRequest)
                guard actual.block5 == desiredHex, actual.block6 == desiredHex else {
                    return .requiresRefresh("Charge verification did not match the requested rides. Tap Detect and try again.")
                }
                return .success
            case "conflict":
                return .requiresRefresh("Charge conflicted with a changed token. Tap Detect and try again.")
            case "verifyFailed":
                return .requiresRefresh("Charge verification failed (\(response.rollbackStatus)). Tap Detect and try again.")
            default:
                return .requiresRefresh("Charge returned an unexpected result. Tap Detect and try again.")
            }
        } catch BridgeClientError.server(let code, _, _) where code == "conflict" {
            return .requiresRefresh("Charge conflicted with a changed token. Tap Detect and try again.")
        } catch BridgeClientError.server(let code, _, _) where code == "no_chip" {
            return .requiresRefresh("No supported T55xx chip was found. Tap Detect and try again.")
        } catch is CancellationError {
            return .requiresRefresh("Charge was cancelled. Tap Detect and try again.")
        } catch {
            // Ambiguous network/timeout failures require refresh; never blind-retry.
            return .requiresRefresh(Self.describe(error, operation: "Charge") + " Tap Detect and try again.")
        }
    }

    public func reset(_ request: ResetMutationRequest) async -> ResetOutcome {
        let profile = ResetSequence.for(request.sequence)
        do {
            let current = try await client.readPage0Blocks1To6()
            let planned = try Page0ResetPlanningWorkflow.planMutations(
                currentBlocks: current.blocks,
                profile: profile
            )
            if planned.isEmpty {
                return .alreadyApplied(Self.token(from: profile, verified: [:]))
            }

            let mutationRequest = try BridgePage0MutationRequest(mutations: planned.map {
                try BridgePage0Mutation(block: $0.block, expected: $0.expected, desired: $0.desired)
            })
            let response = try await client.mutatePage0(mutationRequest)
            switch response.status {
            case "written", "alreadyApplied":
                let verified = Self.verifiedBlockMap(response)
                let token = Self.token(from: profile, verified: verified)
                return response.status == "alreadyApplied" ? .alreadyApplied(token) : .success(token)
            case "conflict":
                return .requiresRefresh("Reset conflicted with a changed token. Tap Detect and try again.")
            case "verifyFailed":
                return .requiresRefresh("Reset verification failed (\(response.rollbackStatus)). Tap Detect and try again.")
            default:
                return .requiresRefresh("Reset returned an unexpected result. Tap Detect and try again.")
            }
        } catch BridgeClientError.server(let code, _, _) where code == "conflict" {
            return .requiresRefresh("Reset conflicted with a changed token. Tap Detect and try again.")
        } catch BridgeClientError.server(let code, _, _) where code == "no_chip" {
            return .requiresRefresh("No supported T55xx chip was found. Tap Detect and try again.")
        } catch is CancellationError {
            return .requiresRefresh("Reset was cancelled. Tap Detect and try again.")
        } catch {
            return .requiresRefresh(Self.describe(error, operation: "Reset") + " Tap Detect and try again.")
        }
    }

    private static func verifiedMirrorValues(
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

    private static func verifiedBlockMap(_ response: BridgePage0MutationResponse) -> [Int: UInt32] {
        var verified: [Int: UInt32] = [:]
        for result in response.results {
            guard let actual = result.actual, let value = UInt32(actual, radix: 16) else { continue }
            verified[result.block] = value
        }
        return verified
    }

    private static func token(from profile: ResetSequence, verified: [Int: UInt32]) -> Token {
        var blocks = profile.resetImage()
        for (block, value) in verified where (1...6).contains(block) {
            blocks[block] = value
        }
        switch TokenDecoder.decode(blocks: blocks) {
        case .known(let token):
            return token
        case .unknown, .noChip:
            return Token(blocks: blocks, rideCount: profile.resetRideCount, sequence: profile.sequence)
        }
    }

    private static func describe(_ error: Error, operation: String) -> String {
        if let bridgeError = error as? BridgeClientError {
            return "\(operation) failed: \(bridgeError.localizedDescription)"
        }
        let detail = (error as? LocalizedError)?.errorDescription ?? error.localizedDescription
        if detail.isEmpty {
            return "\(operation) failed."
        }
        return "\(operation) failed: \(detail)"
    }
}
