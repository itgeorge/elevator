import Foundation
import Network
import XCTest
@testable import RidesTablet

@MainActor
private final class FakeBonjourBrowserSource: BridgeBonjourBrowserSource {
    struct Session {
        let onState: @Sendable (BridgeBonjourSourceState) -> Void
        let onResults: @Sendable ([BridgeBonjourRawResult]) -> Void
    }

    private(set) var startCount = 0
    private(set) var stopCount = 0
    private(set) var sessions: [Session] = []

    func start(
        onState: @escaping @Sendable (BridgeBonjourSourceState) -> Void,
        onResults: @escaping @Sendable ([BridgeBonjourRawResult]) -> Void
    ) {
        startCount += 1
        sessions.append(Session(onState: onState, onResults: onResults))
    }

    func stop() {
        stopCount += 1
    }

    func emitState(_ state: BridgeBonjourSourceState, session: Int = 0) {
        sessions[session].onState(state)
    }

    func emitResults(_ results: [BridgeBonjourRawResult], session: Int = 0) {
        sessions[session].onResults(results)
    }
}

private final class BonjourModelURLProtocol: URLProtocol {
    nonisolated(unsafe) static var handler: ((URLRequest) throws -> (HTTPURLResponse, Data))?
    nonisolated(unsafe) static var holdRequests = false
    nonisolated(unsafe) static var pendingProtocol: BonjourModelURLProtocol?
    nonisolated(unsafe) static var pendingRequest: URLRequest?
    nonisolated(unsafe) static var requestCount = 0

    override class func canInit(with request: URLRequest) -> Bool { true }
    override class func canonicalRequest(for request: URLRequest) -> URLRequest { request }

