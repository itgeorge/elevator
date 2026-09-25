import Foundation
import XCTest
@testable import RidesTablet

private final class NetworkRideTokenDeviceURLProtocol: URLProtocol {
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

final class NetworkRideTokenDeviceTests: XCTestCase {
    private let baseURL = URL(string: "http://127.0.0.1:5080")!
    private let bearer = "network-device-bearer"

    override func tearDown() {
        NetworkRideTokenDeviceURLProtocol.handler = nil
        NetworkRideTokenDeviceURLProtocol.requestLog = []
        super.tearDown()
    }

    func testKnownScanUsesPage0ScanOnly() async throws {
        NetworkRideTokenDeviceURLProtocol.handler = { [self] request in
            XCTAssertEqual(request.url?.path, "/api/v1/hardware/page0/scan")
            return self.json(
                request,
                #"{"version":"v1","block4":"D6D1C733","block5":"BBC7FD03","block6":"BBC7FD03","signalMillivolts":420}"#
            )
        }

        let device = try makeDevice()

        let outcome = await device.scan()

        XCTAssertEqual(NetworkRideTokenDeviceURLProtocol.requestLog, ["/api/v1/hardware/page0/scan"])
        guard case .known(let token, let signal) = outcome else {
            return XCTFail("Expected known scan")
        }
        XCTAssertEqual(signal, 420)
        XCTAssertEqual(token.sequence, .venus)
        XCTAssertEqual(token.rideCount, 180)
        XCTAssertEqual(token.block4, 0xD6D1C733)
    }

