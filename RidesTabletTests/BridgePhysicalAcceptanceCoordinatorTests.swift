#if DEBUG

import Foundation
import XCTest
@testable import RidesTablet

private final class PhysicalAcceptanceURLProtocol: URLProtocol {
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

#if DEBUG
@MainActor
final class BridgePhysicalAcceptanceCoordinatorTests: XCTestCase {
    private let baseURL = URL(string: "http://127.0.0.1:5080")!
    private let bearer = "acceptance-test-bearer"
    private let original5 = String(format: "%08X", RideSequence.mercury.encode(3)!)
    private let original6 = String(format: "%08X", RideSequence.mercury.encode(4)!)

    override func tearDown() {
        PhysicalAcceptanceURLProtocol.handler = nil
        PhysicalAcceptanceURLProtocol.requestCount = 0
        super.tearDown()
    }

    func testLaunchConfigurationUsesExactTriggerKeyAndValue() {
        let enabled = BridgeConnectionLaunchConfiguration(environment: [
            "RIDES_PHASE2_PHYSICAL_ACCEPTANCE": "1"
        ])
        XCTAssertTrue(enabled.physicalAcceptanceEnabled)

        XCTAssertFalse(BridgeConnectionLaunchConfiguration(environment: [
            "RIDES_PHASE2_PHYSICAL_ACCEPTANCE": "true"
        ]).physicalAcceptanceEnabled)
        XCTAssertFalse(BridgeConnectionLaunchConfiguration(environment: [
            "RIDES_PHASE2_PHYSICAL_ACCEPTANCE_EXTRA": "1"
        ]).physicalAcceptanceEnabled)
    }

    func testSuccessUsesExactSevenRequestSequenceAndRestoresAsymmetricOriginals() async throws {
        var requests: [URLRequest] = []
        let targetRaw = String(format: "%08X", RideSequence.mercury.encode(5)!)
        let secondRaw = String(format: "%08X", RideSequence.mercury.encode(6)!)

        PhysicalAcceptanceURLProtocol.handler = { [self] request in
            requests.append(request)
            XCTAssertEqual(request.value(forHTTPHeaderField: "Authorization"), "Bearer \(self.bearer)")
            XCTAssertEqual(request.cachePolicy, .reloadIgnoringLocalCacheData)
            XCTAssertEqual(request.value(forHTTPHeaderField: "Cache-Control"), "no-cache")
            switch requests.count {
            case 1:
                XCTAssertEqual(request.httpMethod, "GET")
                XCTAssertEqual(request.url?.path, "/api/v1/hardware/page0/mirrors")
                return self.json(request, "{\"version\":\"v1\",\"block5\":\"\(self.original5)\",\"block6\":\"\(self.original6)\"}")
            case 2:
                try self.assertMutation(request, expected: [self.original5, self.original6], desired: [targetRaw, targetRaw])
                return self.mutationResponse(request, status: "written", blockStatus: "written", expected: [self.original5, self.original6], desired: [targetRaw, targetRaw], actual: [targetRaw, targetRaw])
            case 3:
                try self.assertMutation(request, expected: [targetRaw, targetRaw], desired: [targetRaw, targetRaw])
                return self.mutationResponse(request, status: "alreadyApplied", blockStatus: "alreadyApplied", expected: [targetRaw, targetRaw], desired: [targetRaw, targetRaw], actual: [targetRaw, targetRaw])
            case 4:
                try self.assertMutation(request, expected: [self.original5, self.original6], desired: [secondRaw, secondRaw])
                return self.mutationResponse(request, status: "conflict", blockStatus: "conflict", expected: [self.original5, self.original6], desired: [secondRaw, secondRaw], actual: [targetRaw, targetRaw])
            case 5:
                XCTAssertEqual(request.httpMethod, "GET")
                XCTAssertEqual(request.url?.path, "/api/v1/hardware/page0/mirrors")
                return self.json(request, "{\"version\":\"v1\",\"block5\":\"\(targetRaw)\",\"block6\":\"\(targetRaw)\"}")
            case 6:
                try self.assertMutation(request, expected: [targetRaw, targetRaw], desired: [self.original5, self.original6])
                return self.mutationResponse(request, status: "written", blockStatus: "written", expected: [targetRaw, targetRaw], desired: [self.original5, self.original6], actual: [self.original5, self.original6])
            case 7:
                XCTAssertEqual(request.httpMethod, "GET")
                XCTAssertEqual(request.url?.path, "/api/v1/hardware/page0/mirrors")
                return self.json(request, "{\"version\":\"v1\",\"block5\":\"\(self.original5)\",\"block6\":\"\(self.original6)\"}")
            default:
                XCTFail("Unexpected request \(requests.count)")
                return self.json(request, "{}", status: 500)
            }
        }

        let client = try BridgeClient(
            baseURL: baseURL,
            session: physicalAcceptanceSession(),
            credential: BridgeCredential(baseURL: baseURL, accessToken: bearer)
        )
        var output: [String] = []
        let result = await BridgePhysicalAcceptanceCoordinator(client: client, log: { output.append($0) }).run()

        guard case .success(let summary) = result else { return XCTFail("Expected acceptance success: \(result)") }
        XCTAssertEqual(summary.originalBlock5, original5)
        XCTAssertEqual(summary.originalBlock6, original6)
        XCTAssertEqual(summary.targetRides, 5)
        XCTAssertEqual(summary.secondTargetRides, 6)
        XCTAssertEqual(PhysicalAcceptanceURLProtocol.requestCount, 7)
        XCTAssertEqual(requests.map { "\($0.httpMethod!) \($0.url!.path)" }, [
            "GET /api/v1/hardware/page0/mirrors",
            "POST /api/v1/hardware/page0/mutations",
            "POST /api/v1/hardware/page0/mutations",
            "POST /api/v1/hardware/page0/mutations",
            "GET /api/v1/hardware/page0/mirrors",
            "POST /api/v1/hardware/page0/mutations",
            "GET /api/v1/hardware/page0/mirrors"
        ])
        XCTAssertEqual(output.count, 2)
        XCTAssertTrue(output[0].hasPrefix("RIDES_PHASE2_ACCEPTANCE_ORIGINAL "))
        XCTAssertEqual(output[1], "RIDES_PHASE2_ACCEPTANCE_SUCCESS original5=\(original5) original6=\(original6) targetRides=5")
        XCTAssertFalse(output.joined(separator: " ").contains(bearer))
    }

