#if DEBUG

import Foundation
import XCTest
@testable import RidesTablet

private final class Slice4PhysicalAcceptanceURLProtocol: URLProtocol {
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
final class BridgeSlice4PhysicalAcceptanceCoordinatorTests: XCTestCase {
    private let baseURL = URL(string: "http://127.0.0.1:5080")!
    private let bearer = "slice4-acceptance-test-bearer"
    private let block4 = BridgeSlice4PhysicalAcceptanceCoordinator.expectedBlock4
    private let original5 = BridgeSlice4PhysicalAcceptanceCoordinator.expectedBlock5
    private let original6 = BridgeSlice4PhysicalAcceptanceCoordinator.expectedBlock6
    private let signal = BridgeSlice4PhysicalAcceptanceCoordinator.expectedSignalMillivolts
    private let reset5 = String(format: "%08X", RideSequence.venus.zeroBlock)
    private var reset6: String { reset5 }

    override func tearDown() {
        Slice4PhysicalAcceptanceURLProtocol.handler = nil
        Slice4PhysicalAcceptanceURLProtocol.requestCount = 0
        super.tearDown()
    }

    func testLaunchConfigurationUsesExactSlice4TriggerKeyAndValue() {
        let enabled = BridgeConnectionLaunchConfiguration(environment: [
            "RIDES_PHASE4_PHYSICAL_ACCEPTANCE": "1"
        ])
        XCTAssertTrue(enabled.slice4PhysicalAcceptanceEnabled)

        XCTAssertFalse(BridgeConnectionLaunchConfiguration(environment: [
            "RIDES_PHASE4_PHYSICAL_ACCEPTANCE": "true"
        ]).slice4PhysicalAcceptanceEnabled)
        XCTAssertFalse(BridgeConnectionLaunchConfiguration(environment: [
            "RIDES_PHASE4_PHYSICAL_ACCEPTANCE_EXTRA": "1"
        ]).slice4PhysicalAcceptanceEnabled)
    }