    func testUnknownScanFetchesMissingBlocksOnce() async throws {
        NetworkRideTokenDeviceURLProtocol.handler = { [self] request in
            switch request.url?.path {
            case "/api/v1/hardware/page0/scan":
                return self.json(
                    request,
                    #"{"version":"v1","block4":"00000004","block5":"DEADBEEF","block6":"FACECAFE","signalMillivolts":333}"#
                )
            case "/api/v1/hardware/page0/missing":
                return self.json(
                    request,
                    #"{"version":"v1","blocks":[{"block":0,"value":"00148040"},{"block":1,"value":"00000001"},{"block":2,"value":"00000002"},{"block":3,"value":"00000003"},{"block":7,"value":"00000000"}]}"#
                )
            default:
                return self.json(request, #"{"code":"unexpected","message":"bad path"}"#, status: 500)
            }
        }

        let device = try makeDevice()

        let outcome = await device.scan()

        XCTAssertEqual(NetworkRideTokenDeviceURLProtocol.requestLog, [
            "/api/v1/hardware/page0/scan",
            "/api/v1/hardware/page0/missing",
        ])
        guard case .unknown(let token, let signal) = outcome else {
            return XCTFail("Expected unknown scan")
        }
        XCTAssertEqual(signal, 333)
        XCTAssertEqual(token.blocks, [0x00148040, 1, 2, 3, 4, 0xDEADBEEF, 0xFACECAFE, 0])
    }

    func testNoChipScanDoesNotRequestMissingBlocks() async throws {
        NetworkRideTokenDeviceURLProtocol.handler = { [self] request in
            self.json(request, #"{"code":"no_chip","message":"No chip"}"#, status: 409)
        }

        let device = try makeDevice()

        let outcome = await device.scan()

        XCTAssertEqual(outcome, .noChip)
        XCTAssertEqual(NetworkRideTokenDeviceURLProtocol.requestLog, ["/api/v1/hardware/page0/scan"])
    }

    func testUnreachableScanDoesNotRequestMissingBlocks() async throws {
        NetworkRideTokenDeviceURLProtocol.handler = { _ in
            throw URLError(.cannotConnectToHost)
        }

        let device = try makeDevice()

        let outcome = await device.scan()

        guard case .failure(let message) = outcome else {
            return XCTFail("Expected failure outcome")
        }
        XCTAssertTrue(message.contains(BridgeClientError.unreachable.localizedDescription ?? ""))
        XCTAssertNotEqual(message, RidesViewModel.noChipMessage)
        XCTAssertEqual(NetworkRideTokenDeviceURLProtocol.requestLog, ["/api/v1/hardware/page0/scan"])
    }

    func testTimedOutScanMapsToUnreachableStyleFailure() async throws {
        NetworkRideTokenDeviceURLProtocol.handler = { _ in
            throw URLError(.timedOut)
        }

        let device = try makeDevice()

        let outcome = await device.scan()

        guard case .failure(let message) = outcome else {
            return XCTFail("Expected failure outcome")
        }
        XCTAssertTrue(message.contains(BridgeClientError.timeout.localizedDescription ?? ""))
        XCTAssertNotEqual(message, RidesViewModel.noChipMessage)
        XCTAssertEqual(NetworkRideTokenDeviceURLProtocol.requestLog, ["/api/v1/hardware/page0/scan"])
    }

    @MainActor
    func testViewModelDetectUnreachableShowsActionableMessageNotNoChip() async throws {
        NetworkRideTokenDeviceURLProtocol.handler = { _ in
            throw URLError(.cannotConnectToHost)
        }

        let device = try makeDevice()
        let model = RidesViewModel(device: device)

        await model.detect()

        guard case .failed(let error) = model.state else {
            return XCTFail("Expected failed state")
        }
        XCTAssertTrue(error.contains(BridgeClientError.unreachable.localizedDescription ?? ""))
        XCTAssertNotEqual(model.message, RidesViewModel.noChipMessage)
        XCTAssertNil(model.loadedToken)
        XCTAssertEqual(NetworkRideTokenDeviceURLProtocol.requestLog, ["/api/v1/hardware/page0/scan"])
    }

    func testChargeMutatesOnlyBlocksFiveAndSixFromLastReadExpected() async throws {
        let token = Token.sample(rideCount: 180, sequence: .venus)
        let desired = try XCTUnwrap(RideSequence.venus.encode(200))
        let desiredHex = Token.hex(desired)
        var mutationBody: Data?

        NetworkRideTokenDeviceURLProtocol.handler = { [self] request in
            XCTAssertEqual(request.url?.path, "/api/v1/hardware/page0/mutations")
            mutationBody = request.httpBody
            return self.json(request, """
            {"version":"v1","status":"written","results":[
            {"block":5,"status":"written","expected":"\(Token.hex(token.block5))","desired":"\(desiredHex)","actual":"\(desiredHex)"},
            {"block":6,"status":"written","expected":"\(Token.hex(token.block6))","desired":"\(desiredHex)","actual":"\(desiredHex)"}
            ],"rollbackStatus":"notNeeded","rollback":[]}
            """)
        }

        let device = try makeDevice()

        let outcome = await device.writeRideMirrors(RideMirrorWriteRequest(token: token, desiredRides: 200))

        XCTAssertEqual(outcome, .success)
        let body = try XCTUnwrap(mutationBody)
        let object = try XCTUnwrap(JSONSerialization.jsonObject(with: body) as? [String: Any])
        let mutations = try XCTUnwrap(object["mutations"] as? [[String: Any]])
        XCTAssertEqual(mutations.map { $0["block"] as? Int }, [5, 6])
        XCTAssertEqual(mutations.map { $0["expected"] as? String }, [Token.hex(token.block5), Token.hex(token.block6)])
        XCTAssertEqual(mutations.map { $0["desired"] as? String }, [desiredHex, desiredHex])
    }

    func testChargeConflictRequiresRefreshWithoutBlindRetry() async throws {
        let token = Token.sample(rideCount: 180, sequence: .venus)
        NetworkRideTokenDeviceURLProtocol.handler = { [self] request in
            self.json(request, """
            {"version":"v1","status":"conflict","results":[
            {"block":5,"status":"conflict","expected":"\(Token.hex(token.block5))","desired":"48C74948","actual":"11111111"},
            {"block":6,"status":"conflict","expected":"\(Token.hex(token.block6))","desired":"48C74948","actual":"22222222"}
            ],"rollbackStatus":"notNeeded","rollback":[]}
            """)
        }

        let device = try makeDevice()

        let outcome = await device.writeRideMirrors(RideMirrorWriteRequest(token: token, desiredRides: 0))

        guard case .requiresRefresh(let message) = outcome else {
            return XCTFail("Expected requiresRefresh")
        }
        XCTAssertTrue(message.contains("Detect"))
        XCTAssertEqual(NetworkRideTokenDeviceURLProtocol.requestLog.count, 1)
    }

    func testChargeAmbiguousTimeoutRequiresRefresh() async throws {
        let token = Token.sample(rideCount: 180, sequence: .venus)
        NetworkRideTokenDeviceURLProtocol.handler = { _ in
            throw URLError(.timedOut)
        }

        let device = try makeDevice()

        let outcome = await device.writeRideMirrors(RideMirrorWriteRequest(token: token, desiredRides: 190))

        guard case .requiresRefresh(let message) = outcome else {
            return XCTFail("Expected requiresRefresh on ambiguous timeout")
        }
        XCTAssertTrue(message.contains("Detect"))
    }

    func testResetPlansConditionalMutationsAndReturnsVerifiedToken() async throws {
        let venus = ResetSequence.for(.venus)
        let desired5 = Token.hex(venus.resetImage()[5])
        let desired6 = Token.hex(venus.resetImage()[6])

        NetworkRideTokenDeviceURLProtocol.handler = { [self] request in
            if request.url?.path == "/api/v1/hardware/page0/blocks1to6" {
                return self.json(request, """
                {"version":"v1","blocks":[
                {"block":1,"value":"43FE0062"},{"block":2,"value":"5BA494A3"},
                {"block":3,"value":"D6D1C733"},{"block":4,"value":"D6D1C733"},
                {"block":5,"value":"BBC7FD03"},{"block":6,"value":"BBC7FD03"}]}
                """)
            }
            let body = try XCTUnwrap(request.httpBody)
            let object = try XCTUnwrap(JSONSerialization.jsonObject(with: body) as? [String: Any])
            let mutations = try XCTUnwrap(object["mutations"] as? [[String: Any]])
            XCTAssertEqual(mutations.map { $0["block"] as? Int }, [5, 6])
            return self.json(request, """
            {"version":"v1","status":"written","results":[
            {"block":5,"status":"written","expected":"BBC7FD03","desired":"\(desired5)","actual":"\(desired5)"},
            {"block":6,"status":"written","expected":"BBC7FD03","desired":"\(desired6)","actual":"\(desired6)"}
            ],"rollbackStatus":"notNeeded","rollback":[]}
            """)
        }

        let device = try makeDevice()

        let outcome = await device.reset(ResetMutationRequest(sequence: .venus))

        XCTAssertEqual(NetworkRideTokenDeviceURLProtocol.requestLog, [
            "/api/v1/hardware/page0/blocks1to6",
            "/api/v1/hardware/page0/mutations",
        ])
        guard case .success(let token) = outcome else {
            return XCTFail("Expected successful reset")
        }
        XCTAssertEqual(token.sequence, .venus)
        XCTAssertEqual(token.rideCount, 0)
        XCTAssertEqual(token.block5, venus.resetImage()[5])
        XCTAssertEqual(token.blocks[0], venus.block0)
        XCTAssertEqual(token.blocks[7], venus.block7)
    }

    @MainActor
    func testViewModelChargeConflictClearsTokenUntilDetect() async {
        let fake = FakeProxmark()
        fake.set(write: .requiresRefresh("Charge conflicted with a changed token. Tap Detect and try again."))
        let model = RidesViewModel(device: fake)
        await model.detect()
        model.adjustRides(by: 10)
        await model.charge()

        XCTAssertNil(model.loadedToken)
        XCTAssertEqual(model.currentRides, 0)
        XCTAssertEqual(model.pendingRides, 0)
        guard case .failed = model.state else {
            return XCTFail("Expected failed state requiring refresh")
        }
        XCTAssertTrue(model.message?.contains("Detect") == true)
        XCTAssertFalse(model.canCharge)
    }

    private func makeDevice() throws -> NetworkRideTokenDevice {
        let configuration = URLSessionConfiguration.ephemeral
        configuration.protocolClasses = [NetworkRideTokenDeviceURLProtocol.self]
        let session = URLSession(configuration: configuration)
        let credential = BridgeCredential(baseURL: baseURL, accessToken: bearer, tokenType: "Bearer", bridgeId: "bridge-1")
        let client = try BridgeClient(baseURL: baseURL, session: session, credential: credential)
        return NetworkRideTokenDevice(client: client)
    }

    private func json(_ request: URLRequest, _ body: String, status: Int = 200) -> (HTTPURLResponse, Data) {
        (
            HTTPURLResponse(
                url: request.url!,
                statusCode: status,
                httpVersion: "HTTP/1.1",
                headerFields: ["Content-Type": "application/json"]
            )!,
            Data(body.utf8)
        )
    }
}