    func testEveryNetworkStageStopsWithoutRetryOrAutomaticRestore() async throws {
        for failingRequest in 1...7 {
            PhysicalAcceptanceURLProtocol.requestCount = 0
            var requestNumber = 0
            PhysicalAcceptanceURLProtocol.handler = { [self] request in
                requestNumber += 1
                if requestNumber == failingRequest { throw URLError(.timedOut) }
                let targetRaw = String(format: "%08X", RideSequence.mercury.encode(5)!)
                let secondRaw = String(format: "%08X", RideSequence.mercury.encode(6)!)
                switch requestNumber {
                case 1:
                    return self.json(request, "{\"version\":\"v1\",\"block5\":\"\(self.original5)\",\"block6\":\"\(self.original6)\"}")
                case 2:
                    return self.mutationResponse(request, status: "written", blockStatus: "written", expected: [self.original5, self.original6], desired: [targetRaw, targetRaw], actual: [targetRaw, targetRaw])
                case 3:
                    return self.mutationResponse(request, status: "alreadyApplied", blockStatus: "alreadyApplied", expected: [targetRaw, targetRaw], desired: [targetRaw, targetRaw], actual: [targetRaw, targetRaw])
                case 4:
                    return self.mutationResponse(request, status: "conflict", blockStatus: "conflict", expected: [self.original5, self.original6], desired: [secondRaw, secondRaw], actual: [targetRaw, targetRaw])
                case 5:
                    return self.json(request, "{\"version\":\"v1\",\"block5\":\"\(targetRaw)\",\"block6\":\"\(targetRaw)\"}")
                case 6:
                    return self.mutationResponse(request, status: "written", blockStatus: "written", expected: [targetRaw, targetRaw], desired: [self.original5, self.original6], actual: [self.original5, self.original6])
                case 7:
                    return self.json(request, "{\"version\":\"v1\",\"block5\":\"\(self.original5)\",\"block6\":\"\(self.original6)\"}")
                default:
                    return self.json(request, "{}", status: 500)
                }
            }

            let client = try BridgeClient(
                baseURL: baseURL,
                session: physicalAcceptanceSession(),
                credential: BridgeCredential(baseURL: baseURL, accessToken: bearer)
            )
            var output: [String] = []
            let result = await BridgePhysicalAcceptanceCoordinator(client: client, log: { output.append($0) }).run()
            guard case .failure(let failure) = result else { return XCTFail("Expected request \(failingRequest) to fail") }
            XCTAssertEqual(PhysicalAcceptanceURLProtocol.requestCount, failingRequest)
            XCTAssertEqual(failure.stage, stage(for: failingRequest))
            XCTAssertEqual(output.count, failingRequest == 1 ? 1 : 2)
            XCTAssertTrue(output.last?.hasPrefix("RIDES_PHASE2_ACCEPTANCE_FAILURE stage=") == true)
        }
    }

    func testModelLaunchTriggerRunsOnceAndRequiresSavedCredential() async throws {
        installSuccessHandler()
        let store = InMemoryBridgeCredentialStore(credential: BridgeCredential(baseURL: baseURL, accessToken: bearer))
        let model = BridgeConnectionModel(credentialStore: store, session: physicalAcceptanceSession())

        await model.runLaunchPhysicalAcceptanceIfRequested()
        await model.runLaunchPhysicalAcceptanceIfRequested()

        XCTAssertEqual(PhysicalAcceptanceURLProtocol.requestCount, 7)
        XCTAssertNotNil(model.physicalAcceptanceSummary)
        XCTAssertNil(model.physicalAcceptanceFailure)

        PhysicalAcceptanceURLProtocol.requestCount = 0
        let unpaired = BridgeConnectionModel(credentialStore: InMemoryBridgeCredentialStore(), session: physicalAcceptanceSession())
        await unpaired.runLaunchPhysicalAcceptanceIfRequested()
        XCTAssertEqual(PhysicalAcceptanceURLProtocol.requestCount, 0)
        XCTAssertNil(unpaired.physicalAcceptanceSummary)
    }