    func testSuccessUsesExactFiveRequestSequenceAndRestoresOriginalMirrors() async throws {
        var requests: [URLRequest] = []
        Slice4PhysicalAcceptanceURLProtocol.handler = { [self] request in
            requests.append(request)
            XCTAssertEqual(request.value(forHTTPHeaderField: "Authorization"), "Bearer \(self.bearer)")
            XCTAssertEqual(request.cachePolicy, .reloadIgnoringLocalCacheData)
            XCTAssertEqual(request.value(forHTTPHeaderField: "Cache-Control"), "no-cache")
            switch requests.count {
            case 1:
                XCTAssertEqual(request.httpMethod, "GET")
                XCTAssertEqual(request.url?.path, "/api/v1/hardware/page0/scan")
                return self.json(request, """
                {"version":"v1","block4":"\(self.block4)","block5":"\(self.original5)","block6":"\(self.original6)","signalMillivolts":\(self.signal)}
                """)
            case 2:
                XCTAssertEqual(request.httpMethod, "GET")
                XCTAssertEqual(request.url?.path, "/api/v1/hardware/page0/blocks1to6")
                return self.json(request, """
                {"version":"v1","blocks":[
                {"block":1,"value":"43FE0062"},{"block":2,"value":"5BA494A3"},
                {"block":3,"value":"D6D1C733"},{"block":4,"value":"D6D1C733"},
                {"block":5,"value":"\(self.original5)"},{"block":6,"value":"\(self.original6)"}]}
                """)
            case 3:
                try self.assertMutation(request, expected: [self.original5, self.original6], desired: [self.reset5, self.reset6])
                return self.mutationResponse(request, status: "written", blockStatus: "written", expected: [self.original5, self.original6], desired: [self.reset5, self.reset6], actual: [self.reset5, self.reset6])
            case 4:
                try self.assertMutation(request, expected: [self.reset5, self.reset6], desired: [self.original5, self.original6])
                return self.mutationResponse(request, status: "written", blockStatus: "written", expected: [self.reset5, self.reset6], desired: [self.original5, self.original6], actual: [self.original5, self.original6])
            case 5:
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
            session: slice4PhysicalAcceptanceSession(),
            credential: BridgeCredential(baseURL: baseURL, accessToken: bearer)
        )
        var output: [String] = []
        let result = await BridgeSlice4PhysicalAcceptanceCoordinator(client: client, log: { output.append($0) }).run()

        guard case .success(let summary) = result else { return XCTFail("Expected acceptance success: \(result)") }
        XCTAssertEqual(summary.block4, block4)
        XCTAssertEqual(summary.originalBlock5, original5)
        XCTAssertEqual(summary.originalBlock6, original6)
        XCTAssertEqual(summary.signalMillivolts, signal)
        XCTAssertEqual(summary.originalRides, 180)
        XCTAssertEqual(summary.resetBlock5, reset5)
        XCTAssertEqual(summary.resetBlock6, reset6)
        XCTAssertEqual(Slice4PhysicalAcceptanceURLProtocol.requestCount, 5)
        XCTAssertEqual(requests.map { "\($0.httpMethod!) \($0.url!.path)" }, [
            "GET /api/v1/hardware/page0/scan",
            "GET /api/v1/hardware/page0/blocks1to6",
            "POST /api/v1/hardware/page0/mutations",
            "POST /api/v1/hardware/page0/mutations",
            "GET /api/v1/hardware/page0/mirrors"
        ])
        XCTAssertEqual(output.count, 6)
        XCTAssertTrue(output[0].hasPrefix("RIDES_PHASE4_ACCEPTANCE_SCAN "))
        XCTAssertEqual(output[1], "RIDES_PHASE4_ACCEPTANCE_RESET_PLAN blocks=5,6 desired5=\(reset5) desired6=\(reset6)")
        XCTAssertEqual(output[2], "RIDES_PHASE4_ACCEPTANCE_RESET actual5=\(reset5) actual6=\(reset6)")
        XCTAssertEqual(output[3], "RIDES_PHASE4_ACCEPTANCE_POST_RESET rides=0 sequence=venus")
        XCTAssertEqual(output[4], "RIDES_PHASE4_ACCEPTANCE_RESTORE actual5=\(original5) actual6=\(original6)")
        XCTAssertEqual(output[5], "RIDES_PHASE4_ACCEPTANCE_SUCCESS block4=\(block4) original5=\(original5) original6=\(original6) rides=180")
        XCTAssertFalse(output.joined(separator: " ").contains(bearer))
    }

    func testEveryNetworkStageStopsWithoutRetryOrAutomaticRestore() async throws {
        for failingRequest in 1...5 {
            Slice4PhysicalAcceptanceURLProtocol.requestCount = 0
            var requestNumber = 0
            Slice4PhysicalAcceptanceURLProtocol.handler = { [self] request in
                requestNumber += 1
                if requestNumber == failingRequest { throw URLError(.timedOut) }
                switch requestNumber {
                case 1:
                    return self.json(request, """
                    {"version":"v1","block4":"\(self.block4)","block5":"\(self.original5)","block6":"\(self.original6)","signalMillivolts":\(self.signal)}
                    """)
                case 2:
                    return self.json(request, """
                    {"version":"v1","blocks":[
                    {"block":1,"value":"43FE0062"},{"block":2,"value":"5BA494A3"},
                    {"block":3,"value":"D6D1C733"},{"block":4,"value":"D6D1C733"},
                    {"block":5,"value":"\(self.original5)"},{"block":6,"value":"\(self.original6)"}]}
                    """)
                case 3:
                    return self.mutationResponse(request, status: "written", blockStatus: "written", expected: [self.original5, self.original6], desired: [self.reset5, self.reset6], actual: [self.reset5, self.reset6])
                case 4:
                    return self.mutationResponse(request, status: "written", blockStatus: "written", expected: [self.reset5, self.reset6], desired: [self.original5, self.original6], actual: [self.original5, self.original6])
                case 5:
                    return self.json(request, "{\"version\":\"v1\",\"block5\":\"\(self.original5)\",\"block6\":\"\(self.original6)\"}")
                default:
                    return self.json(request, "{}", status: 500)
                }
            }

            let client = try BridgeClient(
                baseURL: baseURL,
                session: slice4PhysicalAcceptanceSession(),
                credential: BridgeCredential(baseURL: baseURL, accessToken: bearer)
            )
            var output: [String] = []
            let result = await BridgeSlice4PhysicalAcceptanceCoordinator(client: client, log: { output.append($0) }).run()
            guard case .failure(let failure) = result else { return XCTFail("Expected request \(failingRequest) to fail") }
            XCTAssertEqual(Slice4PhysicalAcceptanceURLProtocol.requestCount, failingRequest)
            XCTAssertEqual(failure.stage, stage(for: failingRequest))
            XCTAssertEqual(output.count, expectedOutputCount(for: failingRequest))
            XCTAssertTrue(output.last?.hasPrefix("RIDES_PHASE4_ACCEPTANCE_FAILURE stage=") == true)
        }
    }

    func testModelLaunchTriggerRunsOnceAndRequiresSavedCredential() async throws {
        installSuccessHandler()
        let store = InMemoryBridgeCredentialStore(credential: BridgeCredential(baseURL: baseURL, accessToken: bearer))
        let model = BridgeConnectionModel(credentialStore: store, session: slice4PhysicalAcceptanceSession())

        await model.runLaunchSlice4PhysicalAcceptanceIfRequested()
        await model.runLaunchSlice4PhysicalAcceptanceIfRequested()

        XCTAssertEqual(Slice4PhysicalAcceptanceURLProtocol.requestCount, 5)
        XCTAssertNotNil(model.slice4PhysicalAcceptanceSummary)
        XCTAssertNil(model.slice4PhysicalAcceptanceFailure)

        Slice4PhysicalAcceptanceURLProtocol.requestCount = 0
        let unpaired = BridgeConnectionModel(credentialStore: InMemoryBridgeCredentialStore(), session: slice4PhysicalAcceptanceSession())
        await unpaired.runLaunchSlice4PhysicalAcceptanceIfRequested()
        XCTAssertEqual(Slice4PhysicalAcceptanceURLProtocol.requestCount, 0)
        XCTAssertNil(unpaired.slice4PhysicalAcceptanceSummary)
    }

    private func installSuccessHandler() {
        Slice4PhysicalAcceptanceURLProtocol.handler = { [self] request in
            switch Slice4PhysicalAcceptanceURLProtocol.requestCount {
            case 1:
                return self.json(request, """
                {"version":"v1","block4":"\(self.block4)","block5":"\(self.original5)","block6":"\(self.original6)","signalMillivolts":\(self.signal)}
                """)
            case 2:
                return self.json(request, """
                {"version":"v1","blocks":[
                {"block":1,"value":"43FE0062"},{"block":2,"value":"5BA494A3"},
                {"block":3,"value":"D6D1C733"},{"block":4,"value":"D6D1C733"},
                {"block":5,"value":"\(self.original5)"},{"block":6,"value":"\(self.original6)"}]}
                """)
            case 3:
                return self.mutationResponse(request, status: "written", blockStatus: "written", expected: [self.original5, self.original6], desired: [self.reset5, self.reset6], actual: [self.reset5, self.reset6])
            case 4:
                return self.mutationResponse(request, status: "written", blockStatus: "written", expected: [self.reset5, self.reset6], desired: [self.original5, self.original6], actual: [self.original5, self.original6])
            default:
                return self.json(request, "{\"version\":\"v1\",\"block5\":\"\(self.original5)\",\"block6\":\"\(self.original6)\"}")
            }
        }
    }

    private func stage(for request: Int) -> String {
        [
            BridgeSlice4PhysicalAcceptanceCoordinator.scanStage,
            BridgeSlice4PhysicalAcceptanceCoordinator.resetPlanStage,
            BridgeSlice4PhysicalAcceptanceCoordinator.resetMutationStage,
            BridgeSlice4PhysicalAcceptanceCoordinator.restoreStage,
            BridgeSlice4PhysicalAcceptanceCoordinator.finalVerifyStage
        ][request - 1]
    }

    private func expectedOutputCount(for failingRequest: Int) -> Int {
        switch failingRequest {
        case 1: 1
        case 2: 2
        case 3: 3
        case 4: 5
        default: 6
        }
    }

    private func slice4PhysicalAcceptanceSession() -> URLSession {
        let configuration = URLSessionConfiguration.ephemeral
        configuration.protocolClasses = [Slice4PhysicalAcceptanceURLProtocol.self]
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
