import Foundation
import XCTest
@testable import RidesTablet

private final class PairingImportURLProtocol: URLProtocol {
    nonisolated(unsafe) static var handler: ((URLRequest) throws -> (HTTPURLResponse, Data))?
    nonisolated(unsafe) static var paths: [String] = []

    override class func canInit(with request: URLRequest) -> Bool { true }
    override class func canonicalRequest(for request: URLRequest) -> URLRequest { request }

    override func startLoading() {
        do {
            Self.paths.append(request.url?.path ?? "")
            guard let handler = Self.handler else { throw URLError(.badServerResponse) }
            let (response, data) = try handler(request)
            client?.urlProtocol(self, didReceive: response, cacheStoragePolicy: .notAllowed)
            client?.urlProtocol(self, didLoad: data)
            client?.urlProtocolDidFinishLoading(self)
        } catch {
            client?.urlProtocol(self, didFailWithError: error)
        }
    }

    override func stopLoading() {}
}

private enum TestCredentialStoreError: Error {
    case saveFailed
}

private final class CountingCredentialStore: BridgeCredentialStore, @unchecked Sendable {
    var stored: BridgeCredential?
    var failSave = false
    private(set) var saveCount = 0

    init(_ credential: BridgeCredential? = nil) { stored = credential }
    func load() throws -> BridgeCredential? { stored }
    func save(_ credential: BridgeCredential) throws {
        saveCount += 1
        if failSave { throw TestCredentialStoreError.saveFailed }
        stored = credential
    }
    func remove() throws { stored = nil }
}

final class BridgePairingPayloadTests: XCTestCase {
    private let now = ISO8601DateFormatter().date(from: "2026-01-01T00:00:00Z")!
    private let futureExpiration = "2027-01-02T03:04:05.1234567Z"
    private let validBridgeID = "0123456789ABCDEF0123456789ABCDEF"

    override func tearDown() {
        PairingImportURLProtocol.handler = nil
        PairingImportURLProtocol.paths = []
        super.tearDown()
    }

    func testValidBackendPayloadParsesExactFieldsAndDateWithInjectedClock() throws {
        let payload = try BridgePairingPayload.parse(validJSON(), now: now)

        XCTAssertEqual(payload.type, "ridesbridge-pairing")
        XCTAssertEqual(payload.version, "v1")
        XCTAssertEqual(payload.bridgeURL, URL(string: "http://192.168.1.20:5080/")!)
        XCTAssertEqual(payload.pin, "123456")
        XCTAssertEqual(payload.bridgeId, validBridgeID)
        XCTAssertEqual(payload.apiVersion, "v1")
        let expectedExpirationFormatter = ISO8601DateFormatter()
        expectedExpirationFormatter.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        XCTAssertEqual(payload.expiresAt.timeIntervalSince1970, expectedExpirationFormatter.date(from: futureExpiration)!.timeIntervalSince1970, accuracy: 0.001)
    }

