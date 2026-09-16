import UIKit
import XCTest
@testable import RidesTablet

@MainActor
private final class FakePairingScannerCoordinator: BridgePairingScannerCoordinator {
    var availability: BridgePairingScannerAvailability
    var authorization: BridgePairingCameraAuthorization
    var requestAccessResult: BridgePairingCameraAuthorization
    private(set) var requestAccessCount = 0
    private(set) var startCount = 0
    private(set) var stopCount = 0
    private(set) var events: [String] = []
    var startError: Error?
    var holdAuthorizationRequest = false
    private var authorizationContinuation: CheckedContinuation<BridgePairingCameraAuthorization, Never>?
    private var payloadHandler: ((String) -> Void)?
    private var errorHandler: ((BridgePairingScannerError) -> Void)?

    init(
        availability: BridgePairingScannerAvailability = .supported,
        authorization: BridgePairingCameraAuthorization = .authorized,
        requestAccessResult: BridgePairingCameraAuthorization = .authorized
    ) {
        self.availability = availability
        self.authorization = authorization
        self.requestAccessResult = requestAccessResult
    }

    func requestCameraAccess() async -> BridgePairingCameraAuthorization {
        requestAccessCount += 1
        if holdAuthorizationRequest {
            return await withCheckedContinuation { continuation in
                authorizationContinuation = continuation
            }
        }
        authorization = requestAccessResult
        return authorization
    }

    func resolveAuthorizationRequest() {
        authorization = requestAccessResult
        authorizationContinuation?.resume(returning: authorization)
        authorizationContinuation = nil
    }

    func makeViewController(
        onPayload: @escaping (String) -> Void,
        onError: @escaping (BridgePairingScannerError) -> Void
    ) -> UIViewController {
        payloadHandler = onPayload
        errorHandler = onError
        return UIViewController()
    }

    func startScanning() async throws {
        startCount += 1
        events.append("start")
        if let startError { throw startError }
    }

    func stopScanning() {
        stopCount += 1
        events.append("stop")
    }

    func record(_ event: String) {
        events.append(event)
    }

    func emit(payload: String) {
        payloadHandler?(payload)
    }

    func emit(error: BridgePairingScannerError) {
        errorHandler?(error)
    }
}

@MainActor
final class BridgePairingQRScannerTests: XCTestCase {
    func testUnsupportedHardwareIsActionableWithoutCreatingCameraController() async {
        let coordinator = FakePairingScannerCoordinator(availability: .unsupportedHardware)
        let model = BridgePairingScannerModel(coordinator: coordinator)
        var payloads: [String] = []

        await model.begin { payloads.append($0) }

        XCTAssertEqual(model.state, .unavailable(.unsupportedHardware))
        XCTAssertTrue(model.message?.contains("manually") == true)
        XCTAssertEqual(coordinator.requestAccessCount, 0)
        XCTAssertEqual(coordinator.startCount, 0)
        XCTAssertEqual(payloads, [])
    }

    func testCameraUnavailableIsActionableAfterAuthorization() async {
        let coordinator = FakePairingScannerCoordinator(availability: .cameraUnavailable)
        let model = BridgePairingScannerModel(coordinator: coordinator)
        await model.begin { _ in }

        XCTAssertEqual(model.state, .unavailable(.cameraUnavailable))
        XCTAssertTrue(model.message?.contains("Close other camera apps") == true)
        XCTAssertEqual(coordinator.startCount, 0)
    }

    func testPermissionStatesAreActionableAndNotRetried() async {
        let denied = FakePairingScannerCoordinator(authorization: .denied)
        let deniedModel = BridgePairingScannerModel(coordinator: denied)
        await deniedModel.begin { _ in }
        XCTAssertEqual(deniedModel.state, .unavailable(.permissionDenied))
        XCTAssertEqual(denied.requestAccessCount, 0)

        let restricted = FakePairingScannerCoordinator(authorization: .restricted)
        let restrictedModel = BridgePairingScannerModel(coordinator: restricted)
        await restrictedModel.begin { _ in }
        XCTAssertEqual(restrictedModel.state, .unavailable(.permissionRestricted))
        XCTAssertEqual(restricted.requestAccessCount, 0)
    }