    private func installSuccessHandler() {
        let targetRaw = String(format: "%08X", RideSequence.mercury.encode(5)!)
        let secondRaw = String(format: "%08X", RideSequence.mercury.encode(6)!)
        PhysicalAcceptanceURLProtocol.handler = { [self] request in
            switch PhysicalAcceptanceURLProtocol.requestCount {
            case 1:
                return self.json(request, "{\"version\":\"v1\",\"block5\":\"\(self.original5)\",\"block6\":\"\(self.original6)\"}")
            case 2:
                return self.mutationResponse(request, status: "written", blockStatus: "written", expected: [self.original5, self.original6], desired: [targetRaw, targetRaw], actual: [targetRaw, targetRaw])
            case 3:
                return self.mutationResponse(request, status: "alreadyApplied", blockStatus: "alreadyApplied", expected: [targetRaw, targetRaw], desired: [targetRaw, targetRaw], actual: [targetRaw, targetRaw])
            case 4:
                return self.mutationResponse(request, status: "conflict", blockStatus: "conflict", expected: [self.original5, self.original6], desired: [secondRaw, secondRaw], actual: [targetRaw, targetRaw])
            case 5:
                return self.json(request, "{\"version\":\"v1\",\"block5\":\"\(targetRaw)\",\"block6\":\"\(targetRaw)\"}")
            case 6:
                return self.mutationResponse(request, status: "written", blockStatus: "written", expected: [targetRaw, targetRaw], desired: [self.original5, self.original6], actual: [self.original5, self.original6])
            default:
                return self.json(request, "{\"version\":\"v1\",\"block5\":\"\(self.original5)\",\"block6\":\"\(self.original6)\"}")
            }
        }
    }

    private func stage(for request: Int) -> String {
        [
            BridgePhysicalAcceptanceCoordinator.initialReadStage,
            BridgePhysicalAcceptanceCoordinator.firstMutationStage,
            BridgePhysicalAcceptanceCoordinator.alreadyAppliedStage,
            BridgePhysicalAcceptanceCoordinator.staleConflictStage,
            BridgePhysicalAcceptanceCoordinator.targetReadStage,
            BridgePhysicalAcceptanceCoordinator.restoreStage,
            BridgePhysicalAcceptanceCoordinator.finalReadStage
        ][request - 1]
    }

    private func physicalAcceptanceSession() -> URLSession {
        let configuration = URLSessionConfiguration.ephemeral
        configuration.protocolClasses = [PhysicalAcceptanceURLProtocol.self]
        return URLSession(configuration: configuration)
    }

    private func json(_ request: URLRequest, _ body: String, status: Int = 200) -> (HTTPURLResponse, Data) {
        (HTTPURLResponse(url: request.url!, statusCode: status, httpVersion: "HTTP/1.1", headerFields: ["Content-Type": "application/json"])!, Data(body.utf8))
    }

    private func assertMutation(_ request: URLRequest, expected: [String], desired: [String]) throws {
        XCTAssertEqual(request.httpMethod, "POST")
        XCTAssertEqual(request.url?.path, "/api/v1/hardware/page0/mutations")
        XCTAssertEqual(request.value(forHTTPHeaderField: "Content-Type"), "application/json")
        let body = try XCTUnwrap(request.httpBody)
        let object = try XCTUnwrap(try JSONSerialization.jsonObject(with: body) as? [String: Any])
        XCTAssertEqual(object["version"] as? String, "v1")
        let mutations = try XCTUnwrap(object["mutations"] as? [[String: Any]])
        XCTAssertEqual(mutations.map { $0["block"] as? Int }, [5, 6])
        XCTAssertEqual(mutations.map { $0["expected"] as? String }, expected)
        XCTAssertEqual(mutations.map { $0["desired"] as? String }, desired)
    }

    private func mutationResponse(
        _ request: URLRequest,
        status: String,
        blockStatus: String,
        expected: [String],
        desired: [String],
        actual: [String]
    ) -> (HTTPURLResponse, Data) {
        let results = (0..<2).map { index in
            "{\"block\":\(index + 5),\"status\":\"\(blockStatus)\",\"expected\":\"\(expected[index])\",\"desired\":\"\(desired[index])\",\"actual\":\"\(actual[index])\"}"
        }.joined(separator: ",")
        let body = "{\"version\":\"v1\",\"status\":\"\(status)\",\"results\":[\(results)],\"rollbackStatus\":\"notNeeded\",\"rollback\":[]}"
        return json(request, body)
    }
}
#endif

#endif