    func testParserRejectsMissingExtraAndDuplicateFields() {
        let fields = ["type", "version", "url", "pin", "expiresAt", "bridgeId", "apiVersion"]
        for field in fields {
            XCTAssertThrowsError(try BridgePairingPayload.parse(removeField(field), now: now), field)
        }

        XCTAssertThrowsError(try BridgePairingPayload.parse(String(validJSON().dropLast()) + #","bearer":"secret"}"#, now: now))
        XCTAssertThrowsError(try BridgePairingPayload.parse(String(validJSON().dropLast()) + #","verifier":"secret"}"#, now: now))
        XCTAssertThrowsError(try BridgePairingPayload.parse(String(validJSON().dropLast()) + #","nonce":"secret"}"#, now: now))

        let duplicate = #"{"type":"ridesbridge-pairing","version":"v1","url":"http://192.168.1.20:5080/","pin":"123456","expiresAt":"2027-01-02T03:04:05.1234567Z","bridgeId":"0123456789ABCDEF0123456789ABCDEF","apiVersion":"v1","pin":"654321"}"#
        XCTAssertThrowsError(try BridgePairingPayload.parse(duplicate, now: now))
    }

    func testParserRejectsWrongTypes() {
        let replacements = [
            (#""type":"ridesbridge-pairing""#, #""type":true"#),
            (#""version":"v1""#, #""version":1"#),
            (#""pin":"123456""#, #""pin":123456"#),
            (#""expiresAt":"2027-01-02T03:04:05.1234567Z""#, #""expiresAt":123"#),
            (#""bridgeId":"0123456789ABCDEF0123456789ABCDEF""#, #""bridgeId":false"#),
            (#""apiVersion":"v1""#, #""apiVersion":true"#)
        ]
        for (old, new) in replacements {
            XCTAssertThrowsError(try BridgePairingPayload.parse(validJSON().replacingOccurrences(of: old, with: new), now: now), new)
        }
    }

    func testParserRejectsTypeVersionAPIVersionPINAndBridgeIdentityMatrix() {
        let cases = [
            ("type", "other"),
            ("version", "v2"),
            ("apiVersion", "v2"),
            ("pin", "12345"),
            ("pin", "12345A"),
            ("bridgeId", "0123456789abcdef0123456789ABCDEF"),
            ("bridgeId", "0123456789ABCDEF0123456789ABCDE"),
            ("bridgeId", "0123456789ABCDEF0123456789ABCDEG")
        ]
        for (field, value) in cases {
            XCTAssertThrowsError(try BridgePairingPayload.parse(replace(field, with: value), now: now), "\(field)=\(value)")
        }
    }

    func testParserAcceptsSystemTextJsonISO8601FractionVariantsAndOffsets() throws {
        let values = [
            "2027-01-02T03:04:05.040311+00:00",
            "2027-01-02T03:04:05.1Z",
            "2027-01-02T03:04:05.1234567Z",
            "2027-01-02T03:04:05-05:30"
        ]

        for value in values {
            XCTAssertNoThrow(try BridgePairingPayload.parse(validJSON(expiration: value), now: now), value)
        }
    }

    func testParserRejectsExpiredAndMalformedISO8601WithInjectedClock() {
        XCTAssertThrowsError(try BridgePairingPayload.parse(validJSON(expiration: "2025-12-31T23:59:59Z"), now: now))
        for value in [
            "2027-01-02",
            "2027-01-02T03:04:05",
            "2027-01-02T03:04:05.Z",
            "2027-01-02T03:04:05.1234567890Z",
            "20270102T030405Z",
            "2027-01-02T03:04:05+24:00",
            "2027-01-02T03:04:05+00:60",
            "2027-02-29T03:04:05Z",
            "2027-01-02T24:04:05Z",
            "2027-01-02T03:04:05Zjunk",
            "not-a-date"
        ] {
            XCTAssertThrowsError(try BridgePairingPayload.parse(validJSON(expiration: value), now: now), value)
        }
    }

    func testParserCanonicalizesBackendURLWithRootSlash() throws {
        let payload = try BridgePairingPayload.parse(
            validJSON(url: "http://192.168.1.20:5080"),
            now: now
        )
        XCTAssertEqual(payload.bridgeURL.absoluteString, "http://192.168.1.20:5080/")
        XCTAssertEqual(try payload.validated(now: now).bridgeURL.absoluteString, "http://192.168.1.20:5080/")
    }

    func testParserRejectsNonPrivateOrUnsafeURLs() {
        let invalid = [
            "http://localhost:5080/",
            "http://127.0.0.1:5080/",
            "http://0.0.0.0:5080/",
            "http://255.255.255.255:5080/",
            "http://8.8.8.8:5080/",
            "http://bridge.example:5080/",
            "http://[::1]:5080/",
            "http://192.168.1.20:5080/unsafe",
            "http://192.168.1.20:5080/?next=secret",
            "http://192.168.1.20:5080/#fragment",
            "http://user:pass@192.168.1.20:5080/",
            "https://192.168.1.20:5080/",
            "http://192.168.1.20/",
            "http://192.168.1.20:0/",
            "http://192.168.1.20:65536/"
        ]
        for url in invalid {
            XCTAssertThrowsError(try BridgePairingPayload.parse(validJSON(url: url), now: now), url)
        }
    }

    func testCredentialDecodesOldRecordAndNewBridgeIdentityRoundTrips() throws {
        let old = Data(#"{"baseURL":"http://127.0.0.1:5080","accessToken":"old-token","tokenType":"Bearer"}"#.utf8)
        let oldCredential = try JSONDecoder().decode(BridgeCredential.self, from: old)
        XCTAssertNil(oldCredential.bridgeId)

        let newCredential = BridgeCredential(
            baseURL: URL(string: "http://192.168.1.20:5080")!,
            accessToken: "new-token",
            bridgeId: validBridgeID
        )
        let roundTripped = try JSONDecoder().decode(BridgeCredential.self, from: JSONEncoder().encode(newCredential))
        XCTAssertEqual(roundTripped, newCredential)
        XCTAssertEqual(roundTripped.bridgeId, validBridgeID)
    }

    private func validJSON(
        url: String = "http://192.168.1.20:5080/",
        expiration: String? = nil
    ) -> String {
        let expiration = expiration ?? futureExpiration
        return #"{"type":"ridesbridge-pairing","version":"v1","url":"__URL__","pin":"123456","expiresAt":"__EXPIRATION__","bridgeId":"0123456789ABCDEF0123456789ABCDEF","apiVersion":"v1"}"#
            .replacingOccurrences(of: "__URL__", with: url)
            .replacingOccurrences(of: "__EXPIRATION__", with: expiration)
    }

    private func removeField(_ field: String) -> String {
        let pattern = "\\\"\(field)\\\":\\\"[^\\\"]*\\\",?"
        return validJSON().replacingOccurrences(of: pattern, with: "", options: .regularExpression)
    }

    private func replace(_ field: String, with value: String) -> String {
        let old: String
        switch field {
        case "type": old = "ridesbridge-pairing"
        case "version", "apiVersion": old = "v1"
        case "pin": old = "123456"
        case "bridgeId": old = validBridgeID
        default: old = ""
        }
        return validJSON().replacingOccurrences(
            of: "\"\(field)\":\"\(old)\"",
            with: "\"\(field)\":\"\(value)\""
        )
    }
}

@MainActor
final class BridgePairingImportModelTests: XCTestCase {
    private let now = ISO8601DateFormatter().date(from: "2026-01-01T00:00:00Z")!
    private let bridgeURL = URL(string: "http://192.168.1.20:5080")!
    private let bridgeID = "0123456789ABCDEF0123456789ABCDEF"

    override func tearDown() {
        PairingImportURLProtocol.handler = nil
        PairingImportURLProtocol.paths = []
        super.tearDown()
    }

    func testQRImportSavesBridgeIdentityInTheInitialCredentialSave() async throws {
        let store = CountingCredentialStore()
        PairingImportURLProtocol.handler = { request in
            if request.url?.path == "/api/v1/health" {
                return self.json(request, #"{"status":"ok","apiVersion":"v1","bridgeVersion":"1.0.0"}"#)
            }
            XCTAssertEqual(request.url?.path, "/api/v1/pair")
            return self.json(request, #"{"accessToken":"qr-bearer","tokenType":"Bearer"}"#)
        }
        let model = BridgeConnectionModel(credentialStore: store, session: session(), now: { self.now })

        await model.importPairingPayload(validJSON())

        XCTAssertEqual(PairingImportURLProtocol.paths, ["/api/v1/health", "/api/v1/pair"])
        XCTAssertEqual(store.saveCount, 1)
        XCTAssertEqual(store.stored?.bridgeId, bridgeID)
        XCTAssertEqual(store.stored?.accessToken, "qr-bearer")
        XCTAssertEqual(model.bridgeURLText, bridgeURL.absoluteString)
        XCTAssertEqual(model.state, .connected)
    }

    func testManualPairingStillSavesNilBridgeIdentity() async throws {
        let store = CountingCredentialStore()
        PairingImportURLProtocol.handler = { request in
            if request.url?.path == "/api/v1/health" {
                return self.json(request, #"{"status":"ok","apiVersion":"v1","bridgeVersion":"1.0.0"}"#)
            }
            return self.json(request, #"{"accessToken":"manual-bearer","tokenType":"Bearer"}"#)
        }
        let model = BridgeConnectionModel(credentialStore: store, session: session())
        model.bridgeURLText = bridgeURL.absoluteString
        await model.pair(pin: "123456")

        XCTAssertNil(store.stored?.bridgeId)
        XCTAssertEqual(store.saveCount, 1)
    }

    func testExpiredQRIsRejectedBeforeHealthAndPairWithInjectedClock() async {
        let store = CountingCredentialStore()
        let model = BridgeConnectionModel(credentialStore: store, session: session(), now: { self.now })
        await model.importPairingPayload(validJSON(expiration: "2025-12-31T23:59:59.0000000Z"))

        XCTAssertEqual(PairingImportURLProtocol.paths, [])
        XCTAssertNil(store.stored)
        XCTAssertTrue(model.message?.contains("expired") == true)
    }

    func testServerReusedPINErrorIsActionableAndNotRetriedWithinOneImport() async {
        var pairAttempts = 0
        PairingImportURLProtocol.handler = { request in
            if request.url?.path == "/api/v1/health" {
                return self.json(request, #"{"status":"ok","apiVersion":"v1","bridgeVersion":"1.0.0"}"#)
            }
            pairAttempts += 1
            return self.json(request, #"{"code":"pin_reused","message":"Request a fresh PIN."}"#, status: 409)
        }
        let model = BridgeConnectionModel(credentialStore: CountingCredentialStore(), session: session(), now: { self.now })
        await model.importPairingPayload(validJSON())

        XCTAssertEqual(pairAttempts, 1)
        XCTAssertEqual(PairingImportURLProtocol.paths, ["/api/v1/health", "/api/v1/pair"])
        XCTAssertTrue(model.message?.contains("pin_reused") == true)
        XCTAssertTrue(model.message?.contains("fresh PIN") == true)
    }

    func testQRImportDoesNotRetryReadinessOrReplayTheOneTimePIN() async {
        var pairAttempts = 0
        PairingImportURLProtocol.handler = { request in
            if request.url?.path == "/api/v1/health" {
                throw URLError(.cannotConnectToHost)
            }
            pairAttempts += 1
            return self.json(request, #"{"accessToken":"unexpected","tokenType":"Bearer"}"#)
        }
        let model = BridgeConnectionModel(
            credentialStore: CountingCredentialStore(),
            session: session(),
            now: { self.now }
        )
        await model.importPairingPayload(validJSON())

        XCTAssertEqual(PairingImportURLProtocol.paths, ["/api/v1/health"])
        XCTAssertEqual(pairAttempts, 0)
        XCTAssertFalse(model.isPaired)
    }

    func testInitialCredentialSaveFailureRevokesTheNewBearerBeforeDroppingIt() async {
        let store = CountingCredentialStore()
        store.failSave = true
        var revokeAttempts = 0
        PairingImportURLProtocol.handler = { request in
            switch request.url?.path {
            case "/api/v1/health":
                return self.json(request, #"{"status":"ok","apiVersion":"v1","bridgeVersion":"1.0.0"}"#)
            case "/api/v1/pair":
                return self.json(request, #"{"accessToken":"temporary-bearer","tokenType":"Bearer"}"#)
            case "/api/v1/pair/revoke":
                revokeAttempts += 1
                return self.json(request, "{}")
            default:
                XCTFail("Unexpected path \(request.url?.path ?? "nil")")
                return self.json(request, "{}", status: 500)
            }
        }
        let model = BridgeConnectionModel(credentialStore: store, session: session(), now: { self.now })

        await model.importPairingPayload(validJSON())

        XCTAssertEqual(revokeAttempts, 1)
        XCTAssertEqual(PairingImportURLProtocol.paths, ["/api/v1/health", "/api/v1/pair", "/api/v1/pair/revoke"])
        XCTAssertNil(store.stored)
        XCTAssertFalse(model.isPaired)
        XCTAssertTrue(model.message?.contains("cleaned up") == true)
    }

    func testImportRefusesAlreadyPairedWithoutNetwork() async {
        let credential = BridgeCredential(baseURL: bridgeURL, accessToken: "existing", bridgeId: bridgeID)
        let model = BridgeConnectionModel(credentialStore: CountingCredentialStore(credential), session: session(), now: { self.now })
        await model.importPairingPayload(validJSON())

        XCTAssertEqual(PairingImportURLProtocol.paths, [])
        XCTAssertEqual(model.state, .restored)
        XCTAssertTrue(model.message?.contains("already paired") == true)
    }

    private func session() -> URLSession {
        let configuration = URLSessionConfiguration.ephemeral
        configuration.protocolClasses = [PairingImportURLProtocol.self]
        return URLSession(configuration: configuration)
    }

    private func json(_ request: URLRequest, _ body: String, status: Int = 200) -> (HTTPURLResponse, Data) {
        (
            HTTPURLResponse(url: request.url!, statusCode: status, httpVersion: "HTTP/1.1", headerFields: ["Content-Type": "application/json"])!,
            Data(body.utf8)
        )
    }

    private func validJSON(expiration: String = "2027-01-02T03:04:05.1234567Z") -> String {
        #"{"type":"ridesbridge-pairing","version":"v1","url":"http://192.168.1.20:5080/","pin":"123456","expiresAt":"__EXPIRATION__","bridgeId":"0123456789ABCDEF0123456789ABCDEF","apiVersion":"v1"}"#
            .replacingOccurrences(of: "__EXPIRATION__", with: expiration)
    }
}
