import Foundation
import XCTest
@testable import RidesTablet

@MainActor
final class Page0ResetWorkflowTests: XCTestCase {
    private let baseURL = URL(string: "http://127.0.0.1:5080")!
    private let token = "reset-workflow-bearer"

    override func tearDown() {
        Page0ResetURLProtocol.handler = nil
        Page0ResetURLProtocol.requestCount = 0
        super.tearDown()
    }

    func testResetRequiresExplicitSelection() async {
        let model = makeModel()
        await model.confirmResetProfile()
        XCTAssertEqual(Page0ResetURLProtocol.requestCount, 0)
        XCTAssertTrue(model.message?.contains("Choose a reset profile") == true)
    }

    func testMirrorsOnlyResetUsesBlocks1To6ReadAndTargetsOnlyFiveAndSix() async throws {
        let venus = ResetSequence.for(.venus)
        let desired5 = format(venus.resetImage()[5])
        let desired6 = format(venus.resetImage()[6])
        var requests: [URLRequest] = []

        Page0ResetURLProtocol.handler = { [self] request in
            requests.append(request)
            if request.url?.path == "/api/v1/hardware/page0/blocks1to6" {
                return self.json(request, """
                {"version":"v1","blocks":[
                {"block":1,"value":"43FE0062"},{"block":2,"value":"5BA494A3"},
                {"block":3,"value":"D6D1C733"},{"block":4,"value":"D6D1C733"},
                {"block":5,"value":"BBC7FD03"},{"block":6,"value":"BBC7FD03"}]}
                """)
            }
            XCTAssertEqual(request.url?.path, "/api/v1/hardware/page0/mutations")
            let body = try XCTUnwrap(request.httpBody)
            let object = try XCTUnwrap(try JSONSerialization.jsonObject(with: body) as? [String: Any])
            let mutations = try XCTUnwrap(object["mutations"] as? [[String: Any]])
            XCTAssertEqual(mutations.map { $0["block"] as? Int }, [5, 6])
            return self.json(request, """
            {"version":"v1","status":"written","results":[
            {"block":5,"status":"written","expected":"BBC7FD03","desired":"\(desired5)","actual":"\(desired5)"},
            {"block":6,"status":"written","expected":"BBC7FD03","desired":"\(desired6)","actual":"\(desired6)"}
            ],"rollbackStatus":"notNeeded","rollback":[]}
            """)
        }

        let model = makeModel()
        model.selectedResetSequence = .venus
        await model.confirmResetProfile()

        XCTAssertEqual(Page0ResetURLProtocol.requestCount, 2)
        XCTAssertEqual(requests.map { $0.url?.path }, [
            "/api/v1/hardware/page0/blocks1to6",
            "/api/v1/hardware/page0/mutations",
        ])
        XCTAssertEqual(model.lastResetBlockValues[5], desired5)
        XCTAssertEqual(model.lastResetBlockValues[6], desired6)
        XCTAssertEqual(model.resolvedPage0Rides, 0)
        XCTAssertEqual(model.message, "Reset profile written and verified.")
    }

    func testResetConflictInvalidatesSnapshotWithoutRetry() async throws {
        Page0ResetURLProtocol.handler = { [self] request in
            if request.url?.path == "/api/v1/hardware/page0/blocks1to6" {
                return self.json(request, """
                {"version":"v1","blocks":[
                {"block":1,"value":"43FE0062"},{"block":2,"value":"5BA494A3"},
                {"block":3,"value":"D6D1C733"},{"block":4,"value":"D6D1C733"},
                {"block":5,"value":"BBC7FD03"},{"block":6,"value":"BBC7FD03"}]}
                """)
            }
            return self.json(request, """
            {"version":"v1","status":"conflict","results":[
            {"block":5,"status":"conflict","expected":"BBC7FD03","desired":"48C74948","actual":"11111111"},
            {"block":6,"status":"conflict","expected":"BBC7FD03","desired":"48C74948","actual":"22222222"}
            ],"rollbackStatus":"notNeeded","rollback":[]}
            """)
        }

        let model = makeModel()
        model.selectedResetSequence = .venus
        await model.confirmResetProfile()

        XCTAssertEqual(Page0ResetURLProtocol.requestCount, 2)
        XCTAssertFalse(model.hasFreshPage0Snapshot)
        XCTAssertEqual(
            model.message,
            "Reset conflicted with a changed token. No blocks were written and no retry was sent; read page0 blocks again before resetting."
        )
    }

