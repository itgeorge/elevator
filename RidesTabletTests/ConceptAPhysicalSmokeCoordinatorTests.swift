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
}

#endif
