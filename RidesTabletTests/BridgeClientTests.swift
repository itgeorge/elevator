import XCTest
@testable import RidesTablet

private final class StubBridgeURLProtocol: URLProtocol {
    nonisolated(unsafe) static var handler: ((URLRequest) throws -> (HTTPURLResponse, Data))?
    nonisolated(unsafe) static var requestCount = 0

    override class func canInit(with request: URLRequest) -> Bool { true }
    override class func canonicalRequest(for request: URLRequest) -> URLRequest { request }

    override func startLoading() {
        do {
            Self.requestCount += 1
            guard let handler = Self.handler else { throw URLError(.badServerResponse) }
            var request = request
            if request.httpBody == nil, let stream = request.httpBodyStream {
                stream.open()
                var body = Data()
                var buffer = [UInt8](repeating: 0, count: 1024)
                while stream.hasBytesAvailable {
                    let read = stream.read(&buffer, maxLength: buffer.count)
                    if read > 0 { body.append(contentsOf: buffer.prefix(read)) }
                    else { break }
                }
                stream.close()
                request.httpBody = body
            }
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

private func stubSession() -> URLSession {
    let configuration = URLSessionConfiguration.ephemeral
    configuration.protocolClasses = [StubBridgeURLProtocol.self]
    return URLSession(configuration: configuration)
}

private func response(for request: URLRequest, status: Int = 200) -> HTTPURLResponse {
    HTTPURLResponse(url: request.url!, statusCode: status, httpVersion: "HTTP/1.1", headerFields: ["Content-Type": "application/json"])!
}

private enum BridgeTestError: Error {
    case secureSaveFailed
}

final class BridgeContractTests: XCTestCase {
    func testBlockValueRequiresExactlyEightUppercaseHexCharacters() throws {
        XCTAssertTrue(BridgeValueValidation.isUppercaseHex32("A1B2C3D4"))
        XCTAssertTrue(BridgeValueValidation.isUppercaseHex32("0123ABCDEF".suffix(8).description))
        XCTAssertFalse(BridgeValueValidation.isUppercaseHex32("a1B2C3D4"))
        XCTAssertFalse(BridgeValueValidation.isUppercaseHex32("A1B2C3D"))
        XCTAssertFalse(BridgeValueValidation.isUppercaseHex32("A1B2C3D45"))
        XCTAssertFalse(BridgeValueValidation.isUppercaseHex32("A1B2C3G4"))
    }

    func testPairHealthAndErrorContractsDecode() throws {
        let decoder = JSONDecoder()
        XCTAssertEqual(
            try decoder.decode(BridgePairResponse.self, from: Data(#"{"accessToken":"secret","tokenType":"Bearer"}"#.utf8)),
            BridgePairResponse(accessToken: "secret", tokenType: "Bearer")
        )
        XCTAssertEqual(
            try decoder.decode(BridgeHealthResponse.self, from: Data(#"{"status":"ok","apiVersion":"v1","bridgeVersion":"1.0.0"}"#.utf8)),
            BridgeHealthResponse(status: "ok", apiVersion: "v1", bridgeVersion: "1.0.0")
        )
        XCTAssertEqual(
            try decoder.decode(BridgeErrorResponse.self, from: Data(#"{"code":"pm3_unavailable","message":"Connect the reader."}"#.utf8)),
            BridgeErrorResponse(code: "pm3_unavailable", message: "Connect the reader.")
        )
    }

    func testMalformedResponsesAreRejected() {
        let decoder = JSONDecoder()
        let malformed: [Data] = [
            Data(#"{"accessToken":"","tokenType":"Bearer"}"#.utf8),
            Data(#"{"status":"ok","apiVersion":"v1"}"#.utf8),
            Data(#"{"code":"","message":"error"}"#.utf8),
            Data(#"{"block":5,"value":"a1B2C3D4"}"#.utf8),
            Data(#"{"block":5,"value":"A1B2C3D"}"#.utf8),
            Data(#"{"block":4,"value":"A1B2C3D4"}"#.utf8),
            Data(#"{"version":"v1","paired":false}"#.utf8),
            Data(#"{"version":"v1","paired":true,"extra":false}"#.utf8),
            Data(#"not-json"#.utf8)
        ]
        XCTAssertThrowsError(try decoder.decode(BridgePairResponse.self, from: malformed[0]))
        XCTAssertThrowsError(try decoder.decode(BridgeHealthResponse.self, from: malformed[1]))
        XCTAssertThrowsError(try decoder.decode(BridgeErrorResponse.self, from: malformed[2]))
        XCTAssertThrowsError(try decoder.decode(BridgeBlockResponse.self, from: malformed[3]))
        XCTAssertThrowsError(try decoder.decode(BridgeBlockResponse.self, from: malformed[4]))
        XCTAssertThrowsError(try decoder.decode(BridgeBlockResponse.self, from: malformed[5]))
        XCTAssertThrowsError(try decoder.decode(BridgePairStatusResponse.self, from: malformed[6]))
        XCTAssertThrowsError(try decoder.decode(BridgePairStatusResponse.self, from: malformed[7]))
        XCTAssertThrowsError(try decoder.decode(BridgeHealthResponse.self, from: malformed[8]))
    }

    func testBaseURLAcceptsConciseLocalInputAndDefaultsPort() throws {
        XCTAssertEqual(try BridgeClient.normalizeBaseURL(" 192.168.1.20 "), URL(string: "http://192.168.1.20:5080")!)
        XCTAssertEqual(try BridgeClient.normalizeBaseURL("localhost"), URL(string: "http://localhost:5080")!)
        XCTAssertEqual(try BridgeClient.normalizeBaseURL("10.0.0.3:8080"), URL(string: "http://10.0.0.3:8080")!)
        XCTAssertEqual(
            try BridgeClient.normalizeBaseURL(" http://192.168.1.20:8080/ "),
            URL(string: "http://192.168.1.20:8080")!
        )
        XCTAssertEqual(try BridgeClient.normalizeBaseURL("http://localhost:8080/"), URL(string: "http://localhost:8080")!)
        XCTAssertEqual(try BridgeClient.normalizeBaseURL("http://10.0.0.3:1"), URL(string: "http://10.0.0.3:1")!)
    }

    func testBaseURLRejectsHTTPSPublicDNSPathCredentialsQueryAndIPv6() {
        let invalid = [
            "https://192.168.1.2:8080",
            "https://192.168.1.2",
            "http://8.8.8.8:8080",
            "http://bridge.local:8080",
            "http://192.168.1.2:8080/api",
            "http://user:password@192.168.1.2:8080",
            "192.168.1.2:8080?q=secret",
            "http://192.168.1.2:8080?q=secret",
            "http://192.168.1.2:",
            "192.168.1.2:",
            "http://[::1]:8080",
            "[::1]:8080"
        ]
        for value in invalid {
            XCTAssertThrowsError(try BridgeClient.normalizeBaseURL(value), value)
        }
    }
}

final class BridgeClientTests: XCTestCase {
    override func tearDown() {
        StubBridgeURLProtocol.handler = nil
        StubBridgeURLProtocol.requestCount = 0
        super.tearDown()
    }

    func testPairEncodesPINAndReadInjectsBearerHeader() async throws {
        let session = stubSession()
        let token = "test-token-that-must-not-be-logged"
        var requests: [URLRequest] = []
        StubBridgeURLProtocol.handler = { request in
            requests.append(request)
            if request.url!.path == "/api/v1/health" {
                XCTAssertNil(request.value(forHTTPHeaderField: "Authorization"))
                return (response(for: request), Data(#"{"status":"ok","apiVersion":"v1","bridgeVersion":"1.0.0"}"#.utf8))
            }
            if request.url!.path == "/api/v1/pair" {
                XCTAssertEqual(request.httpMethod, "POST")
                XCTAssertEqual(request.value(forHTTPHeaderField: "Content-Type"), "application/json")
                let body = try XCTUnwrap(request.httpBody)
                XCTAssertEqual(try JSONDecoder().decode(BridgePairRequest.self, from: body), BridgePairRequest(pin: "123456"))
                return (response(for: request), Data(#"{"accessToken":"test-token-that-must-not-be-logged","tokenType":"Bearer"}"#.utf8))
            }
            XCTAssertEqual(request.value(forHTTPHeaderField: "Authorization"), "Bearer \(token)")
            return (response(for: request), Data(#"{"block":5,"value":"A1B2C3D4"}"#.utf8))
        }

        let client = try BridgeClient(baseURLString: "http://127.0.0.1:8080", session: session)
        _ = try await client.pair(pin: "123456")
        let result = try await client.readBlock5()
        XCTAssertEqual(result.value, "A1B2C3D4")
        XCTAssertEqual(requests.count, 3)
        XCTAssertEqual(requests.map { $0.url?.path }, ["/api/v1/health", "/api/v1/pair", "/api/v1/hardware/page0/block5"])
    }

    func testPairFailureIsNotRetriedAfterSingleHealthCheck() async throws {
        var paths: [String] = []
        StubBridgeURLProtocol.handler = { request in
            paths.append(request.url!.path)
            if request.url!.path == "/api/v1/health" {
                return (response(for: request), Data(#"{"status":"ok","apiVersion":"v1","bridgeVersion":"1.0.0"}"#.utf8))
            }
            XCTAssertEqual(request.url!.path, "/api/v1/pair")
            return (response(for: request, status: 503), Data(#"{"code":"bridge_busy","message":"Try again."}"#.utf8))
        }
        let client = try BridgeClient(baseURLString: "127.0.0.1", session: stubSession())
        do {
            _ = try await client.pair(pin: "123456")
            XCTFail("Expected pair failure")
        } catch let error as BridgeClientError {
            XCTAssertEqual(error, .server(code: "bridge_busy", message: "Try again.", statusCode: 503))
        }
        XCTAssertEqual(paths, ["/api/v1/health", "/api/v1/pair"])
        XCTAssertEqual(StubBridgeURLProtocol.requestCount, 2)
    }

    func testPairServerErrorRedactsPINFromCodeMessageAndDescription() async throws {
        let pin = "123456"
        var paths: [String] = []
        StubBridgeURLProtocol.handler = { request in
            paths.append(request.url!.path)
            if request.url!.path == "/api/v1/health" {
                return (response(for: request), Data(#"{"status":"ok","apiVersion":"v1","bridgeVersion":"1.0.0"}"#.utf8))
            }
            XCTAssertEqual(request.url!.path, "/api/v1/pair")
            return (
                response(for: request, status: 409),
                Data(#"{"code":"pin_123456_rejected","message":"Submitted PIN 123456 was echoed by the server."}"#.utf8)
            )
        }
        let client = try BridgeClient(baseURLString: "127.0.0.1", session: stubSession())

        do {
            _ = try await client.pair(pin: pin)
            XCTFail("Expected malicious pairing error")
        } catch let error as BridgeClientError {
            XCTAssertEqual(
                error,
                .server(
                    code: "pin_[redacted]_rejected",
                    message: "Submitted PIN [redacted] was echoed by the server.",
                    statusCode: 409
                )
            )
            XCTAssertFalse(error.localizedDescription.contains(pin))
        }
        XCTAssertEqual(paths, ["/api/v1/health", "/api/v1/pair"])
        XCTAssertEqual(StubBridgeURLProtocol.requestCount, 2)
    }

    func testHealthTimeoutIsNotRetriedAndPairIsNotSent() async throws {
        var paths: [String] = []
        var delayCount = 0
        StubBridgeURLProtocol.handler = { request in
            paths.append(request.url!.path)
            XCTAssertEqual(request.url!.path, "/api/v1/health")
            throw URLError(.timedOut)
        }
        let client = try BridgeClient(
            baseURLString: "127.0.0.1",
            session: stubSession(),
            healthRetryDelay: { delayCount += 1 }
        )
        do {
            _ = try await client.pair(pin: "123456")
            XCTFail("Expected health timeout")
        } catch let error as BridgeClientError {
            XCTAssertEqual(error, .timeout)
        }
        XCTAssertEqual(paths, ["/api/v1/health"])
        XCTAssertEqual(delayCount, 0)
    }

    func testPairCancellationIsPreservedWithoutRetry() async throws {
        var paths: [String] = []
        var delayCount = 0
        StubBridgeURLProtocol.handler = { request in
            paths.append(request.url!.path)
            throw CancellationError()
        }
        let client = try BridgeClient(
            baseURLString: "127.0.0.1",
            session: stubSession(),
            healthRetryDelay: { delayCount += 1 }
        )
        do {
            _ = try await client.pair(pin: "123456")
            XCTFail("Expected cancellation")
        } catch is CancellationError {
            // Expected: cancellation is not converted to unreachable or retried.
        }
        XCTAssertEqual(paths, ["/api/v1/health"])
        XCTAssertEqual(delayCount, 0)
    }

    func testTimeoutAndUnreachableAreMappedActionably() async throws {
        let client = try BridgeClient(baseURLString: "http://127.0.0.1:8080", session: stubSession())
        StubBridgeURLProtocol.handler = { _ in throw URLError(.timedOut) }
        do {
            _ = try await client.health()
            XCTFail("Expected timeout")
        } catch let error as BridgeClientError {
            XCTAssertEqual(error, .timeout)
        }

        StubBridgeURLProtocol.handler = { _ in throw URLError(.cannotConnectToHost) }
        do {
            _ = try await client.health()
            XCTFail("Expected unreachable")
        } catch let error as BridgeClientError {
            XCTAssertEqual(error, .unreachable)
        }
    }

    func testInvalidJSONAndStableServerErrorAreMapped() async throws {
        let client = try BridgeClient(baseURLString: "http://127.0.0.1:8080", session: stubSession())
        StubBridgeURLProtocol.handler = { request in
            (response(for: request), Data(#"{"status":"ok"}"#.utf8))
        }
        do {
            _ = try await client.health()
            XCTFail("Expected invalid JSON")
        } catch let error as BridgeClientError {
            XCTAssertEqual(error, .invalidJSON)
        }

        StubBridgeURLProtocol.handler = { request in
            (response(for: request, status: 503), Data(#"{"code":"bridge_busy","message":"Try again after the current operation."}"#.utf8))
        }
        do {
            _ = try await client.health()
            XCTFail("Expected server error")
        } catch let error as BridgeClientError {
            XCTAssertEqual(error, .server(code: "bridge_busy", message: "Try again after the current operation.", statusCode: 503))
        }
    }

    func testHardwareTimeoutUsesExactGETPathNoCacheThirtySecondTimeoutAndNoRetry() async throws {
        let session = stubSession()
        let token = "timeout-token"
        StubBridgeURLProtocol.handler = { request in
            XCTAssertEqual(request.httpMethod, "GET")
            XCTAssertEqual(request.url?.path, "/api/v1/hardware/page0/block5")
            XCTAssertEqual(request.cachePolicy, .reloadIgnoringLocalCacheData)
            XCTAssertEqual(request.timeoutInterval, 30, accuracy: 0.001)
            XCTAssertEqual(request.value(forHTTPHeaderField: "Authorization"), "Bearer \(token)")
            throw URLError(.timedOut)
        }
        let credential = BridgeCredential(baseURL: URL(string: "http://127.0.0.1:8080")!, accessToken: token)
        let client = try BridgeClient(baseURL: credential.baseURL, session: session, credential: credential)
        do {
            _ = try await client.readBlock5()
            XCTFail("Expected timeout")
        } catch let error as BridgeClientError {
            XCTAssertEqual(error, .timeout)
        }
        XCTAssertEqual(StubBridgeURLProtocol.requestCount, 1)
    }

    func testCancellationIsPreserved() async throws {
        StubBridgeURLProtocol.handler = { _ in throw CancellationError() }
        let client = try BridgeClient(baseURLString: "http://127.0.0.1:8080", session: stubSession())
        do {
            _ = try await client.health()
            XCTFail("Expected cancellation")
        } catch is CancellationError {
            // Expected: cancellation is not reported as an unreachable bridge.
        } catch {
            XCTFail("Expected CancellationError, got \(error)")
        }
    }

    func testConcurrentCredentialAccessIsSerialized() async throws {
        let client = try BridgeClient(baseURLString: "http://127.0.0.1:8080", session: stubSession())
        let credential = BridgeCredential(baseURL: client.baseURL, accessToken: "concurrent-token")
        await withTaskGroup(of: Void.self) { group in
            for _ in 0..<100 {
                group.addTask {
                    try? client.setCredential(credential)
                    client.clearCredential()
                    try? client.setCredential(credential)
                }
            }
        }
        XCTAssertTrue(client.hasCredential)
    }

    func testPairStatusVerificationUsesCandidateURLBearerAndExactRequestWithoutRetry() async throws {
        let session = stubSession()
        let token = "status-token"
        var requests: [URLRequest] = []
        StubBridgeURLProtocol.handler = { request in
            requests.append(request)
            XCTAssertEqual(request.httpMethod, "GET")
            XCTAssertEqual(request.url?.absoluteString, "http://192.168.1.20:8080/api/v1/pair/status")
            XCTAssertEqual(request.value(forHTTPHeaderField: "Authorization"), "Bearer \(token)")
            XCTAssertEqual(request.cachePolicy, .reloadIgnoringLocalCacheData)
            XCTAssertEqual(request.value(forHTTPHeaderField: "Cache-Control"), "no-cache")
            XCTAssertEqual(request.timeoutInterval, 30, accuracy: 0.001)
            XCTAssertNil(request.httpBody)
            return (response(for: request), Data(#"{"version":"v1","paired":true}"#.utf8))
        }
        let oldURL = URL(string: "http://127.0.0.1:8080")!
        let client = try BridgeClient(
            baseURL: oldURL,
            session: session,
            credential: BridgeCredential(baseURL: oldURL, accessToken: token)
        )

        let status = try await client.verifyPairing(at: " 192.168.1.20:8080/ ")

        XCTAssertEqual(status.version, "v1")
        XCTAssertTrue(status.paired)
        XCTAssertEqual(requests.count, 1)
        XCTAssertEqual(client.baseURL, oldURL)
    }

    func testPairStatusVerificationTimeoutIsNotRetried() async throws {
        StubBridgeURLProtocol.handler = { request in
            XCTAssertEqual(request.url?.path, "/api/v1/pair/status")
            throw URLError(.timedOut)
        }
        let url = URL(string: "http://127.0.0.1:8080")!
        let client = try BridgeClient(
            baseURL: url,
            session: stubSession(),
            credential: BridgeCredential(baseURL: url, accessToken: "status-timeout-token")
        )

        do {
            _ = try await client.verifyPairing()
            XCTFail("Expected timeout")
        } catch let error as BridgeClientError {
            XCTAssertEqual(error, .timeout)
        }
        XCTAssertEqual(StubBridgeURLProtocol.requestCount, 1)
    }

    func testRevokeUsesAuthenticatedPOST() async throws {
        let session = stubSession()
        let token = "revoke-token"
        StubBridgeURLProtocol.handler = { request in
            XCTAssertEqual(request.httpMethod, "POST")
            XCTAssertEqual(request.url?.path, "/api/v1/pair/revoke")
            XCTAssertEqual(request.value(forHTTPHeaderField: "Authorization"), "Bearer \(token)")
            XCTAssertNil(request.httpBody)
            return (response(for: request, status: 204), Data())
        }
        let credential = BridgeCredential(baseURL: URL(string: "http://127.0.0.1:8080")!, accessToken: token)
        let client = try BridgeClient(baseURL: credential.baseURL, session: session, credential: credential)
        try await client.revoke()
        XCTAssertEqual(StubBridgeURLProtocol.requestCount, 1)
    }

    func testUnauthorizedNeverIncludesBearerTokenAndHardwareRequestIsNotRetried() async throws {
        let session = stubSession()
        let token = "secret-token-never-in-error"
        StubBridgeURLProtocol.handler = { request in
            (response(for: request, status: 401), Data(#"{"code":"unauthorized","message":"Pair again."}"#.utf8))
        }
        let credential = BridgeCredential(baseURL: URL(string: "http://127.0.0.1:8080")!, accessToken: token)
        let client = try BridgeClient(baseURL: credential.baseURL, session: session, credential: credential)
        do {
            _ = try await client.readBlock5()
            XCTFail("Expected unauthorized")
        } catch let error as BridgeClientError {
            XCTAssertEqual(error, .unauthorized)
            XCTAssertFalse(error.localizedDescription.contains(token))
        }
        XCTAssertEqual(StubBridgeURLProtocol.requestCount, 1)
    }
}

@MainActor
final class BridgeConnectionModelTests: XCTestCase {
    private let healthyResponse = Data(#"{"status":"ok","apiVersion":"v1","bridgeVersion":"1.0.0"}"#.utf8)

    func testPairHealthPreflightHappensBeforePINIsConsumed() async throws {
        let store = InMemoryBridgeCredentialStore()
        var paths: [String] = []
        StubBridgeURLProtocol.handler = { request in
            paths.append(request.url!.path)
            switch request.url!.path {
            case "/api/v1/health":
                XCTAssertNil(request.value(forHTTPHeaderField: "Authorization"))
                return (response(for: request), self.healthyResponse)
            case "/api/v1/pair":
                XCTAssertEqual(request.httpMethod, "POST")
                return (response(for: request), Data(#"{"accessToken":"preflight-token","tokenType":"Bearer"}"#.utf8))
            default:
                XCTFail("Unexpected bridge request: \(request.url!.path)")
                return (response(for: request, status: 500), Data())
            }
        }
        let model = BridgeConnectionModel(credentialStore: store, session: stubSession())
        model.bridgeURLText = "192.168.1.20"

        await model.pair(pin: "123456")

        XCTAssertEqual(paths, ["/api/v1/health", "/api/v1/pair"])
        XCTAssertEqual(model.bridgeURLText, "http://192.168.1.20:5080")
        XCTAssertEqual(store.credential?.baseURL, URL(string: "http://192.168.1.20:5080")!)
        XCTAssertEqual(model.state, .connected)
        XCTAssertNotNil(store.credential)
    }

    func testPairRetriesOnlyUnreachableHealthOnceBeforePairing() async throws {
        let store = InMemoryBridgeCredentialStore()
        var paths: [String] = []
        var delayCount = 0
        var healthAttempts = 0
        StubBridgeURLProtocol.handler = { request in
            paths.append(request.url!.path)
            if request.url!.path == "/api/v1/health" {
                healthAttempts += 1
                if healthAttempts == 1 { throw URLError(.cannotConnectToHost) }
                return (response(for: request), self.healthyResponse)
            }
            XCTAssertEqual(request.url!.path, "/api/v1/pair")
            return (response(for: request), Data(#"{"accessToken":"retry-health-token","tokenType":"Bearer"}"#.utf8))
        }
        let model = BridgeConnectionModel(
            credentialStore: store,
            session: stubSession(),
            healthRetryDelay: { delayCount += 1 }
        )
        model.bridgeURLText = "localhost"

        await model.pair(pin: "123456")

        XCTAssertEqual(paths, ["/api/v1/health", "/api/v1/health", "/api/v1/pair"])
        XCTAssertEqual(healthAttempts, 2)
        XCTAssertEqual(delayCount, 1)
        XCTAssertEqual(model.state, .connected)
    }

    func testFailedHealthPreflightDoesNotSendPairAndLeavesPINForRetry() async throws {
        let store = InMemoryBridgeCredentialStore()
        var paths: [String] = []
        var delayCount = 0
        StubBridgeURLProtocol.handler = { request in
            paths.append(request.url!.path)
            XCTAssertEqual(request.url!.path, "/api/v1/health")
            throw URLError(.cannotConnectToHost)
        }
        let model = BridgeConnectionModel(
            credentialStore: store,
            session: stubSession(),
            healthRetryDelay: { delayCount += 1 }
        )
        model.bridgeURLText = "192.168.1.20"
        let pin = "123456"

        await model.pair(pin: pin)

        XCTAssertEqual(pin, "123456")
        XCTAssertEqual(paths, ["/api/v1/health", "/api/v1/health"])
        XCTAssertEqual(delayCount, 1)
        XCTAssertEqual(model.state, .failed(BridgeClientError.unreachable.localizedDescription))
        XCTAssertNil(store.credential)
    }

    func testIncompatibleHealthDoesNotConsumePINOrSendPair() async throws {
        let store = InMemoryBridgeCredentialStore()
        var paths: [String] = []
        StubBridgeURLProtocol.handler = { request in
            paths.append(request.url!.path)
            XCTAssertEqual(request.url!.path, "/api/v1/health")
            return (response(for: request), Data(#"{"status":"ok","apiVersion":"v2","bridgeVersion":"2.0.0"}"#.utf8))
        }
        let model = BridgeConnectionModel(credentialStore: store, session: stubSession())
        model.bridgeURLText = "192.168.1.20"

        await model.pair(pin: "123456")

        XCTAssertEqual(paths, ["/api/v1/health"])
        XCTAssertEqual(model.state, .failed(BridgeClientError.invalidResponse.localizedDescription))
        XCTAssertNil(store.credential)
    }

    func testPairServerErrorNeverExposesPINInModelState() async throws {
        let store = InMemoryBridgeCredentialStore()
        let pin = "123456"
        var paths: [String] = []
        StubBridgeURLProtocol.handler = { request in
            paths.append(request.url!.path)
            if request.url!.path == "/api/v1/health" {
                return (response(for: request), self.healthyResponse)
            }
            XCTAssertEqual(request.url!.path, "/api/v1/pair")
            return (
                response(for: request, status: 409),
                Data(#"{"code":"pin_123456_rejected","message":"The submitted PIN 123456 was logged."}"#.utf8)
            )
        }
        let model = BridgeConnectionModel(credentialStore: store, session: stubSession())
        model.bridgeURLText = "127.0.0.1"

        await model.pair(pin: pin)

        XCTAssertEqual(paths, ["/api/v1/health", "/api/v1/pair"])
        XCTAssertEqual(
            model.state,
            .failed("Bridge error pin_[redacted]_rejected: The submitted PIN [redacted] was logged.")
        )
        XCTAssertFalse(model.message?.contains(pin) == true)
        XCTAssertFalse(String(describing: model.state).contains(pin))
        XCTAssertNil(store.credential)
    }

    func testPairReadRestoreAndForgetStates() async throws {
        let store = InMemoryBridgeCredentialStore()
        StubBridgeURLProtocol.handler = { request in
            if request.url!.path == "/api/v1/health" {
                return (response(for: request), self.healthyResponse)
            }
            if request.url!.path == "/api/v1/pair" {
                return (response(for: request), Data(#"{"accessToken":"persisted-token","tokenType":"Bearer"}"#.utf8))
            }
            return (response(for: request), Data(#"{"block":5,"value":"A1B2C3D4"}"#.utf8))
        }

        let first = BridgeConnectionModel(credentialStore: store, session: stubSession())
        first.bridgeURLText = "http://127.0.0.1:8080"
        await first.pair(pin: "123456")
        XCTAssertEqual(first.state, .connected)
        XCTAssertNotNil(store.credential)

        await first.readBlock5()
        XCTAssertEqual(first.lastBlock5Value, "A1B2C3D4")

        let restored = BridgeConnectionModel(credentialStore: store, session: stubSession())
        XCTAssertEqual(restored.state, .restored)
        await restored.readBlock5()
        XCTAssertEqual(restored.state, .connected)

        await restored.forget()
        XCTAssertEqual(restored.state, .unconfigured)
        XCTAssertNil(store.credential)
    }

    func testSaveFailureRevokesNewBearerAndRequiresRepair() async throws {
        let store = InMemoryBridgeCredentialStore()
        store.setSaveError(BridgeTestError.secureSaveFailed)
        let token = "orphan-prevention-token"
        var paths: [String] = []
        StubBridgeURLProtocol.handler = { request in
            paths.append(request.url!.path)
            if request.url!.path == "/api/v1/health" {
                return (response(for: request), Data(#"{"status":"ok","apiVersion":"v1","bridgeVersion":"1.0.0"}"#.utf8))
            }
            if request.url!.path == "/api/v1/pair" {
                return (response(for: request), Data(#"{"accessToken":"orphan-prevention-token","tokenType":"Bearer"}"#.utf8))
            }
            XCTAssertEqual(request.url!.path, "/api/v1/pair/revoke")
            XCTAssertEqual(request.value(forHTTPHeaderField: "Authorization"), "Bearer \(token)")
            return (response(for: request, status: 204), Data())
        }
        let model = BridgeConnectionModel(credentialStore: store, session: stubSession())
        model.bridgeURLText = "http://127.0.0.1:8080"
        await model.pair(pin: "123456")
        XCTAssertEqual(paths, ["/api/v1/health", "/api/v1/pair", "/api/v1/pair/revoke"])
        XCTAssertFalse(model.isPaired)
        XCTAssertNil(store.credential)
        guard case .failed(let detail) = model.state else { return XCTFail("Expected secure-save failure") }
        XCTAssertTrue(detail.contains("revoked"))
        XCTAssertFalse(detail.contains(token))
        XCTAssertFalse(model.message?.contains(token) == true)
    }

    func testSaveFailureAndRevokeFailureRetainsClientForForgetRetry() async throws {
        let store = InMemoryBridgeCredentialStore()
        store.setSaveError(BridgeTestError.secureSaveFailed)
        let token = "retain-forget-token"
        var revokeAttempts = 0
        StubBridgeURLProtocol.handler = { request in
            if request.url!.path == "/api/v1/health" {
                return (response(for: request), Data(#"{"status":"ok","apiVersion":"v1","bridgeVersion":"1.0.0"}"#.utf8))
            }
            if request.url!.path == "/api/v1/pair" {
                return (response(for: request), Data(#"{"accessToken":"retain-forget-token","tokenType":"Bearer"}"#.utf8))
            }
            revokeAttempts += 1
            if revokeAttempts == 1 {
                return (response(for: request, status: 503), Data(#"{"code":"bridge_busy","message":"Cannot revoke retain-forget-token yet."}"#.utf8))
            }
            return (response(for: request, status: 204), Data())
        }
        let model = BridgeConnectionModel(credentialStore: store, session: stubSession())
        model.bridgeURLText = "http://127.0.0.1:8080"
        await model.pair(pin: "123456")
        XCTAssertTrue(model.isPaired)
        XCTAssertNil(store.credential)
        guard case .failed(let detail) = model.state else { return XCTFail("Expected save/revoke failure") }
        XCTAssertTrue(detail.contains("tap Forget"))
        XCTAssertFalse(detail.contains(token))
        XCTAssertFalse(model.message?.contains(token) == true)

        await model.forget()
        XCTAssertEqual(revokeAttempts, 2)
        XCTAssertEqual(model.state, .unconfigured)
        XCTAssertFalse(model.isPaired)
        XCTAssertFalse(model.message?.contains(token) == true)
    }

    func testSuccessfulForgetRevokesAndRemovesStoredCredential() async throws {
        let store = InMemoryBridgeCredentialStore()
        let token = "successful-forget-token"
        var paths: [String] = []
        StubBridgeURLProtocol.handler = { request in
            paths.append(request.url!.path)
            if request.url!.path == "/api/v1/health" {
                return (response(for: request), Data(#"{"status":"ok","apiVersion":"v1","bridgeVersion":"1.0.0"}"#.utf8))
            }
            if request.url!.path == "/api/v1/pair" {
                return (response(for: request), Data(#"{"accessToken":"successful-forget-token","tokenType":"Bearer"}"#.utf8))
            }
            XCTAssertEqual(request.httpMethod, "POST")
            XCTAssertEqual(request.value(forHTTPHeaderField: "Authorization"), "Bearer \(token)")
            return (response(for: request, status: 204), Data())
        }
        let model = BridgeConnectionModel(credentialStore: store, session: stubSession())
        model.bridgeURLText = "http://127.0.0.1:8080"
        await model.pair(pin: "123456")
        await model.pair(pin: "654321")
        XCTAssertEqual(model.state, .connected)
        XCTAssertTrue(model.message?.contains("already paired") == true)
        await model.forget()
        XCTAssertEqual(paths, ["/api/v1/health", "/api/v1/pair", "/api/v1/pair/revoke"])
        XCTAssertEqual(model.state, .unconfigured)
        XCTAssertFalse(model.isPaired)
        XCTAssertNil(store.credential)
        XCTAssertFalse(model.message?.contains(token) == true)
    }

    func testFailedForgetRetainsCredentialForActionableRetry() async throws {
        let store = InMemoryBridgeCredentialStore()
        let token = "failed-forget-token"
        StubBridgeURLProtocol.handler = { request in
            if request.url!.path == "/api/v1/health" {
                return (response(for: request), Data(#"{"status":"ok","apiVersion":"v1","bridgeVersion":"1.0.0"}"#.utf8))
            }
            if request.url!.path == "/api/v1/pair" {
                return (response(for: request), Data(#"{"accessToken":"failed-forget-token","tokenType":"Bearer"}"#.utf8))
            }
            return (response(for: request, status: 503), Data(#"{"code":"bridge_busy","message":"Cannot revoke failed-forget-token yet."}"#.utf8))
        }
        let model = BridgeConnectionModel(credentialStore: store, session: stubSession())
        model.bridgeURLText = "http://127.0.0.1:8080"
        await model.pair(pin: "123456")
        await model.forget()
        XCTAssertEqual(model.state, .failed("Bridge error bridge_busy: Cannot revoke [redacted] yet."))
        XCTAssertTrue(model.isPaired)
        XCTAssertNotNil(store.credential)
        XCTAssertFalse(model.message?.contains(token) == true)
    }

    func testUnauthorizedForgetClearsCredentialAndRequiresPairing() async throws {
        let store = InMemoryBridgeCredentialStore()
        let token = "expired-forget-token"
        StubBridgeURLProtocol.handler = { request in
            if request.url!.path == "/api/v1/health" {
                return (response(for: request), Data(#"{"status":"ok","apiVersion":"v1","bridgeVersion":"1.0.0"}"#.utf8))
            }
            if request.url!.path == "/api/v1/pair" {
                return (response(for: request), Data(#"{"accessToken":"expired-forget-token","tokenType":"Bearer"}"#.utf8))
            }
            return (response(for: request, status: 401), Data(#"{"code":"unauthorized","message":"Pair again."}"#.utf8))
        }
        let model = BridgeConnectionModel(credentialStore: store, session: stubSession())
        model.bridgeURLText = "http://127.0.0.1:8080"
        await model.pair(pin: "123456")
        await model.forget()
        XCTAssertEqual(model.state, .unconfigured)
        XCTAssertFalse(model.isPaired)
        XCTAssertNil(store.credential)
        XCTAssertFalse(model.message?.contains(token) == true)
    }

    func testUnauthorizedClearsStoredCredentialAndRequiresPairing() async throws {
        let store = InMemoryBridgeCredentialStore()
        StubBridgeURLProtocol.handler = { request in
            if request.url!.path == "/api/v1/health" {
                return (response(for: request), Data(#"{"status":"ok","apiVersion":"v1","bridgeVersion":"1.0.0"}"#.utf8))
            }
            if request.url!.path == "/api/v1/pair" {
                return (response(for: request), Data(#"{"accessToken":"expired-token","tokenType":"Bearer"}"#.utf8))
            }
            return (response(for: request, status: 401), Data(#"{"code":"unauthorized","message":"Pair again."}"#.utf8))
        }
        let model = BridgeConnectionModel(credentialStore: store, session: stubSession())
        model.bridgeURLText = "http://127.0.0.1:8080"
        await model.pair(pin: "123456")
        await model.readBlock5()
        XCTAssertEqual(model.state, .authenticationRequired)
        XCTAssertNil(store.credential)
        XCTAssertTrue(model.message?.contains("Pair again") == true)
        XCTAssertFalse(model.message?.contains("expired-token") == true)
    }

    func testUseEnteredBridgeAddressVerifiesThenSavesSameBearerAndSwitchesClient() async throws {
        let oldURL = URL(string: "http://127.0.0.1:8080")!
        let newURL = URL(string: "http://192.168.1.20:8080")!
        let token = "relocation-token"
        let store = InMemoryBridgeCredentialStore(
            credential: BridgeCredential(baseURL: oldURL, accessToken: token)
        )
        var paths: [String] = []
        StubBridgeURLProtocol.handler = { request in
            paths.append(request.url!.path)
            if request.url!.path == "/api/v1/pair/status" {
                XCTAssertEqual(request.url, newURL.appendingPathComponent("api/v1/pair/status"))
                XCTAssertEqual(request.value(forHTTPHeaderField: "Authorization"), "Bearer \(token)")
                return (response(for: request), Data(#"{"version":"v1","paired":true}"#.utf8))
            }
            XCTAssertEqual(request.url, newURL.appendingPathComponent("api/v1/hardware/page0/block5"))
            XCTAssertEqual(request.value(forHTTPHeaderField: "Authorization"), "Bearer \(token)")
            return (response(for: request), Data(#"{"block":5,"value":"A1B2C3D4"}"#.utf8))
        }
        let model = BridgeConnectionModel(credentialStore: store, session: stubSession())
        model.bridgeURLText = " 192.168.1.20:8080/ "

        XCTAssertTrue(model.canUseEnteredBridgeAddress)
        await model.useEnteredBridgeAddress()
        XCTAssertEqual(paths, ["/api/v1/pair/status"])
        XCTAssertEqual(store.credential, BridgeCredential(baseURL: newURL, accessToken: token))
        XCTAssertEqual(model.bridgeURLText, newURL.absoluteString)
        XCTAssertEqual(model.state, .connected)

        await model.readBlock5()
        XCTAssertEqual(paths, ["/api/v1/pair/status", "/api/v1/hardware/page0/block5"])
        XCTAssertEqual(model.lastBlock5Value, "A1B2C3D4")
    }

    func testUseEnteredBridgeAddressFailuresRetainOldCredentialAndClient() async throws {
        enum Failure { case network, unauthorized, invalidResponse, secureSave }
        let failures: [Failure] = [.network, .unauthorized, .invalidResponse, .secureSave]
        for failure in failures {
            StubBridgeURLProtocol.requestCount = 0
            let oldURL = URL(string: "http://127.0.0.1:8080")!
            let newURL = URL(string: "http://192.168.1.20:8080")!
            let token = "relocation-failure-token"
            let oldCredential = BridgeCredential(baseURL: oldURL, accessToken: token)
            let store = InMemoryBridgeCredentialStore(credential: oldCredential)
            if failure == .secureSave { store.setSaveError(BridgeTestError.secureSaveFailed) }
            var paths: [String] = []
            StubBridgeURLProtocol.handler = { request in
                paths.append(request.url!.path)
                if request.url!.path == "/api/v1/pair/status" {
                    switch failure {
                    case .network: throw URLError(.cannotConnectToHost)
                    case .unauthorized: return (response(for: request, status: 401), Data())
                    case .invalidResponse: return (response(for: request), Data(#"{"version":"v2","paired":true}"#.utf8))
                    case .secureSave: return (response(for: request), Data(#"{"version":"v1","paired":true}"#.utf8))
                    }
                }
                XCTAssertEqual(request.url, oldURL.appendingPathComponent("api/v1/hardware/page0/block5"))
                XCTAssertEqual(request.value(forHTTPHeaderField: "Authorization"), "Bearer \(token)")
                return (response(for: request), Data(#"{"block":5,"value":"A1B2C3D4"}"#.utf8))
            }
            let model = BridgeConnectionModel(credentialStore: store, session: stubSession())
            model.bridgeURLText = newURL.absoluteString

            await model.useEnteredBridgeAddress()

            XCTAssertEqual(store.credential, oldCredential, String(describing: failure))
            XCTAssertEqual(model.bridgeURLText, newURL.absoluteString, String(describing: failure))
            XCTAssertTrue(model.isPaired, String(describing: failure))
            XCTAssertFalse(model.message?.contains(token) == true, String(describing: failure))
            guard case .failed = model.state else {
                XCTFail("Expected failed relocation for \(failure)")
                continue
            }

            // The old client remains usable after every failed candidate attempt.
            await model.readBlock5()
            XCTAssertEqual(model.lastBlock5Value, "A1B2C3D4", String(describing: failure))
            XCTAssertEqual(paths.last, "/api/v1/hardware/page0/block5", String(describing: failure))
        }
    }

    func testLaunchAddressOverrideAutoRelocatesExactlyOnceWithNoPairRevokeOrHardware() async throws {
        StubBridgeURLProtocol.requestCount = 0
        let oldURL = URL(string: "http://127.0.0.1:8080")!
        let newURL = URL(string: "http://192.168.1.20:8080")!
        let token = "launch-override-token"
        let store = InMemoryBridgeCredentialStore(
            credential: BridgeCredential(baseURL: oldURL, accessToken: token)
        )
        var paths: [String] = []
        StubBridgeURLProtocol.handler = { request in
            paths.append(request.url!.path)
            XCTAssertEqual(request.httpMethod, "GET")
            XCTAssertEqual(request.url, newURL.appendingPathComponent("api/v1/pair/status"))
            XCTAssertEqual(request.value(forHTTPHeaderField: "Authorization"), "Bearer \(token)")
            return (response(for: request), Data(#"{"version":"v1","paired":true}"#.utf8))
        }
        let model = BridgeConnectionModel(credentialStore: store, session: stubSession())

        await model.applyLaunchAddressOverride(" 192.168.1.20:8080/ ")
        await model.applyLaunchAddressOverride("192.168.1.20:8080")

        XCTAssertEqual(paths, ["/api/v1/pair/status"])
        XCTAssertEqual(store.credential, BridgeCredential(baseURL: newURL, accessToken: token))
        XCTAssertEqual(model.bridgeURLText, newURL.absoluteString)
        XCTAssertEqual(model.state, .connected)
    }

    func testAbsentLaunchAddressOverrideDoesNothing() async throws {
        StubBridgeURLProtocol.requestCount = 0
        let oldURL = URL(string: "http://127.0.0.1:8080")!
        let token = "no-launch-override-token"
        let store = InMemoryBridgeCredentialStore(
            credential: BridgeCredential(baseURL: oldURL, accessToken: token)
        )
        StubBridgeURLProtocol.handler = { request in
            XCTFail("No launch override request expected: \(request.url!.path)")
            return (response(for: request, status: 500), Data())
        }
        let model = BridgeConnectionModel(credentialStore: store, session: stubSession())

        await model.applyLaunchAddressOverride(nil)

        XCTAssertEqual(model.bridgeURLText, oldURL.absoluteString)
        XCTAssertEqual(store.credential?.baseURL, oldURL)
        XCTAssertEqual(StubBridgeURLProtocol.requestCount, 0)
    }

    func testUnpairedLaunchAddressOverrideOnlyPrefillsWithoutPairing() async throws {
        StubBridgeURLProtocol.requestCount = 0
        StubBridgeURLProtocol.handler = { request in
            XCTFail("Unpaired launch override must not send \(request.url!.path)")
            return (response(for: request, status: 500), Data())
        }
        let model = BridgeConnectionModel(credentialStore: InMemoryBridgeCredentialStore(), session: stubSession())

        await model.applyLaunchAddressOverride(" 192.168.1.20:8080 ")

        XCTAssertEqual(model.bridgeURLText, "192.168.1.20:8080")
        XCTAssertEqual(model.state, .unconfigured)
        XCTAssertEqual(StubBridgeURLProtocol.requestCount, 0)
    }

    func testLaunchAddressOverrideFailureRetainsOldCredentialAndSendsNoPairOrRevoke() async throws {
        StubBridgeURLProtocol.requestCount = 0
        let oldURL = URL(string: "http://127.0.0.1:8080")!
        let token = "failed-launch-override-token"
        let oldCredential = BridgeCredential(baseURL: oldURL, accessToken: token)
        let store = InMemoryBridgeCredentialStore(credential: oldCredential)
        var paths: [String] = []
        StubBridgeURLProtocol.handler = { request in
            paths.append(request.url!.path)
            XCTAssertEqual(request.url?.path, "/api/v1/pair/status")
            XCTAssertEqual(request.value(forHTTPHeaderField: "Authorization"), "Bearer \(token)")
            return (response(for: request, status: 401), Data())
        }
        let model = BridgeConnectionModel(credentialStore: store, session: stubSession())

        await model.applyLaunchAddressOverride("192.168.1.20")
        await model.applyLaunchAddressOverride("192.168.1.20")

        XCTAssertEqual(paths, ["/api/v1/pair/status"])
        XCTAssertEqual(store.credential, oldCredential)
        XCTAssertTrue(model.isPaired)
        XCTAssertEqual(model.state, .failed(BridgeClientError.unauthorized.localizedDescription))
        XCTAssertFalse(model.message?.contains(token) == true)
    }

    func testInvalidURLAndMissingPairingProduceActionableStates() async {
        let model = BridgeConnectionModel(credentialStore: InMemoryBridgeCredentialStore(), session: stubSession())
        model.bridgeURLText = "http://public.example:8080"
        await model.pair(pin: "123456")
        guard case .failed(let detail) = model.state else { return XCTFail("Expected failed state") }
        XCTAssertTrue(detail.contains("private IPv4"))

        await model.readBlock5()
        XCTAssertEqual(model.state, .authenticationRequired)
        XCTAssertTrue(model.message?.contains("Pair") == true)
    }
}