    func testDismissalDuringPermissionPromptCannotRestartScanner() async {
        let coordinator = FakePairingScannerCoordinator(
            authorization: .notDetermined,
            requestAccessResult: .authorized
        )
        coordinator.holdAuthorizationRequest = true
        let model = BridgePairingScannerModel(coordinator: coordinator)
        let beginTask = Task { @MainActor in
            await model.begin { _ in XCTFail("dismissed scanner delivered a payload") }
        }
        while coordinator.requestAccessCount == 0 {
            await Task.yield()
        }

        model.cancel()
        coordinator.resolveAuthorizationRequest()
        await beginTask.value

        XCTAssertEqual(model.state, .cancelled)
        XCTAssertEqual(coordinator.startCount, 0)
        XCTAssertEqual(coordinator.stopCount, 0)
    }

    func testNotDeterminedRequestsAuthorizationOnceAndStartsOnlyAfterGrant() async {
        let coordinator = FakePairingScannerCoordinator(
            authorization: .notDetermined,
            requestAccessResult: .authorized
        )
        let model = BridgePairingScannerModel(coordinator: coordinator)
        await model.begin { _ in }

        XCTAssertEqual(model.state, .scanning)
        XCTAssertEqual(coordinator.requestAccessCount, 1)
        _ = model.makeViewController()
        await model.startScanning()
        await model.startScanning()
        XCTAssertEqual(coordinator.startCount, 1)
    }

    func testOneShotPayloadStopsBeforeCallbackAndIgnoresReplay() async {
        let coordinator = FakePairingScannerCoordinator()
        let model = BridgePairingScannerModel(coordinator: coordinator)
        var events: [String] = []
        await model.begin { payload in
            coordinator.record("payload")
            events.append("payload:\(payload)")
        }
        _ = model.makeViewController()
        await model.startScanning()

        coordinator.emit(payload: "first-payload")
        coordinator.emit(payload: "second-payload")

        XCTAssertEqual(events, ["payload:first-payload"])
        model.cancel()
        XCTAssertEqual(coordinator.events, ["start", "stop", "payload"])
        XCTAssertEqual(model.state, .captured)
        XCTAssertEqual(coordinator.stopCount, 1)
    }

    func testDismissalAndRepresentableTeardownCancellationIsIdempotent() async {
        let coordinator = FakePairingScannerCoordinator()
        let model = BridgePairingScannerModel(coordinator: coordinator)
        await model.begin { _ in XCTFail("dismissed scanner delivered a payload") }
        _ = model.makeViewController()
        await model.startScanning()

        // Sheet onDisappear and UIViewControllerRepresentable dismantle can both
        // arrive for one dismissal. They must produce one stop and one terminal state.
        model.cancel()
        model.cancel()
        coordinator.emit(payload: "late-payload")

        XCTAssertEqual(model.state, .cancelled)
        XCTAssertEqual(coordinator.events, ["start", "stop"])
        XCTAssertEqual(coordinator.stopCount, 1)
    }

    func testStartFailureStopsScanningAndIsActionable() async {
        struct StartFailure: Error {}
        let coordinator = FakePairingScannerCoordinator()
        coordinator.startError = StartFailure()
        let model = BridgePairingScannerModel(coordinator: coordinator)
        await model.begin { _ in }
        _ = model.makeViewController()
        await model.startScanning()

        if case .failed(.runtime) = model.state {
            // expected
        } else {
            XCTFail("start failure was not surfaced")
        }
        XCTAssertEqual(coordinator.stopCount, 1)
    }

    func testRuntimeErrorStopsScanningAndIsActionable() async {
        let coordinator = FakePairingScannerCoordinator()
        let model = BridgePairingScannerModel(coordinator: coordinator)
        await model.begin { _ in }
        _ = model.makeViewController()
        coordinator.emit(error: .runtime("camera interrupted"))

        XCTAssertEqual(model.state, .failed(.runtime("camera interrupted")))
        model.cancel()
        XCTAssertEqual(model.state, .failed(.runtime("camera interrupted")))
        XCTAssertEqual(coordinator.stopCount, 1)
        XCTAssertTrue(model.message?.contains("Try scanning again") == true)
    }

    func testCancelStopsScanningAndNeverEmitsPayload() async {
        let coordinator = FakePairingScannerCoordinator()
        let model = BridgePairingScannerModel(coordinator: coordinator)
        var payloadCount = 0
        await model.begin { _ in payloadCount += 1 }
        _ = model.makeViewController()
        model.cancel()
        coordinator.emit(payload: "late-payload")

        XCTAssertEqual(model.state, .cancelled)
        XCTAssertEqual(coordinator.stopCount, 1)
        XCTAssertEqual(payloadCount, 0)
    }
}
