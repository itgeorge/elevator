import Foundation
import XCTest
@testable import RidesTablet

private final class Page0WorkflowURLProtocol: URLProtocol {
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
final class Page0BridgeWorkflowTests: XCTestCase {
    private let baseURL = URL(string: "http://127.0.0.1:5080")!
    private let token = "workflow-test-bearer"

    override func tearDown() {
        Page0WorkflowURLProtocol.handler = nil
        Page0WorkflowURLProtocol.requestCount = 0
        super.tearDown()
    }

    func testStrictPage0DTOsRejectUnknownFieldsAndSemanticStatuses() throws {
        let decoder = JSONDecoder()
        let mirrorWithExtra = Data(#"{"version":"v1","block5":"CCC749CC","block6":"CCC749CC","extra":true}"#.utf8)
        XCTAssertThrowsError(try decoder.decode(BridgePage0MirrorResponse.self, from: mirrorWithExtra))

        let unknownStatus = Data(#"{"version":"v1","status":"surprise","results":[],"rollbackStatus":"notNeeded","rollback":[]}"#.utf8)
        XCTAssertThrowsError(try decoder.decode(BridgePage0MutationResponse.self, from: unknownStatus))

        let malformedResult = Data(#"{"version":"v1","status":"written","results":[{"block":5,"status":"written","expected":"aAAAAAAA","desired":"BBBBBBBB","actual":"BBBBBBBB"}],"rollbackStatus":"notNeeded","rollback":[]}"#.utf8)
        XCTAssertThrowsError(try decoder.decode(BridgePage0MutationResponse.self, from: malformedResult))

        let impossibleRollback = Data(#"{"version":"v1","status":"verifyFailed","results":[{"block":5,"status":"verifyFailed","expected":"AAAAAAAA","desired":"BBBBBBBB","actual":"BAD00000"}],"rollbackStatus":"rollbackSucceeded","rollback":[{"block":5,"expected":"AAAAAAAA","actual":"BAD00000","succeeded":true}]}"#.utf8)
        XCTAssertThrowsError(try decoder.decode(BridgePage0MutationResponse.self, from: impossibleRollback))

        let conflictWithoutStale = Data(#"{"version":"v1","status":"conflict","results":[{"block":5,"status":"conflict","expected":"AAAAAAAA","desired":"BBBBBBBB","actual":"BBBBBBBB"},{"block":6,"status":"conflict","expected":"CCCCCCCC","desired":"DDDDDDDD","actual":"CCCCCCCC"}],"rollbackStatus":"notNeeded","rollback":[]}"#.utf8)
        XCTAssertThrowsError(try decoder.decode(BridgePage0MutationResponse.self, from: conflictWithoutStale))
    }

    func testReadPage0SuccessDisplaysRawResolvedSourceAndWarningWithExactRequest() async throws {
        let model = makeModel { request in
            XCTAssertEqual(request.httpMethod, "GET")
            XCTAssertEqual(request.url?.path, "/api/v1/hardware/page0/mirrors")
            XCTAssertEqual(request.value(forHTTPHeaderField: "Authorization"), "Bearer \(self.token)")
            XCTAssertEqual(request.cachePolicy, .reloadIgnoringLocalCacheData)
            XCTAssertEqual(request.value(forHTTPHeaderField: "Cache-Control"), "no-cache")
            XCTAssertEqual(request.timeoutInterval, 30, accuracy: 0.001)
            return self.json(request, #"{"version":"v1","block5":"3FC6BD93","block6":"CCC749CC"}"#)
        }

        await model.readPage0Rides()

        XCTAssertEqual(Page0WorkflowURLProtocol.requestCount, 1)
        XCTAssertEqual(model.lastPage0Block5Value, "3FC6BD93")
        XCTAssertEqual(model.lastPage0Block6Value, "CCC749CC")
        XCTAssertEqual(model.resolvedPage0Rides, 0)
        XCTAssertEqual(model.page0SourceBlockNumber, 6)
        XCTAssertTrue(model.page0WarningMessage?.contains("using block 6") == true)
        XCTAssertTrue(model.page0WarningDisplay?.contains("using block 6") == true)
        XCTAssertTrue(model.hasFreshPage0Snapshot)
        XCTAssertEqual(model.state, .connected)
    }

    func testUnknownMirrorIsDisplayedButMalformedMirrorDoesNotCreateSnapshot() async throws {
        var responseBody = #"{"version":"v1","block5":"DEADBEEF","block6":"FACECAFE"}"#
        Page0WorkflowURLProtocol.handler = { [self] request in
            let result = self.json(request, responseBody)
            responseBody = #"{"version":"v1","block5":"not-hex","block6":"CCC749CC"}"#
            return result
        }
        let model = makeModel()

        await model.readPage0Rides()
        XCTAssertEqual(model.lastPage0Read?.status, .unknownEncodingSequence)
        XCTAssertEqual(model.page0BlocksMatch, false)
        XCTAssertEqual(model.page0SourceBlockNumber, 5)
        XCTAssertEqual(model.page0WarningDisplay, "Warning: page0 mirror encoding is unknown.")
        XCTAssertFalse(model.hasFreshPage0Snapshot)
        XCTAssertEqual(model.state, .connected)

        await model.readPage0Rides()
        XCTAssertFalse(model.hasFreshPage0Snapshot)
        XCTAssertNil(model.lastPage0Block5Value)
        XCTAssertTrue(model.message?.contains(BridgeClientError.invalidJSON.localizedDescription) == true)
        XCTAssertEqual(Page0WorkflowURLProtocol.requestCount, 2)
    }

    func testUnknownMirrorsRemainDiagnosticOnlyAndValidSnapshotStillRejectsOutOfRangeTarget() async throws {
        var mirrorReads = 0
        Page0WorkflowURLProtocol.handler = { [self] request in
            XCTAssertEqual(request.url?.path, "/api/v1/hardware/page0/mirrors")
            mirrorReads += 1
            if mirrorReads == 1 {
                return self.json(request, #"{"version":"v1","block5":"DEADBEEF","block6":"FACECAFE"}"#)
            }
            return self.json(request, #"{"version":"v1","block5":"CCC749CC","block6":"CCC749CC"}"#)
        }
        let model = makeModel()
        await model.readPage0Rides()
        XCTAssertEqual(model.lastPage0Block5Value, "DEADBEEF")
        XCTAssertEqual(model.lastPage0Block6Value, "FACECAFE")
        XCTAssertEqual(model.page0BlocksMatch, false)
        XCTAssertEqual(model.lastPage0Read?.status, .unknownEncodingSequence)
        XCTAssertFalse(model.hasFreshPage0Snapshot)

        model.targetPage0RidesText = "1"
        XCTAssertFalse(model.canSetPage0Rides)
        await model.setPage0Rides()
        XCTAssertEqual(Page0WorkflowURLProtocol.requestCount, 1)
        XCTAssertTrue(model.message?.contains("disabled for unknown encoding") == true)

        await model.readPage0Rides()
        XCTAssertEqual(model.resolvedPage0Rides, 0)
        XCTAssertTrue(model.hasFreshPage0Snapshot)
        model.targetPage0RidesText = "501"
        XCTAssertFalse(model.canSetPage0Rides)
        await model.setPage0Rides()
        XCTAssertEqual(Page0WorkflowURLProtocol.requestCount, 2)
        XCTAssertTrue(model.message?.contains("0 through 500") == true)
    }

    func testWrittenMutationUsesLastRawValuesBothBlocksExactBodyAuthAndNoRetry() async throws {
        let block500 = String(format: "%08X", RideSequence.mercury.encode(500)!)
        var requests: [URLRequest] = []
        Page0WorkflowURLProtocol.handler = { [self] request in
            requests.append(request)
            if request.url?.path == "/api/v1/hardware/page0/mirrors" {
                return self.json(request, #"{"version":"v1","block5":"CCC749CC","block6":"CCC74EBC"}"#)
            }
            XCTAssertEqual(request.url?.path, "/api/v1/hardware/page0/mutations")
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
        await model.readPage0Rides()
        model.targetPage0RidesText = "500"
        await model.setPage0Rides()

        XCTAssertEqual(Page0WorkflowURLProtocol.requestCount, 2)
        XCTAssertEqual(model.resolvedPage0Rides, 500)
        XCTAssertTrue(model.hasFreshPage0Snapshot)
        XCTAssertEqual(model.message, "Page0 rides written and verified.")
    }

    func testAlreadyAppliedIsExplicitAndDoesNotRewrite() async throws {
        let desired = String(format: "%08X", RideSequence.mercury.encode(7)!)
        var mutationRequests = 0
        Page0WorkflowURLProtocol.handler = { [self] request in
            if request.url?.path.hasSuffix("/mirrors") == true {
                return self.json(request, "{\"version\":\"v1\",\"block5\":\"\(desired)\",\"block6\":\"\(desired)\"}")
            }
            mutationRequests += 1
            return self.json(request, "{\"version\":\"v1\",\"status\":\"alreadyApplied\",\"results\":[{\"block\":5,\"status\":\"alreadyApplied\",\"expected\":\"\(desired)\",\"desired\":\"\(desired)\",\"actual\":\"\(desired)\"},{\"block\":6,\"status\":\"alreadyApplied\",\"expected\":\"\(desired)\",\"desired\":\"\(desired)\",\"actual\":\"\(desired)\"}],\"rollbackStatus\":\"notNeeded\",\"rollback\":[]}")
        }
        let model = makeModel()
        await model.readPage0Rides()
        model.targetPage0RidesText = "7"
        await model.setPage0Rides()

        XCTAssertEqual(mutationRequests, 1)
        XCTAssertEqual(Page0WorkflowURLProtocol.requestCount, 2)
        XCTAssertEqual(model.resolvedPage0Rides, 7)
        XCTAssertEqual(model.message, "Page0 rides already applied; no blocks were rewritten.")
    }

    func testConflictInvalidatesSnapshotAndNeverAutomaticallyReplays() async throws {
        let desired = String(format: "%08X", RideSequence.mercury.encode(1)!)
        var mutationRequests = 0
        Page0WorkflowURLProtocol.handler = { [self] request in
            if request.url?.path.hasSuffix("/mirrors") == true {
                return self.json(request, #"{"version":"v1","block5":"CCC749CC","block6":"CCC74EBC"}"#)
            }
            mutationRequests += 1
            return self.json(request, "{\"version\":\"v1\",\"status\":\"conflict\",\"results\":[{\"block\":5,\"status\":\"conflict\",\"expected\":\"CCC749CC\",\"desired\":\"\(desired)\",\"actual\":\"11111111\"},{\"block\":6,\"status\":\"conflict\",\"expected\":\"CCC74EBC\",\"desired\":\"\(desired)\",\"actual\":\"22222222\"}],\"rollbackStatus\":\"notNeeded\",\"rollback\":[]}")
        }
        let model = makeModel()
        await model.readPage0Rides()
        model.targetPage0RidesText = "1"
        await model.setPage0Rides()

        XCTAssertEqual(mutationRequests, 1)
        XCTAssertEqual(Page0WorkflowURLProtocol.requestCount, 2)
        XCTAssertFalse(model.hasFreshPage0Snapshot)
        XCTAssertTrue(model.message?.contains("conflicted") == true)
        XCTAssertTrue(model.message?.contains("read page0 rides again") == true)
        await model.setPage0Rides()
        XCTAssertEqual(mutationRequests, 1)
    }

    func testMixedDesiredAndStaleConflictIsAcceptedAndActionableWithoutRetry() async throws {
        let desired = String(format: "%08X", RideSequence.mercury.encode(1)!)
        var mutationRequests = 0
        Page0WorkflowURLProtocol.handler = { [self] request in
            if request.url?.path.hasSuffix("/mirrors") == true {
                return self.json(request, #"{"version":"v1","block5":"CCC749CC","block6":"CCC74EBC"}"#)
            }
            mutationRequests += 1
            return self.json(request, "{\"version\":\"v1\",\"status\":\"conflict\",\"results\":[{\"block\":5,\"status\":\"conflict\",\"expected\":\"CCC749CC\",\"desired\":\"\(desired)\",\"actual\":\"\(desired)\"},{\"block\":6,\"status\":\"conflict\",\"expected\":\"CCC74EBC\",\"desired\":\"\(desired)\",\"actual\":\"11111111\"}],\"rollbackStatus\":\"notNeeded\",\"rollback\":[]}")
        }
        let model = makeModel()
        await model.readPage0Rides()
        model.targetPage0RidesText = "1"
        await model.setPage0Rides()

        XCTAssertEqual(mutationRequests, 1)
        XCTAssertEqual(Page0WorkflowURLProtocol.requestCount, 2)
        XCTAssertFalse(model.hasFreshPage0Snapshot)
        XCTAssertTrue(model.message?.contains("No blocks were written") == true)
        XCTAssertTrue(model.message?.contains("read page0 rides again") == true)
    }

    func testVerifyFailureRollbackSucceededAndIncompleteAreExplicitAndInvalidateSnapshot() async throws {
        let desired = String(format: "%08X", RideSequence.mercury.encode(1)!)
        for rollbackStatus in ["rollbackSucceeded", "rollbackIncomplete"] {
            Page0WorkflowURLProtocol.requestCount = 0
            var mutationRequests = 0
            Page0WorkflowURLProtocol.handler = { [self] request in
                if request.url?.path.hasSuffix("/mirrors") == true {
                    return self.json(request, #"{"version":"v1","block5":"CCC749CC","block6":"CCC74EBC"}"#)
                }
                mutationRequests += 1
                let success = rollbackStatus == "rollbackSucceeded"
                let actual = success ? "CCC749CC" : "BAD00000"
                return self.json(request, "{\"version\":\"v1\",\"status\":\"verifyFailed\",\"results\":[{\"block\":5,\"status\":\"verifyFailed\",\"expected\":\"CCC749CC\",\"desired\":\"\(desired)\",\"actual\":\"BAD00000\"},{\"block\":6,\"status\":\"notAttempted\",\"expected\":\"CCC74EBC\",\"desired\":\"\(desired)\",\"actual\":null}],\"rollbackStatus\":\"\(rollbackStatus)\",\"rollback\":[{\"block\":5,\"expected\":\"CCC749CC\",\"actual\":\"\(actual)\",\"succeeded\":\(success)}]}")
            }
            let model = makeModel()
            await model.readPage0Rides()
            model.targetPage0RidesText = "1"
            await model.setPage0Rides()

            XCTAssertEqual(mutationRequests, 1)
            XCTAssertFalse(model.hasFreshPage0Snapshot)
            XCTAssertTrue(model.message?.contains(rollbackStatus) == true)
            XCTAssertTrue(model.message?.contains("No retry was sent") == true)
        }
    }

    func testNoChipAndNetworkTimeoutInvalidateSnapshotWithoutRetryAndPreserveCredential() async throws {
        var mode = 0
        Page0WorkflowURLProtocol.handler = { [self] request in
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
        await model.readPage0Rides()
        model.targetPage0RidesText = "1"
        await model.setPage0Rides()
        XCTAssertFalse(model.hasFreshPage0Snapshot)
        XCTAssertTrue(model.message?.contains("No supported T55xx chip") == true)
        XCTAssertTrue(model.isPaired)
        XCTAssertEqual(Page0WorkflowURLProtocol.requestCount, 2)

        mode = 1
        await model.readPage0Rides()
        XCTAssertTrue(model.hasFreshPage0Snapshot)
        await model.setPage0Rides()
        // The timeout is one mutation request; there is no hidden replay.
        XCTAssertEqual(Page0WorkflowURLProtocol.requestCount, 4)
        XCTAssertFalse(model.hasFreshPage0Snapshot)
        XCTAssertTrue(model.message?.contains("No retry was sent") == true)
    }

    func testSetRequiresFreshSnapshotAndRejectsOutOfBoundsInputBeforePOST() async throws {
        let model = makeModel { request in
            XCTFail("No network request should be made")
            return self.json(request, #"{}"#)
        }
        for value in ["", "-1", "501", "1.5", " 1"] {
            model.targetPage0RidesText = value
            XCTAssertFalse(model.canSetPage0Rides)
            await model.setPage0Rides()
            XCTAssertEqual(Page0WorkflowURLProtocol.requestCount, 0, value)
        }
        XCTAssertTrue(model.message?.contains("fresh mirror snapshot") == true)

        Page0WorkflowURLProtocol.handler = { [self] request in
            XCTAssertEqual(request.url?.path, "/api/v1/hardware/page0/mirrors")
            return self.json(request, #"{"version":"v1","block5":"CCC749CC","block6":"CCC74EBC"}"#)
        }
        let freshModel = makeModel()
        await freshModel.readPage0Rides()
        XCTAssertTrue(freshModel.hasFreshPage0Snapshot)
        for value in ["-1", "501", "1.5", " 1"] {
            freshModel.targetPage0RidesText = value
            XCTAssertFalse(freshModel.canSetPage0Rides)
            await freshModel.setPage0Rides()
        }
        XCTAssertEqual(Page0WorkflowURLProtocol.requestCount, 1)
    }

    func testCancellationInvalidatesSnapshotWithoutReplayAndPreservesCredential() async throws {
        Page0WorkflowURLProtocol.handler = { [self] request in
            if request.url?.path.hasSuffix("/mirrors") == true {
                return self.json(request, #"{"version":"v1","block5":"CCC749CC","block6":"CCC74EBC"}"#)
            }
            throw CancellationError()
        }
        let store = InMemoryBridgeCredentialStore(credential: BridgeCredential(baseURL: baseURL, accessToken: token))
        let model = BridgeConnectionModel(credentialStore: store, session: workflowSession())
        await model.readPage0Rides()
        model.targetPage0RidesText = "1"
        await model.setPage0Rides()

        XCTAssertEqual(Page0WorkflowURLProtocol.requestCount, 2)
        XCTAssertFalse(model.hasFreshPage0Snapshot)
        XCTAssertTrue(model.isPaired)
        XCTAssertTrue(model.message?.contains("No retry was sent") == true)
    }

    func testUnauthorizedPage0SetClearsCredentialWithoutReplay() async throws {
        Page0WorkflowURLProtocol.handler = { [self] request in
            if request.url?.path.hasSuffix("/mirrors") == true {
                return self.json(request, #"{"version":"v1","block5":"CCC749CC","block6":"CCC74EBC"}"#)
            }
            return self.json(request, #"{"code":"unauthorized","message":"Pair again."}"#, status: 401)
        }
        let store = InMemoryBridgeCredentialStore(credential: BridgeCredential(baseURL: baseURL, accessToken: token))
        let model = BridgeConnectionModel(credentialStore: store, session: workflowSession())
        await model.readPage0Rides()
        model.targetPage0RidesText = "1"
        await model.setPage0Rides()

        XCTAssertEqual(Page0WorkflowURLProtocol.requestCount, 2)
        XCTAssertNil(store.credential)
        XCTAssertFalse(model.isPaired)
        XCTAssertEqual(model.state, .authenticationRequired)
    }

    func testVenusFakePm3SeedPreservesSequenceOnSet() async throws {
        // fake-pm3 seed mirrors: BBC7FD03 / Venus 180.
        let seed = "BBC7FD03"
        XCTAssertEqual(String(format: "%08X", try XCTUnwrap(RideSequence.venus.encode(180))), seed)
        let desired = String(format: "%08X", try XCTUnwrap(RideSequence.venus.encode(181)))
        var desiredFromRequest: String?

        Page0WorkflowURLProtocol.handler = { [self] request in
            if request.url?.path == "/api/v1/hardware/page0/mirrors" {
                return self.json(request, "{\"version\":\"v1\",\"block5\":\"\(seed)\",\"block6\":\"\(seed)\"}")
            }
            XCTAssertEqual(request.url?.path, "/api/v1/hardware/page0/mutations")
            let body = try XCTUnwrap(request.httpBody)
            let object = try XCTUnwrap(try JSONSerialization.jsonObject(with: body) as? [String: Any])
            let mutations = try XCTUnwrap(object["mutations"] as? [[String: Any]])
            XCTAssertEqual(mutations.count, 2)
            XCTAssertEqual(mutations.map { $0["expected"] as? String }, [seed, seed])
            let desiredValues = mutations.compactMap { $0["desired"] as? String }
            XCTAssertEqual(desiredValues, [desired, desired])
            desiredFromRequest = desiredValues.first
            // Must not fall back to Mercury encoding for a Venus seed.
            XCTAssertNotEqual(desiredValues.first, String(format: "%08X", RideSequence.mercury.encode(181)!))
            return self.json(request, "{\"version\":\"v1\",\"status\":\"written\",\"results\":[{\"block\":5,\"status\":\"written\",\"expected\":\"\(seed)\",\"desired\":\"\(desired)\",\"actual\":\"\(desired)\"},{\"block\":6,\"status\":\"written\",\"expected\":\"\(seed)\",\"desired\":\"\(desired)\",\"actual\":\"\(desired)\"}],\"rollbackStatus\":\"notNeeded\",\"rollback\":[]}")
        }

        let model = makeModel()
        await model.readPage0Rides()
        XCTAssertEqual(model.resolvedPage0Rides, 180)
        XCTAssertEqual(model.lastPage0Read?.sequence, .venus)
        XCTAssertTrue(model.hasFreshPage0Snapshot)

        model.targetPage0RidesText = "181"
        await model.setPage0Rides()

        XCTAssertEqual(Page0WorkflowURLProtocol.requestCount, 2)
        XCTAssertEqual(desiredFromRequest, desired)
        XCTAssertEqual(model.resolvedPage0Rides, 181)
        XCTAssertEqual(model.lastPage0Read?.sequence, .venus)
        XCTAssertEqual(model.message, "Page0 rides written and verified.")
    }

    private func makeModel(
        handler: ((URLRequest) throws -> (HTTPURLResponse, Data))? = nil
    ) -> BridgeConnectionModel {
        if let handler { Page0WorkflowURLProtocol.handler = handler }
        let store = InMemoryBridgeCredentialStore(credential: BridgeCredential(baseURL: baseURL, accessToken: token))
        return BridgeConnectionModel(credentialStore: store, session: workflowSession())
    }

    private func workflowSession() -> URLSession {
        let configuration = URLSessionConfiguration.ephemeral
        configuration.protocolClasses = [Page0WorkflowURLProtocol.self]
        return URLSession(configuration: configuration)
    }

    private func json(_ request: URLRequest, _ body: String, status: Int = 200) -> (HTTPURLResponse, Data) {
        (HTTPURLResponse(url: request.url!, statusCode: status, httpVersion: "HTTP/1.1", headerFields: ["Content-Type": "application/json"])!, Data(body.utf8))
    }
}
