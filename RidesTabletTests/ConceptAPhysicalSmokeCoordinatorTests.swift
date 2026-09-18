#if DEBUG

import Foundation
import XCTest
@testable import RidesTablet

@MainActor
final class ConceptAPhysicalSmokeCoordinatorTests: XCTestCase {
    func testLaunchConfigurationUsesExactSlice5TriggerKeyAndValue() {
        let enabled = BridgeConnectionLaunchConfiguration(environment: [
            "RIDES_SLICE5_CONCEPTA_SMOKE": "1"
        ])
        XCTAssertTrue(enabled.slice5ConceptASmokeEnabled)
        XCTAssertFalse(BridgeConnectionLaunchConfiguration(environment: [
            "RIDES_SLICE5_CONCEPTA_SMOKE": "true"
        ]).slice5ConceptASmokeEnabled)
    }

    func testSmokePassesForNonVenusRegisteredToken() async {
        let token = Token.sample(rideCount: 500, sequence: .nix)
        let fake = ConceptAPhysicalSmokeFakeDevice(token: token, signalMillivolts: 46354)
        var log: [String] = []
        let coordinator = ConceptAPhysicalSmokeCoordinator(device: fake, log: { log.append($0) })

        let result = await coordinator.run()

        guard case .success(let summary) = result else {
            return XCTFail("Expected success, got \(result)")
        }
        XCTAssertEqual(summary.sequence, .nix)
        XCTAssertEqual(summary.detectedRides, 500)
        XCTAssertEqual(summary.chargedRides, 490)
        XCTAssertEqual(summary.resetRides, 0)
        XCTAssertEqual(fake.token.rideCount, 500)
        XCTAssertEqual(fake.token.sequence, .nix)
        XCTAssertTrue(log.contains(where: { $0.contains("RIDES_SLICE5_SMOKE_DETECT sequence=nix rides=500") }))
        XCTAssertTrue(log.contains(where: { $0.contains("RIDES_SLICE5_SMOKE_RESTORE rides=500 sequence=nix") }))
    }

    func testSmokeChargeIncreasesWhenDetectedRidesBelowTen() async {
        let token = Token.sample(rideCount: 5, sequence: .nix)
        let fake = ConceptAPhysicalSmokeFakeDevice(token: token, signalMillivolts: 333)
        let coordinator = ConceptAPhysicalSmokeCoordinator(device: fake)

        let result = await coordinator.run()

        guard case .success(let summary) = result else {
            return XCTFail("Expected success, got \(result)")
        }
        XCTAssertEqual(summary.detectedRides, 5)
        XCTAssertEqual(summary.chargedRides, 15)
        XCTAssertEqual(fake.token.rideCount, 5)
    }
}

/// Stateful fake that mirrors the Concept A smoke mutation path for any registered sequence.
private final class ConceptAPhysicalSmokeFakeDevice: RideTokenDevice, @unchecked Sendable {
    private let lock = NSLock()
    private var storedToken: Token
    private let storedSignal: Int

    init(token: Token, signalMillivolts: Int) {
        storedToken = token
        storedSignal = signalMillivolts
    }

    var token: Token {
        withLock { storedToken }
    }

    func scan() async -> ScanOutcome {
        await Task.yield()
        return withLock { .known(storedToken, signalMillivolts: storedSignal) }
    }

    func writeRideMirrors(_ request: RideMirrorWriteRequest) async -> WriteOutcome {
        await Task.yield()
        return withLock {
            if request.token.block5 == 0x11111111, request.token.block6 == 0x22222222 {
                return .requiresRefresh("Charge conflicted with a changed token. Tap Detect and try again.")
            }
            guard let encoded = request.token.sequence.encode(request.desiredRides) else {
                return .failure("Target rides must be a whole number from 0 through 500.")
            }
            var blocks = request.token.blocks
            blocks[5] = encoded
            blocks[6] = encoded
            storedToken = Token(blocks: blocks, rideCount: request.desiredRides, sequence: request.token.sequence)
            return .success
        }
    }

    func reset(_ request: ResetMutationRequest) async -> ResetOutcome {
        await Task.yield()
        return withLock {
            let image = ResetSequence.for(request.sequence).resetImage()
            storedToken = Token(blocks: image, rideCount: 0, sequence: request.sequence)
            return .success(storedToken)
        }
    }

    private func withLock<T>(_ body: () -> T) -> T {
        lock.lock()
        defer { lock.unlock() }
        return body()
    }
}

#endif
