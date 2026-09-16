import Foundation
import XCTest
@testable import RidesTablet

private final class MercuryWorkflowURLProtocol: URLProtocol {
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
                    if read > 0 { body.append(contentsOf: buffer.prefix(read)) } else { break }
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

@MainActor
final class MercuryBridgeWorkflowTests: XCTestCase {
    private let baseURL = URL(string: "http://127.0.0.1:5080")!
    private let token = "workflow-test-bearer"

    override func tearDown() {
        MercuryWorkflowURLProtocol.handler = nil
        MercuryWorkflowURLProtocol.requestCount = 0
        super.tearDown()
    }

    func testStrictMercuryDTOsRejectUnknownFieldsAndSemanticStatuses() throws {
        let decoder = JSONDecoder()
        let mirrorWithExtra = Data(#"{"version":"v1","block5":"CCC749CC","block6":"CCC749CC","extra":true}"#.utf8)
        XCTAssertThrowsError(try decoder.decode(BridgeMercuryMirrorResponse.self, from: mirrorWithExtra))

        let unknownStatus = Data(#"{"version":"v1","status":"surprise","results":[],"rollbackStatus":"notNeeded","rollback":[]}"#.utf8)
        XCTAssertThrowsError(try decoder.decode(BridgeMercuryMutationResponse.self, from: unknownStatus))

        let malformedResult = Data(#"{"version":"v1","status":"written","results":[{"block":5,"status":"written","expected":"aAAAAAAA","desired":"BBBBBBBB","actual":"BBBBBBBB"}],"rollbackStatus":"notNeeded","rollback":[]}"#.utf8)
        XCTAssertThrowsError(try decoder.decode(BridgeMercuryMutationResponse.self, from: malformedResult))

        let impossibleRollback = Data(#"{"version":"v1","status":"verifyFailed","results":[{"block":5,"status":"verifyFailed","expected":"AAAAAAAA","desired":"BBBBBBBB","actual":"BAD00000"}],"rollbackStatus":"rollbackSucceeded","rollback":[{"block":5,"expected":"AAAAAAAA","actual":"BAD00000","succeeded":true}]}"#.utf8)
        XCTAssertThrowsError(try decoder.decode(BridgeMercuryMutationResponse.self, from: impossibleRollback))

        let conflictWithoutStale = Data(#"{"version":"v1","status":"conflict","results":[{"block":5,"status":"conflict","expected":"AAAAAAAA","desired":"BBBBBBBB","actual":"BBBBBBBB"},{"block":6,"status":"conflict","expected":"CCCCCCCC","desired":"DDDDDDDD","actual":"CCCCCCCC"}],"rollbackStatus":"notNeeded","rollback":[]}"#.utf8)
        XCTAssertThrowsError(try decoder.decode(BridgeMercuryMutationResponse.self, from: conflictWithoutStale))
    }

    func testReadMercurySuccessDisplaysRawResolvedSourceAndWarningWithExactRequest() async throws {
        let model = makeModel { request in
            XCTAssertEqual(request.httpMethod, "GET")
            XCTAssertEqual(request.url?.path, "/api/v1/hardware/mercury/mirrors")
            XCTAssertEqual(request.value(forHTTPHeaderField: "Authorization"), "Bearer \(self.token)")
            XCTAssertEqual(request.cachePolicy, .reloadIgnoringLocalCacheData)
            XCTAssertEqual(request.value(forHTTPHeaderField: "Cache-Control"), "no-cache")
            XCTAssertEqual(request.timeoutInterval, 30, accuracy: 0.001)
            return self.json(request, #"{"version":"v1","block5":"3FC6BD93","block6":"CCC749CC"}"#)
        }

        await model.readMercuryRides()

        XCTAssertEqual(MercuryWorkflowURLProtocol.requestCount, 1)
        XCTAssertEqual(model.lastMercuryBlock5Value, "3FC6BD93")
        XCTAssertEqual(model.lastMercuryBlock6Value, "CCC749CC")
        XCTAssertEqual(model.resolvedMercuryRides, 0)
        XCTAssertEqual(model.mercurySourceBlockNumber, 6)
        XCTAssertTrue(model.mercuryWarningMessage?.contains("using block 6") == true)
        XCTAssertTrue(model.mercuryWarningDisplay?.contains("using block 6") == true)
        XCTAssertTrue(model.hasFreshMercurySnapshot)
        XCTAssertEqual(model.state, .connected)
    }

    func testUnknownMirrorIsDisplayedButMalformedMirrorDoesNotCreateSnapshot() async throws {
        var responseBody = #"{"version":"v1","block5":"DEADBEEF","block6":"FACECAFE"}"#
        MercuryWorkflowURLProtocol.handler = { [self] request in
            let result = self.json(request, responseBody)
            responseBody = #"{"version":"v1","block5":"not-hex","block6":"CCC749CC"}"#
            return result
        }
        let model = makeModel()

        await model.readMercuryRides()
        XCTAssertEqual(model.lastMercuryRead?.status, .unknownEncodingSequence)
        XCTAssertEqual(model.mercuryBlocksMatch, false)
        XCTAssertEqual(model.mercurySourceBlockNumber, 5)
        XCTAssertEqual(model.mercuryWarningDisplay, "Warning: Mercury mirror encoding is unknown.")
        XCTAssertFalse(model.hasFreshMercurySnapshot)
        XCTAssertEqual(model.state, .connected)

        await model.readMercuryRides()
        XCTAssertFalse(model.hasFreshMercurySnapshot)
        XCTAssertNil(model.lastMercuryBlock5Value)
        XCTAssertTrue(model.message?.contains(BridgeClientError.invalidJSON.localizedDescription) == true)
        XCTAssertEqual(MercuryWorkflowURLProtocol.requestCount, 2)
    }

    func testUnknownMirrorsRemainDiagnosticOnlyAndValidSnapshotStillRejectsOutOfRangeTarget() async throws {
        var mirrorReads = 0
        MercuryWorkflowURLProtocol.handler = { [self] request in
            XCTAssertEqual(request.url?.path, "/api/v1/hardware/mercury/mirrors")
            mirrorReads += 1
            if mirrorReads == 1 {
                return self.json(request, #"{"version":"v1","block5":"DEADBEEF","block6":"FACECAFE"}"#)
            }
            return self.json(request, #"{"version":"v1","block5":"CCC749CC","block6":"CCC749CC"}"#)
        }
        let model = makeModel()
        await model.readMercuryRides()
        XCTAssertEqual(model.lastMercuryBlock5Value, "DEADBEEF")
        XCTAssertEqual(model.lastMercuryBlock6Value, "FACECAFE")
        XCTAssertEqual(model.mercuryBlocksMatch, false)
        XCTAssertEqual(model.lastMercuryRead?.status, .unknownEncodingSequence)
        XCTAssertFalse(model.hasFreshMercurySnapshot)

        model.targetMercuryRidesText = "1"
        XCTAssertFalse(model.canSetMercuryRides)
        await model.setMercuryRides()
        XCTAssertEqual(MercuryWorkflowURLProtocol.requestCount, 1)
        XCTAssertTrue(model.message?.contains("disabled for unknown encoding") == true)

        await model.readMercuryRides()
        XCTAssertEqual(model.resolvedMercuryRides, 0)
        XCTAssertTrue(model.hasFreshMercurySnapshot)
        model.targetMercuryRidesText = "501"
        XCTAssertFalse(model.canSetMercuryRides)
        await model.setMercuryRides()
        XCTAssertEqual(MercuryWorkflowURLProtocol.requestCount, 2)
        XCTAssertTrue(model.message?.contains("0 through 500") == true)
    }

    func testWrittenMutationUsesLastRawValuesBothBlocksExactBodyAuthAndNoRetry() async throws {
        let block500 = String(format: "%08X", MercuryRideCodec.encode(500)!)
        var requests: [URLRequest] = []
        MercuryWorkflowURLProtocol.handler = { [self] request in
            requests.append(request)
            if request.url?.path == "/api/v1/hardware/mercury/mirrors" {
                return self.json(request, #"{"version":"v1","block5":"CCC749CC","block6":"CCC74EBC"}"#)
            }
            XCTAssertEqual(request.url?.path, "/api/v1/hardware/mercury/mutations")
            XCTAssertEqual(request.httpMethod, "POST")
            XCTAssertEqual(request.value(forHTTPHeaderField: "Authorization"), "Bearer \(self.token)")
            XCTAssertEqual(request.value(forHTTPHeaderField: "Content-Type"), "application/json")
            XCTAssertEqual(request.cachePolicy, .reloadIgnoringLocalCacheData)
            XCTAssertEqual(request.value(forHTTPHeaderField: "Cache-Control"), "no-cache")
            XCTAssertEqual(request.timeoutInterval, 30, accuracy: 0.001)
            let body = try XCTUnwrap(request.httpBody)
            let object = try XCTUnwrap(try JSONSerialization.jsonObject(with: body) as? [String: Any])
            XCTAssertEqual(object["version"] as? String, "v1")
            let mutations = try XCTUnwrap(object["mutations"] as? [[String: Any]])
            XCTAssertEqual(mutations.count, 2)
            XCTAssertEqual(mutations.map { $0["block"] as? Int }, [5, 6])
            XCTAssertEqual(mutations.map { $0["expected"] as? String }, ["CCC749CC", "CCC74EBC"])
            XCTAssertEqual(mutations.map { $0["desired"] as? String }, [block500, block500])
            return self.json(request, "{\"version\":\"v1\",\"status\":\"written\",\"results\":[{\"block\":5,\"status\":\"written\",\"expected\":\"CCC749CC\",\"desired\":\"\(block500)\",\"actual\":\"\(block500)\"},{\"block\":6,\"status\":\"written\",\"expected\":\"CCC74EBC\",\"desired\":\"\(block500)\",\"actual\":\"\(block500)\"}],\"rollbackStatus\":\"notNeeded\",\"rollback\":[]}")
        }
        let model = makeModel()
        await model.readMercuryRides()
        model.targetMercuryRidesText = "500"
        await model.setMercuryRides()

        XCTAssertEqual(MercuryWorkflowURLProtocol.requestCount, 2)
        XCTAssertEqual(model.resolvedMercuryRides, 500)
        XCTAssertTrue(model.hasFreshMercurySnapshot)
        XCTAssertEqual(model.message, "Mercury rides written and verified.")
    }

    func testAlreadyAppliedIsExplicitAndDoesNotRewrite() async throws {
        let desired = String(format: "%08X", MercuryRideCodec.encode(7)!)
        var mutationRequests = 0
        MercuryWorkflowURLProtocol.handler = { [self] request in
            if request.url?.path.hasSuffix("/mirrors") == true {
                return self.json(request, "{\"version\":\"v1\",\"block5\":\"\(desired)\",\"block6\":\"\(desired)\"}")
            }
            mutationRequests += 1
            return self.json(request, "{\"version\":\"v1\",\"status\":\"alreadyApplied\",\"results\":[{\"block\":5,\"status\":\"alreadyApplied\",\"expected\":\"\(desired)\",\"desired\":\"\(desired)\",\"actual\":\"\(desired)\"},{\"block\":6,\"status\":\"alreadyApplied\",\"expected\":\"\(desired)\",\"desired\":\"\(desired)\",\"actual\":\"\(desired)\"}],\"rollbackStatus\":\"notNeeded\",\"rollback\":[]}")
        }
        let model = makeModel()
        await model.readMercuryRides()
        model.targetMercuryRidesText = "7"
        await model.setMercuryRides()

        XCTAssertEqual(mutationRequests, 1)
        XCTAssertEqual(MercuryWorkflowURLProtocol.requestCount, 2)
        XCTAssertEqual(model.resolvedMercuryRides, 7)
        XCTAssertEqual(model.message, "Mercury rides already applied; no blocks were rewritten.")
    }

    func testConflictInvalidatesSnapshotAndNeverAutomaticallyReplays() async throws {
        let desired = String(format: "%08X", MercuryRideCodec.encode(1)!)
        var mutationRequests = 0
        MercuryWorkflowURLProtocol.handler = { [self] request in
            if request.url?.path.hasSuffix("/mirrors") == true {
                return self.json(request, #"{"version":"v1","block5":"CCC749CC","block6":"CCC74EBC"}"#)
            }
            mutationRequests += 1
            return self.json(request, "{\"version\":\"v1\",\"status\":\"conflict\",\"results\":[{\"block\":5,\"status\":\"conflict\",\"expected\":\"CCC749CC\",\"desired\":\"\(desired)\",\"actual\":\"11111111\"},{\"block\":6,\"status\":\"conflict\",\"expected\":\"CCC74EBC\",\"desired\":\"\(desired)\",\"actual\":\"22222222\"}],\"rollbackStatus\":\"notNeeded\",\"rollback\":[]}")
        }
        let model = makeModel()
        await model.readMercuryRides()
        model.targetMercuryRidesText = "1"
        await model.setMercuryRides()

        XCTAssertEqual(mutationRequests, 1)
        XCTAssertEqual(MercuryWorkflowURLProtocol.requestCount, 2)
        XCTAssertFalse(model.hasFreshMercurySnapshot)
        XCTAssertTrue(model.message?.contains("conflicted") == true)
        XCTAssertTrue(model.message?.contains("read Mercury rides again") == true)
        await model.setMercuryRides()
        XCTAssertEqual(mutationRequests, 1)
    }

    func testMixedDesiredAndStaleConflictIsAcceptedAndActionableWithoutRetry() async throws {
        let desired = String(format: "%08X", MercuryRideCodec.encode(1)!)
        var mutationRequests = 0
        MercuryWorkflowURLProtocol.handler = { [self] request in
            if request.url?.path.hasSuffix("/mirrors") == true {
                return self.json(request, #"{"version":"v1","block5":"CCC749CC","block6":"CCC74EBC"}"#)
            }
            mutationRequests += 1
            return self.json(request, "{\"version\":\"v1\",\"status\":\"conflict\",\"results\":[{\"block\":5,\"status\":\"conflict\",\"expected\":\"CCC749CC\",\"desired\":\"\(desired)\",\"actual\":\"\(desired)\"},{\"block\":6,\"status\":\"conflict\",\"expected\":\"CCC74EBC\",\"desired\":\"\(desired)\",\"actual\":\"11111111\"}],\"rollbackStatus\":\"notNeeded\",\"rollback\":[]}")
        }
        let model = makeModel()
        await model.readMercuryRides()
        model.targetMercuryRidesText = "1"
        await model.setMercuryRides()

        XCTAssertEqual(mutationRequests, 1)
        XCTAssertEqual(MercuryWorkflowURLProtocol.requestCount, 2)
        XCTAssertFalse(model.hasFreshMercurySnapshot)
        XCTAssertTrue(model.message?.contains("No blocks were written") == true)
        XCTAssertTrue(model.message?.contains("read Mercury rides again") == true)
    }

    func testVerifyFailureRollbackSucceededAndIncompleteAreExplicitAndInvalidateSnapshot() async throws {
        let desired = String(format: "%08X", MercuryRideCodec.encode(1)!)
        for rollbackStatus in ["rollbackSucceeded", "rollbackIncomplete"] {
            MercuryWorkflowURLProtocol.requestCount = 0
            var mutationRequests = 0
            MercuryWorkflowURLProtocol.handler = { [self] request in
                if request.url?.path.hasSuffix("/mirrors") == true {
                    return self.json(request, #"{"version":"v1","block5":"CCC749CC","block6":"CCC74EBC"}"#)
                }
                mutationRequests += 1
                let success = rollbackStatus == "rollbackSucceeded"
                let actual = success ? "CCC749CC" : "BAD00000"
                return self.json(request, "{\"version\":\"v1\",\"status\":\"verifyFailed\",\"results\":[{\"block\":5,\"status\":\"verifyFailed\",\"expected\":\"CCC749CC\",\"desired\":\"\(desired)\",\"actual\":\"BAD00000\"},{\"block\":6,\"status\":\"notAttempted\",\"expected\":\"CCC74EBC\",\"desired\":\"\(desired)\",\"actual\":null}],\"rollbackStatus\":\"\(rollbackStatus)\",\"rollback\":[{\"block\":5,\"expected\":\"CCC749CC\",\"actual\":\"\(actual)\",\"succeeded\":\(success)}]}")
            }
            let model = makeModel()
            await model.readMercuryRides()
            model.targetMercuryRidesText = "1"
            await model.setMercuryRides()

            XCTAssertEqual(mutationRequests, 1)
            XCTAssertFalse(model.hasFreshMercurySnapshot)
            XCTAssertTrue(model.message?.contains(rollbackStatus) == true)
            XCTAssertTrue(model.message?.contains("No retry was sent") == true)
        }
    }

    func testNoChipAndNetworkTimeoutInvalidateSnapshotWithoutRetryAndPreserveCredential() async throws {
        var mode = 0
        MercuryWorkflowURLProtocol.handler = { [self] request in
            if request.url?.path.hasSuffix("/mirrors") == true {
                return self.json(request, #"{"version":"v1","block5":"CCC749CC","block6":"CCC74EBC"}"#)
            }
            if mode == 1 { throw URLError(.timedOut) }
            if mode == 0 {
                return self.json(request, #"{"code":"no_chip","message":"No supported T55xx chip is present."}"#, status: 409)
            }
            throw URLError(.timedOut)
        }
        let model = makeModel()
        await model.readMercuryRides()
        model.targetMercuryRidesText = "1"
        await model.setMercuryRides()
        XCTAssertFalse(model.hasFreshMercurySnapshot)
        XCTAssertTrue(model.message?.contains("No supported T55xx chip") == true)
        XCTAssertTrue(model.isPaired)
        XCTAssertEqual(MercuryWorkflowURLProtocol.requestCount, 2)

        mode = 1
        await model.readMercuryRides()
        XCTAssertTrue(model.hasFreshMercurySnapshot)
        await model.setMercuryRides()
        // The timeout is one mutation request; there is no hidden replay.
        XCTAssertEqual(MercuryWorkflowURLProtocol.requestCount, 4)
        XCTAssertFalse(model.hasFreshMercurySnapshot)
        XCTAssertTrue(model.message?.contains("No retry was sent") == true)
    }

    func testSetRequiresFreshSnapshotAndRejectsOutOfBoundsInputBeforePOST() async throws {
        let model = makeModel { request in
            XCTFail("No network request should be made")
            return self.json(request, #"{}"#)
        }
        for value in ["", "-1", "501", "1.5", " 1"] {
            model.targetMercuryRidesText = value
            XCTAssertFalse(model.canSetMercuryRides)
            await model.setMercuryRides()
            XCTAssertEqual(MercuryWorkflowURLProtocol.requestCount, 0, value)
        }
        XCTAssertTrue(model.message?.contains("fresh mirror snapshot") == true)

        MercuryWorkflowURLProtocol.handler = { [self] request in
            XCTAssertEqual(request.url?.path, "/api/v1/hardware/mercury/mirrors")
            return self.json(request, #"{"version":"v1","block5":"CCC749CC","block6":"CCC74EBC"}"#)
        }
        let freshModel = makeModel()
        await freshModel.readMercuryRides()
        XCTAssertTrue(freshModel.hasFreshMercurySnapshot)
        for value in ["-1", "501", "1.5", " 1"] {
            freshModel.targetMercuryRidesText = value
            XCTAssertFalse(freshModel.canSetMercuryRides)
            await freshModel.setMercuryRides()
        }
        XCTAssertEqual(MercuryWorkflowURLProtocol.requestCount, 1)
    }

    func testCancellationInvalidatesSnapshotWithoutReplayAndPreservesCredential() async throws {
        MercuryWorkflowURLProtocol.handler = { [self] request in
            if request.url?.path.hasSuffix("/mirrors") == true {
                return self.json(request, #"{"version":"v1","block5":"CCC749CC","block6":"CCC74EBC"}"#)
            }
            throw CancellationError()
        }
        let store = InMemoryBridgeCredentialStore(credential: BridgeCredential(baseURL: baseURL, accessToken: token))
        let model = BridgeConnectionModel(credentialStore: store, session: workflowSession())
        await model.readMercuryRides()
        model.targetMercuryRidesText = "1"
        await model.setMercuryRides()

        XCTAssertEqual(MercuryWorkflowURLProtocol.requestCount, 2)
        XCTAssertFalse(model.hasFreshMercurySnapshot)
        XCTAssertTrue(model.isPaired)
        XCTAssertTrue(model.message?.contains("No retry was sent") == true)
    }

    func testUnauthorizedMercurySetClearsCredentialWithoutReplay() async throws {
        MercuryWorkflowURLProtocol.handler = { [self] request in
            if request.url?.path.hasSuffix("/mirrors") == true {
                return self.json(request, #"{"version":"v1","block5":"CCC749CC","block6":"CCC74EBC"}"#)
            }
            return self.json(request, #"{"code":"unauthorized","message":"Pair again."}"#, status: 401)
        }
        let store = InMemoryBridgeCredentialStore(credential: BridgeCredential(baseURL: baseURL, accessToken: token))
        let model = BridgeConnectionModel(credentialStore: store, session: workflowSession())
        await model.readMercuryRides()
        model.targetMercuryRidesText = "1"
        await model.setMercuryRides()

        XCTAssertEqual(MercuryWorkflowURLProtocol.requestCount, 2)
        XCTAssertNil(store.credential)
        XCTAssertFalse(model.isPaired)
        XCTAssertEqual(model.state, .authenticationRequired)
    }

    private func makeModel(
        handler: ((URLRequest) throws -> (HTTPURLResponse, Data))? = nil
    ) -> BridgeConnectionModel {
        if let handler { MercuryWorkflowURLProtocol.handler = handler }
        let store = InMemoryBridgeCredentialStore(credential: BridgeCredential(baseURL: baseURL, accessToken: token))
        return BridgeConnectionModel(credentialStore: store, session: workflowSession())
    }

    private func workflowSession() -> URLSession {
        let configuration = URLSessionConfiguration.ephemeral
        configuration.protocolClasses = [MercuryWorkflowURLProtocol.self]
        return URLSession(configuration: configuration)
    }

    private func json(_ request: URLRequest, _ body: String, status: Int = 200) -> (HTTPURLResponse, Data) {
        (HTTPURLResponse(url: request.url!, statusCode: status, httpVersion: "HTTP/1.1", headerFields: ["Content-Type": "application/json"])!, Data(body.utf8))
    }
}