    override func startLoading() {
        do {
            Self.requestCount += 1
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
            if Self.holdRequests {
                Self.pendingProtocol = self
                Self.pendingRequest = request
                return
            }
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

    static func respondPending(
        statusCode: Int = 200,
        data: Data = Data(#"{"version":"v1","paired":true}"#.utf8)
    ) {
        guard let pending = pendingProtocol, let client = pending.client, let url = pending.request.url else { return }
        let response = HTTPURLResponse(url: url, statusCode: statusCode, httpVersion: "HTTP/1.1", headerFields: nil)!
        client.urlProtocol(pending, didReceive: response, cacheStoragePolicy: .notAllowed)
        client.urlProtocol(pending, didLoad: data)
        client.urlProtocolDidFinishLoading(pending)
        pendingProtocol = nil
        pendingRequest = nil
    }
}

private func bonjourSession() -> URLSession {
    let configuration = URLSessionConfiguration.ephemeral
    configuration.protocolClasses = [BonjourModelURLProtocol.self]
    return URLSession(configuration: configuration)
}

private func encodedTXT(_ entries: [String]) -> Data {
    var result = Data()
    for entry in entries {
        let bytes = Array(entry.utf8)
        precondition(bytes.count <= 255)
        result.append(UInt8(bytes.count))
        result.append(contentsOf: bytes)
    }
    return result
}

private let bonjourBridgeID = "0123456789ABCDEF0123456789ABCDEF"
private let secondBonjourBridgeID = "ABCDEF0123456789ABCDEF0123456789"

private func validTXT(
    type: String = "elevator-rides",
    bridgeId: String = bonjourBridgeID,
    apiVersion: String = "v1",
    url: String = "http://192.168.1.20:5080/"
) -> Data {
    encodedTXT([
        "type=\(type)",
        "bridgeId=\(bridgeId)",
        "apiVersion=\(apiVersion)",
        "url=\(url)"
    ])
}

private func validProofResponseData(
    for request: URLRequest,
    bearer: String,
    bridgeId: String
) throws -> Data {
    let body = try XCTUnwrap(request.httpBody)
    let proofRequest = try JSONDecoder().decode(BridgePairRelocationProofRequest.self, from: body)
    let proof = try XCTUnwrap(BridgeRelocationProof.proof(
        for: bearer,
        nonce: proofRequest.nonce,
        bridgeId: bridgeId,
        canonicalURL: proofRequest.url
    ))
    return try JSONSerialization.data(withJSONObject: [
        "bridgeId": bridgeId,
        "apiVersion": "v1",
        "nonce": proofRequest.nonce,
        "proof": proof
    ])
}

private func serviceResult(
    name: String = "elevator-rides-a",
    type: String = BridgeBonjourConstants.serviceType,
    domain: String = BridgeBonjourConstants.domain,
    txt: Data = validTXT(),
    onlyStringEntries: Bool = true
) -> BridgeBonjourRawResult {
    BridgeBonjourRawResult(
        endpoint: .service(name: name, type: type, domain: domain),
        txtRecord: txt,
        containsOnlyStringEntries: onlyStringEntries
    )
}

private func waitForBonjourCallbacks() async {
    await Task.yield()
    await Task.yield()
}

@MainActor
private func waitUntil(
    _ condition: @escaping @MainActor () -> Bool
) async {
    for _ in 0..<1_000 {
        if condition() { return }
        await Task.yield()
    }
    XCTFail("Timed out waiting for deterministic test condition")
}

@MainActor
private func waitForAutomaticReconnect() async {
    for _ in 0..<10_000 { await Task.yield() }
}

private final class ControlledAsyncGate: @unchecked Sendable {
    private let lock = NSLock()
    private var continuations: [CheckedContinuation<Void, Error>] = []
    private var count = 0

    var waitCount: Int {
        lock.lock()
        defer { lock.unlock() }
        return count
    }

    func wait() async throws {
        try await withCheckedThrowingContinuation { continuation in
            lock.lock()
            count += 1
            continuations.append(continuation)
            lock.unlock()
        }
    }

    func resumeNext() {
        lock.lock()
        guard !continuations.isEmpty else {
            lock.unlock()
            return
        }
        let continuation = continuations.removeFirst()
        lock.unlock()
        continuation.resume()
    }
}

private enum AutomaticReconnectStoreError: Error {
    case loadFailed
}

private final class AutomaticReconnectLoadFailureStore: BridgeCredentialStore, @unchecked Sendable {
    func load() throws -> BridgeCredential? { throw AutomaticReconnectStoreError.loadFailed }
    func save(_: BridgeCredential) throws {}
    func remove() throws {}
}

final class NetworkBonjourBrowserSourceTests: XCTestCase {
    func testLocalNetworkAuthorizationUsesStableNetworkErrorCodes() {
        XCTAssertTrue(NetworkBonjourBrowserSource.isAuthorizationError(.posix(.EACCES)))
        XCTAssertTrue(NetworkBonjourBrowserSource.isAuthorizationError(.posix(.EPERM)))
        XCTAssertTrue(NetworkBonjourBrowserSource.isAuthorizationError(.dns(-65570)))
        XCTAssertFalse(NetworkBonjourBrowserSource.isAuthorizationError(.posix(.ECONNREFUSED)))
    }
}

final class BridgeBonjourTXTParserTests: XCTestCase {
    func testParsesRawDNSWireRecordAndReturnsCanonicalPrivateIPv4RootURL() throws {
        let values = try BridgeBonjourTXTParser.parse(encodedTXT([
            "url=http://192.168.1.20:5080",
            "apiVersion=v1",
            "bridgeId=\(bonjourBridgeID)",
            "type=elevator-rides"
        ]))

        XCTAssertEqual(values.type, "elevator-rides")
        XCTAssertEqual(values.bridgeId, bonjourBridgeID)
        XCTAssertEqual(values.apiVersion, "v1")
        XCTAssertEqual(values.url.absoluteString, "http://192.168.1.20:5080/")
    }

    func testRejectsEmptyMalformedAndNonUTF8WireRecords() {
        XCTAssertThrowsError(try BridgeBonjourTXTParser.parse(Data())) { error in
            XCTAssertEqual(error as? BridgeBonjourTXTError, .emptyRecord)
        }
        XCTAssertThrowsError(try BridgeBonjourTXTParser.parse(Data([0]))) { error in
            XCTAssertEqual(error as? BridgeBonjourTXTError, .emptyEntry)
        }
        XCTAssertThrowsError(try BridgeBonjourTXTParser.parse(Data([5, 116, 121]))) { error in
            XCTAssertEqual(error as? BridgeBonjourTXTError, .malformedLength)
        }
        XCTAssertThrowsError(try BridgeBonjourTXTParser.parse(Data([2, 0xFF, 0xFE]))) { error in
            XCTAssertEqual(error as? BridgeBonjourTXTError, .nonUTF8)
        }
    }

    func testRejectsNoEqualsEmptyKeyDuplicateAndUnknownKeys() {
        let cases: [(Data, BridgeBonjourTXTError)] = [
            (encodedTXT(["type"]), .missingEquals),
            (encodedTXT(["=value"]), .emptyKey),
            (encodedTXT(["type=elevator-rides", "type=elevator-rides"]), .duplicateKey),
            (encodedTXT(["other=value"]), .unknownKey)
        ]

        for (record, expected) in cases {
            XCTAssertThrowsError(try BridgeBonjourTXTParser.parse(record)) { error in
                XCTAssertEqual(error as? BridgeBonjourTXTError, expected)
            }
        }
    }

    func testRejectsMissingAndWrongExactContractValues() {
        let records: [Data] = [
            encodedTXT(["type=elevator-rides", "bridgeId=\(bonjourBridgeID)", "apiVersion=v1"]),
            validTXT(type: "other"),
            validTXT(bridgeId: "0123456789abcdef0123456789ABCDEF"),
            validTXT(bridgeId: "0123456789ABCDEF0123456789ABCDE"),
            validTXT(apiVersion: "v2"),
            validTXT(url: "http://192.168.1.20:5080/extra")
        ]

        for record in records {
            XCTAssertThrowsError(try BridgeBonjourTXTParser.parse(record))
        }
    }

    func testRejectsUnsafeNonCanonicalURLsAndAcceptsOnlyBackendPrivateRanges() {
        let invalidURLs = [
            "https://192.168.1.20:5080/",
            "http://8.8.8.8:5080/",
            "http://127.0.0.1:5080/",
            "http://192.168.1.20/",
            "http://192.168.1.20:0/",
            "http://192.168.1.20:65536/",
            "http://0192.168.1.20:5080/",
            "http://user:pass@192.168.1.20:5080/",
            "http://192.168.1.20:5080/?token=secret",
            "http://192.168.1.20:5080/#fragment",
            "http://192.168.1.20:5080/path"
        ]
        for url in invalidURLs {
            XCTAssertThrowsError(try BridgeBonjourTXTParser.parse(validTXT(url: url)), url)
        }

        XCTAssertNoThrow(try BridgeBonjourTXTParser.parse(validTXT(url: "http://10.1.2.3:1/")))
        XCTAssertNoThrow(try BridgeBonjourTXTParser.parse(validTXT(url: "http://172.16.0.1:65535/")))
        XCTAssertNoThrow(try BridgeBonjourTXTParser.parse(validTXT(url: "http://169.254.10.4:5080/")))
    }

    func testNonStringMetadataIsRejectedBeforeTXTParsing() {
        let snapshot = BridgeBonjourCandidateParser.parseSnapshot([
            serviceResult(onlyStringEntries: false)
        ])
        XCTAssertEqual(snapshot.candidates, [])
        XCTAssertEqual(snapshot.rejectedResultCount, 1)
    }
}

final class BridgeBonjourCandidateParserTests: XCTestCase {
    func testDeduplicatesAndSortsStableCandidates() {
        let duplicate = serviceResult(name: "z-name")
        let sameCandidateWithEarlierName = serviceResult(name: "a-name")
        let secondURL = serviceResult(
            name: "second-url",
            txt: validTXT(url: "http://192.168.1.21:5080/")
        )
        let secondBridge = serviceResult(
            name: "other-bridge",
            txt: validTXT(bridgeId: secondBonjourBridgeID, url: "http://10.0.0.2:5080/")
        )

        let snapshot = BridgeBonjourCandidateParser.parseSnapshot([
            duplicate, secondURL, secondBridge, sameCandidateWithEarlierName
        ])

        XCTAssertEqual(snapshot.rejectedResultCount, 0)
        XCTAssertEqual(snapshot.candidates.count, 3)
        XCTAssertEqual(snapshot.candidates.map(\.serviceName), ["a-name", "second-url", "other-bridge"])
        XCTAssertEqual(snapshot.candidates.map(\.url.absoluteString), [
            "http://192.168.1.20:5080/",
            "http://192.168.1.21:5080/",
            "http://10.0.0.2:5080/"
        ])
    }

    func testSameBridgeIdentityAtMultipleURLsRemainsTwoDistinctCandidates() {
        let snapshot = BridgeBonjourCandidateParser.parseSnapshot([
            serviceResult(name: "address-a", txt: validTXT(url: "http://192.168.1.20:5080/")),
            serviceResult(name: "address-b", txt: validTXT(url: "http://10.0.0.2:5080/"))
        ])

        XCTAssertEqual(snapshot.candidates.count, 2)
        XCTAssertEqual(Set(snapshot.candidates.map(\.bridgeId)), [bonjourBridgeID])
        XCTAssertEqual(Set(snapshot.candidates.map(\.id)).count, 2)
    }

    func testRejectsWrongServiceEndpointNameTypeDomainAndMalformedTXT() {
        let results = [
            serviceResult(name: "bad name"),
            serviceResult(type: "_other._tcp"),
            serviceResult(domain: "example."),
            BridgeBonjourRawResult(endpoint: .nonService, txtRecord: validTXT()),
            serviceResult(txt: encodedTXT(["type=elevator-rides"]))
        ]

        let snapshot = BridgeBonjourCandidateParser.parseSnapshot(results)
        XCTAssertEqual(snapshot.candidates, [])
        XCTAssertEqual(snapshot.rejectedResultCount, results.count)
    }
}

@MainActor
final class BridgeBonjourDiscoveryModelTests: XCTestCase {
    override func setUp() {
        super.setUp()
        BonjourModelURLProtocol.handler = nil
        BonjourModelURLProtocol.holdRequests = false
        BonjourModelURLProtocol.pendingProtocol = nil
        BonjourModelURLProtocol.pendingRequest = nil
        BonjourModelURLProtocol.requestCount = 0
    }

    override func tearDown() {
        BonjourModelURLProtocol.handler = nil
        BonjourModelURLProtocol.holdRequests = false
        BonjourModelURLProtocol.pendingProtocol = nil
        BonjourModelURLProtocol.pendingRequest = nil
        BonjourModelURLProtocol.requestCount = 0
        super.tearDown()
    }

    func testModelConstructionDoesNotStartNetworkDiscovery() {
        let source = FakeBonjourBrowserSource()
        let model = BridgeConnectionModel(
            credentialStore: InMemoryBridgeCredentialStore(),
            bonjourBrowserSource: source
        )
        XCTAssertEqual(source.startCount, 0)
        withExtendedLifetime(model) {
            XCTAssertEqual(source.stopCount, 0)
        }
    }

    func testOneCandidateIsOnlyAnOfferAndUnpairedSelectionPrefillsWithoutRequest() async {
        let source = FakeBonjourBrowserSource()
        let model = BridgeConnectionModel(
            credentialStore: InMemoryBridgeCredentialStore(),
            session: bonjourSession(),
            defaultBridgeURL: "",
            bonjourBrowserSource: source
        )

        model.startBonjourBrowse()
        source.emitResults([serviceResult()])
        await waitForBonjourCallbacks()

        XCTAssertEqual(model.bonjourDiscoveryState, BridgeBonjourDiscoveryState.offered)
        XCTAssertTrue(model.isBonjourBrowsing)
        XCTAssertEqual(model.bonjourCandidates.count, 1)
        XCTAssertEqual(model.offeredBonjourCandidate, model.bonjourCandidates[0])
        XCTAssertEqual(model.bridgeURLText, "")
        XCTAssertEqual(BonjourModelURLProtocol.requestCount, 0)

        await model.selectBonjourCandidate(model.bonjourCandidates[0])

        XCTAssertEqual(model.bridgeURLText, "http://192.168.1.20:5080/")
        XCTAssertEqual(model.state, BridgeConnectionState.unconfigured)
        XCTAssertEqual(BonjourModelURLProtocol.requestCount, 0)
    }

    func testMultipleCandidatesRequireExplicitSelectionAndDoNotChooseOne() async {
        let source = FakeBonjourBrowserSource()
        let model = BridgeConnectionModel(
            credentialStore: InMemoryBridgeCredentialStore(),
            defaultBridgeURL: "manual-value",
            bonjourBrowserSource: source
        )
        model.startBonjourBrowse()
        source.emitResults([
            serviceResult(name: "one"),
            serviceResult(name: "two", txt: validTXT(url: "http://192.168.1.21:5080/"))
        ])
        await waitForBonjourCallbacks()

        XCTAssertEqual(model.bonjourDiscoveryState, .selectionRequired)
        XCTAssertTrue(model.isBonjourBrowsing)
        XCTAssertNil(model.offeredBonjourCandidate)
        XCTAssertEqual(model.bonjourCandidates.count, 2)
        XCTAssertEqual(model.bridgeURLText, "manual-value")
        XCTAssertEqual(BonjourModelURLProtocol.requestCount, 0)
    }

    func testOfferedBrowseAcceptsChangedThenEmptySnapshots() async {
        let source = FakeBonjourBrowserSource()
        let model = BridgeConnectionModel(
            credentialStore: InMemoryBridgeCredentialStore(),
            bonjourBrowserSource: source
        )
        model.startBonjourBrowse()

        source.emitResults([serviceResult(name: "initial")])
        await waitForBonjourCallbacks()
        XCTAssertEqual(model.bonjourDiscoveryState, .offered)
        XCTAssertTrue(model.isBonjourBrowsing)

        source.emitResults([serviceResult(name: "changed", txt: validTXT(url: "http://192.168.1.21:5080/"))])
        await waitForBonjourCallbacks()
        XCTAssertEqual(model.bonjourDiscoveryState, .offered)
        XCTAssertEqual(model.bonjourCandidates.map(\.serviceName), ["changed"])
        XCTAssertTrue(model.isBonjourBrowsing)

        source.emitResults([])
        await waitForBonjourCallbacks()
        XCTAssertEqual(model.bonjourDiscoveryState, .browsing)
        XCTAssertEqual(model.bonjourCandidates, [])
        XCTAssertNil(model.offeredBonjourCandidate)
        XCTAssertTrue(model.isBonjourBrowsing)
    }

    func testOfferedBrowseAcceptsMultiCandidateUpdateWithoutAutomaticSelection() async {
        let source = FakeBonjourBrowserSource()
        let model = BridgeConnectionModel(
            credentialStore: InMemoryBridgeCredentialStore(),
            bonjourBrowserSource: source
        )
        model.startBonjourBrowse()
        source.emitResults([serviceResult(name: "initial")])
        await waitForBonjourCallbacks()

        source.emitResults([
            serviceResult(name: "one"),
            serviceResult(name: "two", txt: validTXT(url: "http://192.168.1.21:5080/"))
        ])
        await waitForBonjourCallbacks()

        XCTAssertEqual(model.bonjourDiscoveryState, .selectionRequired)
        XCTAssertTrue(model.isBonjourBrowsing)
        XCTAssertNil(model.offeredBonjourCandidate)
        XCTAssertEqual(model.bonjourCandidates.count, 2)
        XCTAssertEqual(BonjourModelURLProtocol.requestCount, 0)
    }

    func testBrowseIsIdempotentCanBeStoppedAndIgnoresStaleCallbacksAcrossRestart() async {
        let source = FakeBonjourBrowserSource()
        let model = BridgeConnectionModel(
            credentialStore: InMemoryBridgeCredentialStore(),
            bonjourBrowserSource: source
        )

        model.startBonjourBrowse()
        model.startBonjourBrowse()
        XCTAssertEqual(source.startCount, 1)
        source.emitResults([serviceResult()])
        await waitForBonjourCallbacks()
        XCTAssertEqual(model.bonjourCandidates.count, 1)

        model.stopBonjourBrowse()
        XCTAssertEqual(model.bonjourDiscoveryState, .stopped)
        XCTAssertFalse(model.isBonjourBrowsing)
        XCTAssertEqual(model.bonjourCandidates, [])
        XCTAssertEqual(source.stopCount, 1)

        model.startBonjourBrowse()
        XCTAssertEqual(source.startCount, 2)
        source.emitResults([
            serviceResult(txt: validTXT(url: "http://192.168.1.99:5080/"))
        ], session: 0)
        await waitForBonjourCallbacks()
        XCTAssertEqual(model.bonjourCandidates, [])

        source.emitResults([serviceResult(name: "fresh")], session: 1)
        await waitForBonjourCallbacks()
        XCTAssertEqual(model.bonjourCandidates.count, 1)
        XCTAssertEqual(model.bonjourCandidates[0].serviceName, "fresh")
    }

    func testDeniedAndFailedSourceStatesAreActionableWithoutSecrets() async {
        let source = FakeBonjourBrowserSource()
        let model = BridgeConnectionModel(
            credentialStore: InMemoryBridgeCredentialStore(),
            bonjourBrowserSource: source
        )
        model.startBonjourBrowse()
        source.emitState(.denied)
        await waitForBonjourCallbacks()
        XCTAssertEqual(model.bonjourDiscoveryState, .denied)
        XCTAssertFalse(model.isBonjourBrowsing)
        XCTAssertEqual(source.stopCount, 1)
        XCTAssertTrue(model.message?.contains("Local Network") == true)
        XCTAssertFalse(model.message?.contains(bonjourBridgeID) == true)

        model.startBonjourBrowse()
        source.emitState(.failed, session: 1)
        await waitForBonjourCallbacks()
        XCTAssertEqual(model.bonjourDiscoveryState, .failed)
        XCTAssertFalse(model.isBonjourBrowsing)
        XCTAssertEqual(source.stopCount, 2)
        XCTAssertTrue(model.message?.contains("manual") == true)
    }

    func testPairedIdentityMismatchMakesNoRequest() async {
        let oldURL = URL(string: "http://192.168.1.20:5080")!
        let credential = BridgeCredential(baseURL: oldURL, accessToken: "bearer", bridgeId: bonjourBridgeID)
        let source = FakeBonjourBrowserSource()
        let model = BridgeConnectionModel(
            credentialStore: InMemoryBridgeCredentialStore(credential: credential),
            session: bonjourSession(),
            bonjourBrowserSource: source
        )
        model.startBonjourBrowse()
        source.emitResults([
            serviceResult(txt: validTXT(bridgeId: secondBonjourBridgeID, url: "http://10.0.0.2:5080/"))
        ])
        await waitForBonjourCallbacks()

        await model.selectBonjourCandidate(model.bonjourCandidates[0])

        XCTAssertEqual(BonjourModelURLProtocol.requestCount, 0)
        XCTAssertEqual(model.state, .failed(BridgeBonjourSelectionError.identityMismatch.localizedDescription))
        XCTAssertTrue(model.message?.contains("No request was sent") == true)
    }

    func testPairedCredentialWithoutIdentityMakesNoRequest() async {
        let oldURL = URL(string: "http://192.168.1.20:5080")!
        let credential = BridgeCredential(baseURL: oldURL, accessToken: "bearer")
        let source = FakeBonjourBrowserSource()
        let model = BridgeConnectionModel(
            credentialStore: InMemoryBridgeCredentialStore(credential: credential),
            session: bonjourSession(),
            bonjourBrowserSource: source
        )
        model.startBonjourBrowse()
        source.emitResults([serviceResult(txt: validTXT())])
        await waitForBonjourCallbacks()

        await model.selectBonjourCandidate(model.bonjourCandidates[0])

        XCTAssertEqual(BonjourModelURLProtocol.requestCount, 0)
        XCTAssertEqual(model.state, .failed(BridgeBonjourSelectionError.identityUnavailable.localizedDescription))
    }

    func testPairedMatchingIdentityMigratesOnceWithOneStatusRequest() async throws {
        let oldURL = URL(string: "http://192.168.1.20:5080")!
        let newURL = URL(string: "http://10.0.0.2:5080/")!
        let token = "matching-bearer"
        let store = InMemoryBridgeCredentialStore(
            credential: BridgeCredential(baseURL: oldURL, accessToken: token, bridgeId: bonjourBridgeID)
        )
        var paths: [String] = []
        BonjourModelURLProtocol.handler = { request in
            paths.append(request.url!.path)
            if request.url!.path == "/api/v1/pair/proof" {
                XCTAssertNil(request.value(forHTTPHeaderField: "Authorization"))
                return (
                    HTTPURLResponse(url: request.url!, statusCode: 200, httpVersion: "HTTP/1.1", headerFields: nil)!,
                    try validProofResponseData(for: request, bearer: token, bridgeId: bonjourBridgeID)
                )
            }
            XCTAssertEqual(request.url, newURL.appendingPathComponent("api/v1/pair/status"))
            XCTAssertEqual(request.value(forHTTPHeaderField: "Authorization"), "Bearer \(token)")
            return (
                HTTPURLResponse(url: request.url!, statusCode: 200, httpVersion: "HTTP/1.1", headerFields: nil)!,
                Data(#"{"version":"v1","paired":true}"#.utf8)
            )
        }

        let source = FakeBonjourBrowserSource()
        let model = BridgeConnectionModel(
            credentialStore: store,
            session: bonjourSession(),
            bonjourBrowserSource: source
        )
        model.startBonjourBrowse()
        source.emitResults([serviceResult(txt: validTXT(url: newURL.absoluteString))])
        await waitForBonjourCallbacks()

        await model.selectBonjourCandidate(model.bonjourCandidates[0])

        XCTAssertEqual(paths, ["/api/v1/pair/proof", "/api/v1/pair/status"])
        XCTAssertEqual(BonjourModelURLProtocol.requestCount, 2)
        XCTAssertEqual(store.credential?.baseURL, URL(string: "http://10.0.0.2:5080")!)
        XCTAssertEqual(model.bridgeURLText, "http://10.0.0.2:5080")
        XCTAssertEqual(model.state, .connected)
    }

    func testBonjourForgedProofSendsNoStatusAndRollsBack() async {
        let oldURL = URL(string: "http://192.168.1.20:5080")!
        let candidateURL = URL(string: "http://10.0.0.2:5080/")!
        let token = "forged-proof-token"
        let store = InMemoryBridgeCredentialStore(credential: BridgeCredential(
            baseURL: oldURL, accessToken: token, bridgeId: bonjourBridgeID
        ))
        var paths: [String] = []
        BonjourModelURLProtocol.handler = { request in
            paths.append(request.url!.path)
            XCTAssertNil(request.value(forHTTPHeaderField: "Authorization"))
            var body = try JSONSerialization.jsonObject(with: try XCTUnwrap(
                try? validProofResponseData(for: request, bearer: token, bridgeId: bonjourBridgeID)
            )) as! [String: Any]
            body["proof"] = String(repeating: "0", count: 64)
            return (
                HTTPURLResponse(url: request.url!, statusCode: 200, httpVersion: "HTTP/1.1", headerFields: nil)!,
                try JSONSerialization.data(withJSONObject: body)
            )
        }
        let source = FakeBonjourBrowserSource()
        let model = BridgeConnectionModel(
            credentialStore: store,
            session: bonjourSession(),
            bonjourBrowserSource: source,
            relocationNonceGenerator: {
                "000102030405060708090A0B0C0D0E0F101112131415161718191A1B1C1D1E1F"
            }
        )
        model.startBonjourBrowse()
        source.emitResults([serviceResult(txt: validTXT(url: candidateURL.absoluteString))])
        await waitForBonjourCallbacks()

        await model.selectBonjourCandidate(model.bonjourCandidates[0])

        XCTAssertEqual(paths, ["/api/v1/pair/proof"])
        XCTAssertEqual(store.credential?.baseURL, oldURL)
        XCTAssertEqual(model.bridgeURLText, oldURL.absoluteString)
        XCTAssertTrue(model.isPaired)
    }

    func testMatchingIdentityMigrationRestoresPriorTextOnFailure() async {
        let oldURL = URL(string: "http://192.168.1.20:5080")!
        let candidateURL = URL(string: "http://10.0.0.2:5080/")!
        let store = InMemoryBridgeCredentialStore(
            credential: BridgeCredential(baseURL: oldURL, accessToken: "bearer", bridgeId: bonjourBridgeID)
        )
        BonjourModelURLProtocol.handler = { _ in throw URLError(.cannotConnectToHost) }
        let source = FakeBonjourBrowserSource()
        let model = BridgeConnectionModel(
            credentialStore: store,
            session: bonjourSession(),
            bonjourBrowserSource: source
        )
        model.bridgeURLText = "operator text before selection"
        model.startBonjourBrowse()
        source.emitResults([serviceResult(txt: validTXT(url: candidateURL.absoluteString))])
        await waitForBonjourCallbacks()

        await model.selectBonjourCandidate(model.bonjourCandidates[0])

        XCTAssertEqual(BonjourModelURLProtocol.requestCount, 1)
        XCTAssertEqual(model.bridgeURLText, "operator text before selection")
        XCTAssertEqual(store.credential?.baseURL, oldURL)
        XCTAssertTrue(model.isPaired)
    }

    func testAutomaticLaunchBrowsesOnlyForPersistedCredentialWithValidIdentity() async {
        let source = FakeBonjourBrowserSource()
        let unpaired = BridgeConnectionModel(
            credentialStore: InMemoryBridgeCredentialStore(),
            bonjourBrowserSource: source,
            bonjourQuiescenceDelay: {}
        )
        await unpaired.startAutomaticBonjourReconnect()
        XCTAssertEqual(source.startCount, 0)

        let legacy = BridgeConnectionModel(
            credentialStore: InMemoryBridgeCredentialStore(credential: BridgeCredential(
                baseURL: URL(string: "http://192.168.1.20:5080")!, accessToken: "bearer"
            )),
            bonjourBrowserSource: source,
            bonjourQuiescenceDelay: {}
        )
        await legacy.startAutomaticBonjourReconnect()
        XCTAssertEqual(source.startCount, 0)

        let eligible = BridgeConnectionModel(
            credentialStore: InMemoryBridgeCredentialStore(credential: BridgeCredential(
                baseURL: URL(string: "http://192.168.1.20:5080")!, accessToken: "bearer", bridgeId: bonjourBridgeID
            )),
            bonjourBrowserSource: source,
            bonjourQuiescenceDelay: {}
        )
        await eligible.startAutomaticBonjourReconnect()
        XCTAssertEqual(source.startCount, 1)
        eligible.stopBonjourBrowse()
        await eligible.startAutomaticBonjourReconnect()
        XCTAssertEqual(source.startCount, 1)
    }

    func testAutomaticBonjourStartupSearchIsNotPresentedAsConnected() async {
        let source = FakeBonjourBrowserSource()
        let model = BridgeConnectionModel(
            credentialStore: InMemoryBridgeCredentialStore(credential: BridgeCredential(
                baseURL: URL(string: "http://192.168.1.20:5080")!,
                accessToken: "saved-bearer",
                bridgeId: bonjourBridgeID
            )),
            bonjourBrowserSource: source,
            bonjourQuiescenceDelay: {}
        )

        XCTAssertEqual(model.state, .restored)
        await model.startAutomaticBonjourReconnect()
        XCTAssertEqual(model.state, .searching)
        XCTAssertEqual(model.state.title, "Searching for saved bridge…")
        XCTAssertTrue(model.isPaired)
        XCTAssertTrue(model.isBonjourBrowsing)

        source.emitResults([])
        await waitForBonjourCallbacks()
        XCTAssertEqual(model.state, .searching)
        XCTAssertEqual(BonjourModelURLProtocol.requestCount, 0)

        source.emitState(.denied)
        await waitForBonjourCallbacks()
        XCTAssertEqual(model.state, .restored)
        XCTAssertTrue(model.isPaired)
    }

    func testAutomaticReconnectRequiresStableExactSingleMatchAndSendsOneStatusRequest() async {
        let oldURL = URL(string: "http://192.168.1.20:5080")!
        let candidateURL = URL(string: "http://10.0.0.2:5080/")!
        let store = InMemoryBridgeCredentialStore(
            credential: BridgeCredential(baseURL: oldURL, accessToken: "saved-bearer", bridgeId: bonjourBridgeID)
        )
        var paths: [String] = []
        BonjourModelURLProtocol.handler = { request in
            paths.append(request.url!.path)
            if request.url!.path == "/api/v1/pair/proof" {
                XCTAssertNil(request.value(forHTTPHeaderField: "Authorization"))
                return (
                    HTTPURLResponse(url: request.url!, statusCode: 200, httpVersion: "HTTP/1.1", headerFields: nil)!,
                    try validProofResponseData(for: request, bearer: "saved-bearer", bridgeId: bonjourBridgeID)
                )
            }
            XCTAssertEqual(request.value(forHTTPHeaderField: "Authorization"), "Bearer saved-bearer")
            return (
                HTTPURLResponse(url: request.url!, statusCode: 200, httpVersion: "HTTP/1.1", headerFields: nil)!,
                Data(#"{"version":"v1","paired":true}"#.utf8)
            )
        }
        let source = FakeBonjourBrowserSource()
        let model = BridgeConnectionModel(
            credentialStore: store,
            session: bonjourSession(),
            bonjourBrowserSource: source,
            bonjourQuiescenceDelay: {}
        )
        await model.startAutomaticBonjourReconnect()
        source.emitResults([serviceResult(txt: validTXT(url: candidateURL.absoluteString))])
        await waitForAutomaticReconnect()
        source.emitResults([serviceResult(txt: validTXT(url: candidateURL.absoluteString))])
        await waitForAutomaticReconnect()

        XCTAssertEqual(paths, ["/api/v1/pair/proof", "/api/v1/pair/status"])
        XCTAssertEqual(BonjourModelURLProtocol.requestCount, 2)
        XCTAssertEqual(store.credential?.baseURL, URL(string: "http://10.0.0.2:5080")!)
        XCTAssertEqual(model.state, .connected)
    }

    func testAutomaticReconnectAdvertisementRemovalSearchesWithoutBearerRetryAndRestoresOnReadvertisement() async throws {
        let oldURL = URL(string: "http://192.168.1.20:5080")!
        let candidateURL = URL(string: "http://10.0.0.2:5080/")!
        let store = InMemoryBridgeCredentialStore(credential: BridgeCredential(
            baseURL: oldURL, accessToken: "saved-bearer", bridgeId: bonjourBridgeID
        ))
        BonjourModelURLProtocol.handler = { request in
            if request.url?.path == "/api/v1/pair/proof" {
                return (
                    HTTPURLResponse(url: request.url!, statusCode: 200, httpVersion: "HTTP/1.1", headerFields: nil)!,
                    try validProofResponseData(for: request, bearer: "saved-bearer", bridgeId: bonjourBridgeID)
                )
            }
            return (
                HTTPURLResponse(url: request.url!, statusCode: 200, httpVersion: "HTTP/1.1", headerFields: nil)!,
                Data(#"{"version":"v1","paired":true}"#.utf8)
            )
        }
        let source = FakeBonjourBrowserSource()
        let model = BridgeConnectionModel(
            credentialStore: store,
            session: bonjourSession(),
            bonjourBrowserSource: source,
            bonjourQuiescenceDelay: {}
        )
        await model.startAutomaticBonjourReconnect()
        source.emitResults([serviceResult(txt: validTXT(url: candidateURL.absoluteString))])
        await waitForAutomaticReconnect()
        XCTAssertEqual(model.state, .connected)
        XCTAssertEqual(BonjourModelURLProtocol.requestCount, 2)

        source.emitResults([])
        await waitForBonjourCallbacks()
        XCTAssertEqual(model.state, .searching)
        XCTAssertTrue(model.isPaired)
        XCTAssertEqual(BonjourModelURLProtocol.requestCount, 2)

        source.emitResults([serviceResult(txt: validTXT(url: candidateURL.absoluteString))])
        await waitForAutomaticReconnect()
        XCTAssertEqual(model.state, .connected)
        XCTAssertEqual(BonjourModelURLProtocol.requestCount, 4)
        XCTAssertEqual(store.credential?.baseURL, URL(string: "http://10.0.0.2:5080")!)
    }

    func testManualBrowseResultChurnDoesNotDisconnectVerifiedConnection() async {
        let oldURL = URL(string: "http://127.0.0.1:8080")!
        let newURL = URL(string: "http://192.168.1.20:8080")!
        let store = InMemoryBridgeCredentialStore(credential: BridgeCredential(
            baseURL: oldURL, accessToken: "saved-bearer"
        ))
        BonjourModelURLProtocol.handler = { request in
            XCTAssertEqual(request.url, newURL.appendingPathComponent("api/v1/pair/status"))
            return (
                HTTPURLResponse(url: request.url!, statusCode: 200, httpVersion: "HTTP/1.1", headerFields: nil)!,
                Data(#"{"version":"v1","paired":true}"#.utf8)
            )
        }
        let source = FakeBonjourBrowserSource()
        let model = BridgeConnectionModel(
            credentialStore: store,
            session: bonjourSession(),
            bonjourBrowserSource: source
        )
        model.bridgeURLText = newURL.absoluteString
        await model.useEnteredBridgeAddress()
        XCTAssertEqual(model.state, .connected)

        model.startBonjourBrowse()
        source.emitResults([serviceResult(txt: validTXT(bridgeId: secondBonjourBridgeID))])
        await waitForBonjourCallbacks()
        source.emitResults([])
        await waitForBonjourCallbacks()

        XCTAssertEqual(model.state, .connected)
        XCTAssertTrue(model.isPaired)
    }

    func testAutomaticReconnectDoesNotRequestForMismatchMultipleIdentityOrSameIdentityMultipleURL() async {
        let credential = BridgeCredential(
            baseURL: URL(string: "http://192.168.1.20:5080")!, accessToken: "bearer", bridgeId: bonjourBridgeID
        )
        let source = FakeBonjourBrowserSource()
        let model = BridgeConnectionModel(
            credentialStore: InMemoryBridgeCredentialStore(credential: credential),
            session: bonjourSession(),
            bonjourBrowserSource: source,
            bonjourQuiescenceDelay: {}
        )
        await model.startAutomaticBonjourReconnect()

        source.emitResults([serviceResult(txt: validTXT(bridgeId: secondBonjourBridgeID))])
        await waitForAutomaticReconnect()
        XCTAssertEqual(BonjourModelURLProtocol.requestCount, 0)

        source.emitResults([
            serviceResult(name: "one"),
            serviceResult(name: "two", txt: validTXT(bridgeId: secondBonjourBridgeID, url: "http://10.0.0.2:5080/"))
        ])
        await waitForAutomaticReconnect()
        XCTAssertEqual(BonjourModelURLProtocol.requestCount, 0)

        source.emitResults([
            serviceResult(name: "address-a", txt: validTXT(url: "http://192.168.1.20:5080/")),
            serviceResult(name: "address-b", txt: validTXT(url: "http://10.0.0.2:5080/"))
        ])
        await waitForAutomaticReconnect()
        XCTAssertEqual(BonjourModelURLProtocol.requestCount, 0)
        XCTAssertTrue(model.message?.contains("Multiple") == true)
    }

    func testAutomaticReconnectCancelsOnCandidateRemovalAndStopBeforeDelay() async {
        let source = FakeBonjourBrowserSource()
        let delay = ControlledAsyncGate()
        let model = BridgeConnectionModel(
            credentialStore: InMemoryBridgeCredentialStore(credential: BridgeCredential(
                baseURL: URL(string: "http://192.168.1.20:5080")!, accessToken: "bearer", bridgeId: bonjourBridgeID
            )),
            session: bonjourSession(),
            bonjourBrowserSource: source,
            bonjourQuiescenceDelay: { try await delay.wait() }
        )
        await model.startAutomaticBonjourReconnect()
        source.emitResults([serviceResult()])
        await waitUntil { delay.waitCount == 1 }
        source.emitResults([])
        model.stopBonjourBrowse()
        delay.resumeNext()
        await waitForBonjourCallbacks()

        XCTAssertEqual(BonjourModelURLProtocol.requestCount, 0)
        XCTAssertEqual(source.stopCount, 1)
    }

    func testAutomaticReconnectFailurePreservesOldCredentialAndAddressAndManualSelectionRemainsAvailable() async {
        let oldURL = URL(string: "http://192.168.1.20:5080")!
        let candidateURL = URL(string: "http://10.0.0.2:5080/")!
        let store = InMemoryBridgeCredentialStore(
            credential: BridgeCredential(baseURL: oldURL, accessToken: "bearer", bridgeId: bonjourBridgeID)
        )
        BonjourModelURLProtocol.handler = { _ in throw URLError(.cannotConnectToHost) }
        let source = FakeBonjourBrowserSource()
        let model = BridgeConnectionModel(
            credentialStore: store,
            session: bonjourSession(),
            bonjourBrowserSource: source,
            bonjourQuiescenceDelay: {}
        )
        await model.startAutomaticBonjourReconnect()
        source.emitResults([serviceResult(txt: validTXT(url: candidateURL.absoluteString))])
        await waitForAutomaticReconnect()

        XCTAssertEqual(BonjourModelURLProtocol.requestCount, 1)
        XCTAssertEqual(store.credential?.baseURL, oldURL)
        XCTAssertEqual(model.bridgeURLText, oldURL.absoluteString)
        XCTAssertTrue(model.message?.contains("manually") == true)
        source.emitResults([serviceResult(txt: validTXT(url: candidateURL.absoluteString))])
        await waitForAutomaticReconnect()
        XCTAssertEqual(BonjourModelURLProtocol.requestCount, 1)
        await model.selectBonjourCandidate(model.bonjourCandidates[0])
        XCTAssertEqual(BonjourModelURLProtocol.requestCount, 2)
    }

    func testAutomaticReconnectStorageErrorDoesNotBrowseOrRequest() async {
        let source = FakeBonjourBrowserSource()
        let model = BridgeConnectionModel(
            credentialStore: AutomaticReconnectLoadFailureStore(),
            bonjourBrowserSource: source,
            bonjourQuiescenceDelay: {}
        )
        await model.startAutomaticBonjourReconnect()
        XCTAssertEqual(source.startCount, 0)
        XCTAssertEqual(BonjourModelURLProtocol.requestCount, 0)
        XCTAssertTrue(model.message?.contains("saved pairing") == true)
    }

    func testAutomaticReconnectLaunchOverrideCompletesBeforeBonjourAndDoesNotMigrateTwice() async {
        let oldURL = URL(string: "http://192.168.1.20:5080")!
        let overrideURL = URL(string: "http://10.0.0.2:5080/")!
        let store = InMemoryBridgeCredentialStore(
            credential: BridgeCredential(baseURL: oldURL, accessToken: "bearer", bridgeId: bonjourBridgeID)
        )
        var paths: [String] = []
        BonjourModelURLProtocol.handler = { request in
            paths.append(request.url!.path)
            XCTAssertEqual(request.url, overrideURL.appendingPathComponent("api/v1/pair/status"))
            return (
                HTTPURLResponse(url: request.url!, statusCode: 200, httpVersion: "HTTP/1.1", headerFields: nil)!,
                Data(#"{"version":"v1","paired":true}"#.utf8)
            )
        }
        let source = FakeBonjourBrowserSource()
        let model = BridgeConnectionModel(
            credentialStore: store,
            session: bonjourSession(),
            bonjourBrowserSource: source,
            bonjourQuiescenceDelay: {}
        )
        await model.applyLaunchAddressOverride(overrideURL.absoluteString)
        await model.startAutomaticBonjourReconnect()
        XCTAssertEqual(paths, ["/api/v1/pair/status"])
        XCTAssertEqual(source.startCount, 0)
        XCTAssertEqual(store.credential?.baseURL, URL(string: "http://10.0.0.2:5080")!)
    }

    func testAutomaticReconnectAtoBChurnDuringDelayUsesOnlyNewestStableSnapshot() async {
        let oldURL = URL(string: "http://192.168.1.20:5080")!
        let candidateA = URL(string: "http://10.0.0.2:5080/")!
        let candidateB = URL(string: "http://10.0.0.3:5080/")!
        let store = InMemoryBridgeCredentialStore(credential: BridgeCredential(
            baseURL: oldURL, accessToken: "bearer", bridgeId: bonjourBridgeID
        ))
        let delay = ControlledAsyncGate()
        let source = FakeBonjourBrowserSource()
        let model = BridgeConnectionModel(
            credentialStore: store,
            session: bonjourSession(),
            bonjourBrowserSource: source,
            bonjourQuiescenceDelay: { try await delay.wait() }
        )
        BonjourModelURLProtocol.holdRequests = true
        await model.startAutomaticBonjourReconnect()

        source.emitResults([serviceResult(txt: validTXT(url: candidateA.absoluteString))])
        await waitUntil { delay.waitCount == 1 }
        source.emitResults([serviceResult(txt: validTXT(url: candidateB.absoluteString))])
        await waitUntil { model.bonjourCandidates.map(\.url) == [candidateB] }

        delay.resumeNext()
        await waitUntil { delay.waitCount == 2 }
        delay.resumeNext()
        await waitUntil { BonjourModelURLProtocol.requestCount == 1 && BonjourModelURLProtocol.pendingRequest != nil }
        let proofRequest = BonjourModelURLProtocol.pendingRequest!
        XCTAssertEqual(proofRequest.url, candidateB.appendingPathComponent("api/v1/pair/proof"))
        BonjourModelURLProtocol.respondPending(data: try! validProofResponseData(
            for: proofRequest, bearer: "bearer", bridgeId: bonjourBridgeID
        ))
        await waitUntil { BonjourModelURLProtocol.requestCount == 2 }
        XCTAssertEqual(BonjourModelURLProtocol.pendingRequest?.url, candidateB.appendingPathComponent("api/v1/pair/status"))
        BonjourModelURLProtocol.respondPending()
        await waitUntil { store.credential?.baseURL == URL(string: "http://10.0.0.3:5080")! }

        XCTAssertEqual(BonjourModelURLProtocol.requestCount, 2)
        XCTAssertEqual(model.bridgeURLText, "http://10.0.0.3:5080")
        XCTAssertEqual(model.state, .connected)
    }

    func testAutomaticReconnectAtoBChurnDuringInFlightRequestRejectsOldCompletionAndThenRunsBOnce() async {
        let oldURL = URL(string: "http://192.168.1.20:5080")!
        let candidateA = URL(string: "http://10.0.0.2:5080/")!
        let candidateB = URL(string: "http://10.0.0.3:5080/")!
        let store = InMemoryBridgeCredentialStore(credential: BridgeCredential(
            baseURL: oldURL, accessToken: "bearer", bridgeId: bonjourBridgeID
        ))
        let source = FakeBonjourBrowserSource()
        let model = BridgeConnectionModel(
            credentialStore: store,
            session: bonjourSession(),
            bonjourBrowserSource: source,
            bonjourQuiescenceDelay: {}
        )
        BonjourModelURLProtocol.holdRequests = true
        await model.startAutomaticBonjourReconnect()
        source.emitResults([serviceResult(txt: validTXT(url: candidateA.absoluteString))])
        await waitUntil { BonjourModelURLProtocol.requestCount == 1 && BonjourModelURLProtocol.pendingRequest != nil }
        XCTAssertEqual(model.state, .relocating)

        source.emitResults([serviceResult(txt: validTXT(url: candidateB.absoluteString))])
        await waitUntil {
            model.bonjourCandidates.map(\.url) == [candidateB] && model.state == .searching
        }
        XCTAssertEqual(store.credential?.baseURL, oldURL)

        let firstProofRequest = BonjourModelURLProtocol.pendingRequest!
        BonjourModelURLProtocol.respondPending(data: try! validProofResponseData(
            for: firstProofRequest, bearer: "bearer", bridgeId: bonjourBridgeID
        ))
        await waitUntil { BonjourModelURLProtocol.requestCount == 2 && BonjourModelURLProtocol.pendingRequest != nil }
        let secondProofRequest = BonjourModelURLProtocol.pendingRequest!
        XCTAssertEqual(secondProofRequest.url, candidateB.appendingPathComponent("api/v1/pair/proof"))
        XCTAssertEqual(store.credential?.baseURL, oldURL)
        BonjourModelURLProtocol.respondPending(data: try! validProofResponseData(
            for: secondProofRequest, bearer: "bearer", bridgeId: bonjourBridgeID
        ))
        await waitUntil { BonjourModelURLProtocol.requestCount == 3 && BonjourModelURLProtocol.pendingRequest != nil }
        XCTAssertEqual(BonjourModelURLProtocol.pendingRequest?.url, candidateB.appendingPathComponent("api/v1/pair/status"))
        XCTAssertEqual(store.credential?.baseURL, oldURL)
        BonjourModelURLProtocol.respondPending()
        await waitUntil { store.credential?.baseURL == URL(string: "http://10.0.0.3:5080")! }

        XCTAssertEqual(BonjourModelURLProtocol.requestCount, 3)
        XCTAssertEqual(model.bridgeURLText, "http://10.0.0.3:5080")
        XCTAssertEqual(model.state, .connected)
    }

    func testAutomaticReconnectStopDuringInFlightRequestRemainsTerminalAfterLateCompletion() async {
        let oldURL = URL(string: "http://192.168.1.20:5080")!
        let candidateURL = URL(string: "http://10.0.0.2:5080/")!
        let store = InMemoryBridgeCredentialStore(credential: BridgeCredential(
            baseURL: oldURL, accessToken: "bearer", bridgeId: bonjourBridgeID
        ))
        let source = FakeBonjourBrowserSource()
        let model = BridgeConnectionModel(
            credentialStore: store,
            session: bonjourSession(),
            bonjourBrowserSource: source,
            bonjourQuiescenceDelay: {}
        )
        BonjourModelURLProtocol.holdRequests = true
        await model.startAutomaticBonjourReconnect()
        source.emitResults([serviceResult(txt: validTXT(url: candidateURL.absoluteString))])
        await waitUntil { BonjourModelURLProtocol.requestCount == 1 && BonjourModelURLProtocol.pendingRequest != nil }

        model.stopBonjourBrowse()
        XCTAssertEqual(model.bonjourDiscoveryState, .stopped)
        XCTAssertEqual(model.state, .restored)
        BonjourModelURLProtocol.respondPending()
        await waitForAutomaticReconnect()

        XCTAssertEqual(BonjourModelURLProtocol.requestCount, 1)
        XCTAssertEqual(store.credential?.baseURL, oldURL)
        XCTAssertEqual(model.bridgeURLText, oldURL.absoluteString)
        XCTAssertEqual(model.bonjourDiscoveryState, .stopped)
        XCTAssertEqual(model.state, .restored)
    }

    func testForgetCancelsInFlightBonjourProofLocallyAndRejectsLateCompletion() async {
        let oldURL = URL(string: "http://192.168.1.20:5080")!
        let candidateURL = URL(string: "http://10.0.0.2:5080/")!
        let store = InMemoryBridgeCredentialStore(credential: BridgeCredential(
            baseURL: oldURL, accessToken: "bearer", bridgeId: bonjourBridgeID
        ))
        let source = FakeBonjourBrowserSource()
        let model = BridgeConnectionModel(
            credentialStore: store,
            session: bonjourSession(),
            bonjourBrowserSource: source,
            bonjourQuiescenceDelay: {}
        )
        BonjourModelURLProtocol.holdRequests = true
        await model.startAutomaticBonjourReconnect()
        source.emitResults([serviceResult(txt: validTXT(url: candidateURL.absoluteString))])
        await waitUntil { BonjourModelURLProtocol.requestCount == 1 && BonjourModelURLProtocol.pendingRequest != nil }
        XCTAssertEqual(model.state, .relocating)
        XCTAssertTrue(model.hasPairingToForget)

        await model.forget()

        XCTAssertEqual(model.state, .unconfigured)
        XCTAssertFalse(model.isPaired)
        XCTAssertNil(store.credential)
        XCTAssertEqual(model.bonjourCandidates, [])
        XCTAssertNil(model.offeredBonjourCandidate)
        XCTAssertEqual(model.bonjourDiscoveryState, .stopped)
        XCTAssertGreaterThanOrEqual(source.stopCount, 1)
        XCTAssertEqual(BonjourModelURLProtocol.requestCount, 1)

        // A completion already queued by the old proof must not send status or restore state.
        BonjourModelURLProtocol.respondPending(data: try! validProofResponseData(
            for: BonjourModelURLProtocol.pendingRequest!, bearer: "bearer", bridgeId: bonjourBridgeID
        ))
        await waitForAutomaticReconnect()
        XCTAssertEqual(BonjourModelURLProtocol.requestCount, 1)
        XCTAssertEqual(model.state, .unconfigured)
        XCTAssertFalse(model.isPaired)
    }

    func testAutomaticReconnectModelDeinitCancelsPendingDelayWithoutRetainingModel() async {
        let delay = ControlledAsyncGate()
        let source = FakeBonjourBrowserSource()
        weak var weakModel: BridgeConnectionModel?
        var model: BridgeConnectionModel? = BridgeConnectionModel(
            credentialStore: InMemoryBridgeCredentialStore(credential: BridgeCredential(
                baseURL: URL(string: "http://192.168.1.20:5080")!,
                accessToken: "bearer",
                bridgeId: bonjourBridgeID
            )),
            bonjourBrowserSource: source,
            bonjourQuiescenceDelay: { try await delay.wait() }
        )
        weakModel = model
        await model?.startAutomaticBonjourReconnect()
        source.emitResults([serviceResult()])
        await waitUntil { delay.waitCount == 1 }

        model = nil
        await waitUntil { weakModel == nil }
        delay.resumeNext()
        await waitForAutomaticReconnect()
        XCTAssertNil(weakModel)
    }

    func testAutomaticReconnectTerminalDiscoveryStateWinsOverLateRequestCompletion() async {
        let oldURL = URL(string: "http://192.168.1.20:5080")!
        let candidateURL = URL(string: "http://10.0.0.2:5080/")!
        let store = InMemoryBridgeCredentialStore(credential: BridgeCredential(
            baseURL: oldURL, accessToken: "bearer", bridgeId: bonjourBridgeID
        ))
        let source = FakeBonjourBrowserSource()
        let model = BridgeConnectionModel(
            credentialStore: store,
            session: bonjourSession(),
            bonjourBrowserSource: source,
            bonjourQuiescenceDelay: {}
        )
        BonjourModelURLProtocol.holdRequests = true
        await model.startAutomaticBonjourReconnect()
        source.emitResults([serviceResult(txt: validTXT(url: candidateURL.absoluteString))])
        await waitUntil { BonjourModelURLProtocol.requestCount == 1 && BonjourModelURLProtocol.pendingRequest != nil }

        source.emitState(.denied)
        await waitUntil { model.bonjourDiscoveryState == .denied }
        BonjourModelURLProtocol.respondPending()
        await waitForAutomaticReconnect()

        XCTAssertEqual(BonjourModelURLProtocol.requestCount, 1)
        XCTAssertEqual(store.credential?.baseURL, oldURL)
        XCTAssertEqual(model.bridgeURLText, oldURL.absoluteString)
        XCTAssertEqual(model.bonjourDiscoveryState, .denied)
        XCTAssertEqual(model.state, .restored)
    }
}