    func testResetVerifyFailureReportsRollbackWarning() async throws {
        Page0ResetURLProtocol.handler = { [self] request in
            if request.url?.path == "/api/v1/hardware/page0/blocks1to6" {
                return self.json(request, """
                {"version":"v1","blocks":[
                {"block":1,"value":"43FE0062"},{"block":2,"value":"5BA494A3"},
                {"block":3,"value":"D6D1C733"},{"block":4,"value":"D6D1C733"},
                {"block":5,"value":"BBC7FD03"},{"block":6,"value":"BBC7FD03"}]}
                """)
            }
            return self.json(request, """
            {"version":"v1","status":"verifyFailed","results":[
            {"block":5,"status":"verifyFailed","expected":"BBC7FD03","desired":"48C74948","actual":"BAD00000"},
            {"block":6,"status":"notAttempted","expected":"BBC7FD03","desired":"48C74948","actual":null}
            ],"rollbackStatus":"rollbackIncomplete","rollback":[
            {"block":5,"expected":"BBC7FD03","actual":"BAD00000","succeeded":false}
            ]}
            """)
        }

        let model = makeModel()
        model.selectedResetSequence = .venus
        await model.confirmResetProfile()

        XCTAssertEqual(Page0ResetURLProtocol.requestCount, 2)
        XCTAssertFalse(model.hasFreshPage0Snapshot)
        XCTAssertTrue(model.message?.contains("rollbackIncomplete") == true)
    }

    func testResetCancellationDoesNotReplay() async throws {
        Page0ResetURLProtocol.handler = { [self] request in
            if request.url?.path == "/api/v1/hardware/page0/blocks1to6" {
                return self.json(request, """
                {"version":"v1","blocks":[
                {"block":1,"value":"43FE0062"},{"block":2,"value":"5BA494A3"},
                {"block":3,"value":"D6D1C733"},{"block":4,"value":"D6D1C733"},
                {"block":5,"value":"BBC7FD03"},{"block":6,"value":"BBC7FD03"}]}
                """)
            }
            throw CancellationError()
        }

        let model = makeModel()
        model.selectedResetSequence = .venus
        await model.confirmResetProfile()

        XCTAssertEqual(Page0ResetURLProtocol.requestCount, 2)
        XCTAssertTrue(model.isPaired)
        XCTAssertTrue(model.message?.contains("cancelled") == true)
    }

    private func makeModel() -> BridgeConnectionModel {
        let store = InMemoryBridgeCredentialStore(credential: BridgeCredential(baseURL: baseURL, accessToken: token))
        return BridgeConnectionModel(credentialStore: store, session: workflowSession())
    }

    private func workflowSession() -> URLSession {
        let configuration = URLSessionConfiguration.ephemeral
        configuration.protocolClasses = [Page0ResetURLProtocol.self]
        return URLSession(configuration: configuration)
    }

    private func json(_ request: URLRequest, _ body: String, status: Int = 200) -> (HTTPURLResponse, Data) {
        (HTTPURLResponse(url: request.url!, statusCode: status, httpVersion: "HTTP/1.1", headerFields: ["Content-Type": "application/json"])!, Data(body.utf8))
    }

    private func format(_ word: UInt32) -> String {
        String(format: "%08X", word)
    }
}

private final class Page0ResetURLProtocol: URLProtocol {
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
