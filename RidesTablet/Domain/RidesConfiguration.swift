import Foundation

public struct RidesConfiguration: Codable, Equatable, Sendable {
    public let currencyCode: String
    public let pricePerRideEUR: Decimal
    public let cashIncrementEUR: Decimal
    public let maxRides: UInt

    public init(currencyCode: String = "EUR", pricePerRideEUR: Decimal = 0.03, cashIncrementEUR: Decimal = 1.50, maxRides: UInt = 500) {
        self.currencyCode = currencyCode
        self.pricePerRideEUR = pricePerRideEUR
        self.cashIncrementEUR = cashIncrementEUR
        self.maxRides = maxRides
    }

    public static let prototype = RidesConfiguration()

    public func price(for rides: UInt) -> Decimal { Decimal(rides) * pricePerRideEUR }

    public func rides(forCash amount: Decimal) -> UInt {
        guard amount >= 0, pricePerRideEUR > 0 else { return 0 }
        return min(maxRides, UInt(truncating: (amount / pricePerRideEUR) as NSDecimalNumber))
    }

    public static func load(bundle: Bundle = .main) -> RidesConfiguration {
        guard let url = bundle.url(forResource: "rides-config", withExtension: "json"),
              let data = try? Data(contentsOf: url),
              let value = try? JSONDecoder().decode(RidesConfiguration.self, from: data) else {
            return .prototype
        }
        return value
    }
}
