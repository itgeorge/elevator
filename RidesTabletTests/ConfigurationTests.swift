import XCTest
@testable import RidesTablet

final class ConfigurationTests: XCTestCase {
    func testPrototypePricing() {
        let configuration = RidesConfiguration.prototype
        XCTAssertEqual(configuration.currencyCode, "EUR")
        XCTAssertEqual(configuration.pricePerRideEUR, 0.03)
        XCTAssertEqual(configuration.cashIncrementEUR, 1.50)
        XCTAssertEqual(configuration.price(for: 10), 0.30)
        XCTAssertEqual(configuration.rides(forCash: 1.50), 50)
    }
}
