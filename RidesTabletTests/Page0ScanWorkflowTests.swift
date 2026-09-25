import Foundation
import XCTest
@testable import RidesTablet

private final class Page0ScanWorkflowURLProtocol: URLProtocol {
    nonisolated(unsafe) static var handler: ((URLRequest) throws -> (HTTPURLResponse, Data))?
    nonisolated(unsafe) static var requestLog: [String] = []

    override class func canInit(with request: URLRequest) -> Bool { true }
    override class func canonicalRequest(for request: URLRequest) -> URLRequest { request }

    override func startLoading() {
        do {
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
            Self.requestLog.append(request.url?.path ?? "")
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
final class Page0ScanWorkflowTests: XCTestCase {
    private let baseURL = URL(string: "http://127.0.0.1:5080")!
    private let token = "scan-workflow-bearer"

    override func tearDown() {
        Page0ScanWorkflowURLProtocol.handler = nil
        Page0ScanWorkflowURLProtocol.requestLog = []
        super.tearDown()
    }

    func testAssemblePage0PreservesBlockOrderAndBigEndianFilenameConvention() throws {
        let scan = try BridgePage0ScanResponse(
            block4: "00000004",
            block5: "DEADBEEF",
            block6: "FACECAFE",
            signalMillivolts: 420
        )
        let missing = try decodeMissing(#"{"version":"v1","blocks":[{"block":0,"value":"00148040"},{"block":1,"value":"00000001"},{"block":2,"value":"00000002"},{"block":3,"value":"00000003"},{"block":7,"value":"00000000"}]}"#)

        let blocks = try Page0ScanWorkflow.assemblePage0(scan: scan, missing: missing)
        XCTAssertEqual(blocks, [0x00148040, 1, 2, 3, 4, 0xDEADBEEF, 0xFACECAFE, 0])

        let directory = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        defer { try? FileManager.default.removeItem(at: directory) }
        let url = try UnknownDumpStore(directory: directory).save(UnknownToken(blocks: blocks))
        XCTAssertEqual(try Data(contentsOf: url).count, 32)
        XCTAssertEqual(
            url.lastPathComponent,
            "elevator-t55xx-00148040-00000001-00000002-00000003-00000004-DEADBEEF-FACECAFE-00000000--rides-UNKNOWN.bin"
        )
    }

    func testKnownScanStopsAfterScanWithoutMissingRequest() async throws {
        let seed = "BBC7FD03"
        Page0ScanWorkflowURLProtocol.handler = { [self] request in
            XCTAssertEqual(request.url?.path, "/api/v1/hardware/page0/scan")
            return self.json(
                request,
                #"{"version":"v1","block4":"D6D1C733","block5":"\#(seed)","block6":"\#(seed)","signalMillivolts":420}"#
            )
        }

        let model = makeModel()
        await model.scanPage0Token()

        XCTAssertEqual(Page0ScanWorkflowURLProtocol.requestLog, ["/api/v1/hardware/page0/scan"])
        XCTAssertEqual(model.lastScanBlock4Value, "D6D1C733")
        XCTAssertEqual(model.lastSignalMillivolts, 420)
        XCTAssertEqual(model.resolvedPage0Rides, 180)
        XCTAssertEqual(model.lastPage0Read?.sequence, .venus)
        XCTAssertNil(model.lastUnknownDumpURL)
        XCTAssertEqual(model.state, .connected)
    }

    func testUnknownScanFetchesMissingBlocksOnceAndLogsDump() async throws {
        Page0ScanWorkflowURLProtocol.handler = { [self] request in
            switch request.url?.path {
            case "/api/v1/hardware/page0/scan":
                return self.json(
                    request,
                    #"{"version":"v1","block4":"00000004","block5":"DEADBEEF","block6":"FACECAFE","signalMillivolts":420}"#
                )
            case "/api/v1/hardware/page0/missing":
                return self.json(
                    request,
                    #"{"version":"v1","blocks":[{"block":0,"value":"00148040"},{"block":1,"value":"00000001"},{"block":2,"value":"00000002"},{"block":3,"value":"00000003"},{"block":7,"value":"00000000"}]}"#
                )
            default:
                XCTFail("Unexpected path \(request.url?.path ?? "")")
                return self.json(request, #"{"code":"bad","message":"unexpected"}"#, status: 500)
            }
        }

        let directory = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        defer { try? FileManager.default.removeItem(at: directory) }
        let model = makeModel(dumpStore: UnknownDumpStore(directory: directory))
        await model.scanPage0Token()

        XCTAssertEqual(Page0ScanWorkflowURLProtocol.requestLog, [
            "/api/v1/hardware/page0/scan",
            "/api/v1/hardware/page0/missing",
        ])
        XCTAssertNotNil(model.lastUnknownDumpURL)
        XCTAssertEqual(model.message, RidesViewModel.unknownMessage)
        XCTAssertEqual(try Data(contentsOf: XCTUnwrap(model.lastUnknownDumpURL)).count, 32)
    }

    func testUnknownPersistenceFailureRemainsErrorWithoutUnknownLoggedMessage() async throws {
        let destination = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try Data("not a directory".utf8).write(to: destination)
        defer { try? FileManager.default.removeItem(at: destination) }

        Page0ScanWorkflowURLProtocol.handler = { [self] request in
            switch request.url?.path {
            case "/api/v1/hardware/page0/scan":
                return self.json(
                    request,
                    #"{"version":"v1","block4":"00000004","block5":"DEADBEEF","block6":"FACECAFE","signalMillivolts":420}"#
                )
            case "/api/v1/hardware/page0/missing":
                return self.json(
                    request,
                    #"{"version":"v1","blocks":[{"block":0,"value":"00148040"},{"block":1,"value":"00000001"},{"block":2,"value":"00000002"},{"block":3,"value":"00000003"},{"block":7,"value":"00000000"}]}"#
                )
            default:
                return self.json(request, #"{"code":"bad","message":"unexpected"}"#, status: 500)
            }
        }

        let model = makeModel(dumpStore: UnknownDumpStore(directory: destination))
        await model.scanPage0Token()

        XCTAssertNil(model.lastUnknownDumpURL)
        XCTAssertTrue(model.message?.hasPrefix("Unknown token — log failed:") == true)
        XCTAssertEqual(model.state, .failed(model.message!))
        XCTAssertNotEqual(model.message, RidesViewModel.unknownMessage)
    }

    func testInterruptedMissingBlockReadDoesNotReportSuccessfulDump() async throws {
        Page0ScanWorkflowURLProtocol.handler = { [self] request in
            switch request.url?.path {
            case "/api/v1/hardware/page0/scan":
                return self.json(
                    request,
                    #"{"version":"v1","block4":"00000004","block5":"DEADBEEF","block6":"FACECAFE","signalMillivolts":420}"#
                )
            case "/api/v1/hardware/page0/missing":
                throw CancellationError()
            default:
                return self.json(request, #"{"code":"bad","message":"unexpected"}"#, status: 500)
            }
        }

        let directory = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        defer { try? FileManager.default.removeItem(at: directory) }
        let model = makeModel(dumpStore: UnknownDumpStore(directory: directory))
        await model.scanPage0Token()

        XCTAssertNil(model.lastUnknownDumpURL)
        XCTAssertNotEqual(model.message, RidesViewModel.unknownMessage)
        XCTAssertTrue(model.message?.contains("cancelled") == true)
    }

    func testNoChipScanDoesNotRequestMissingBlocks() async throws {
        Page0ScanWorkflowURLProtocol.handler = { [self] request in
            self.json(request, #"{"code":"no_chip","message":"No supported T55xx chip is present."}"#, status: 409)
        }

        let model = makeModel()
        await model.scanPage0Token()

        XCTAssertEqual(Page0ScanWorkflowURLProtocol.requestLog, ["/api/v1/hardware/page0/scan"])
        XCTAssertEqual(model.message, RidesViewModel.noChipMessage)
        XCTAssertNil(model.lastUnknownDumpURL)
    }

    private func makeModel(
        dumpStore: UnknownDumpStore = UnknownDumpStore(directory: FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString))
    ) -> BridgeConnectionModel {
        let store = InMemoryBridgeCredentialStore(credential: BridgeCredential(baseURL: baseURL, accessToken: token))
        return BridgeConnectionModel(credentialStore: store, session: workflowSession(), dumpStore: dumpStore)
    }

    private func workflowSession() -> URLSession {
        let configuration = URLSessionConfiguration.ephemeral
        configuration.protocolClasses = [Page0ScanWorkflowURLProtocol.self]
        return URLSession(configuration: configuration)
    }

    private func json(_ request: URLRequest, _ body: String, status: Int = 200) -> (HTTPURLResponse, Data) {
        (HTTPURLResponse(url: request.url!, statusCode: status, httpVersion: "HTTP/1.1", headerFields: ["Content-Type": "application/json"])!, Data(body.utf8))
    }

    private func decodeMissing(_ json: String) throws -> BridgePage0MissingBlocksResponse {
        try JSONDecoder().decode(BridgePage0MissingBlocksResponse.self, from: Data(json.utf8))
    }
}
