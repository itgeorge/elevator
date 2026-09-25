import AVFoundation
import SwiftUI
import UIKit
import VisionKit

public enum BridgePairingCameraAuthorization: Equatable, Sendable {
    case notDetermined
    case authorized
    case denied
    case restricted
}

public enum BridgePairingScannerAvailability: Equatable, Sendable {
    case supported
    case unsupportedHardware
    case cameraUnavailable
}

public enum BridgePairingScannerError: Error, Equatable, LocalizedError, Sendable {
    case unsupportedHardware
    case cameraUnavailable
    case permissionDenied
    case permissionRestricted
    case runtime(String)

    public var errorDescription: String? {
        switch self {
        case .unsupportedHardware:
            return "This iPad does not support QR scanning. Enter the bridge URL and six-digit PIN manually."
        case .cameraUnavailable:
            return "The camera is unavailable. Close other camera apps, check the iPad camera, or use manual URL and PIN pairing."
        case .permissionDenied:
            return "Camera access is denied. Enable Camera for RidesTablet in Settings, or use manual URL and PIN pairing."
        case .permissionRestricted:
            return "Camera access is restricted on this iPad. Ask an administrator to allow it, or use manual URL and PIN pairing."
        case .runtime(let detail):
            return "QR scanning stopped unexpectedly (\(detail)). Try scanning again or use manual URL and PIN pairing."
        }
    }
}

public enum BridgePairingScannerState: Equatable, Sendable {
    case preparing
    case scanning
    case captured
    case cancelled
    case unavailable(BridgePairingScannerError)
    case failed(BridgePairingScannerError)

    public var isScanning: Bool {
        if case .scanning = self { return true }
        return false
    }
}

/// The camera boundary is deliberately injectable. Tests can provide availability,
/// authorization, a plain UIViewController, and deterministic callbacks without touching
/// VisionKit or asking the simulator for camera access.
@MainActor
public protocol BridgePairingScannerCoordinator: AnyObject {
    var availability: BridgePairingScannerAvailability { get }
    var authorization: BridgePairingCameraAuthorization { get }
    func requestCameraAccess() async -> BridgePairingCameraAuthorization
    func makeViewController(
        onPayload: @escaping (String) -> Void,
        onError: @escaping (BridgePairingScannerError) -> Void
    ) -> UIViewController
    func startScanning() async throws
    func stopScanning()
}

@MainActor
public final class BridgePairingScannerModel: ObservableObject {
    @Published public private(set) var state: BridgePairingScannerState?
    @Published public private(set) var message: String?

    nonisolated(unsafe) private let coordinator: any BridgePairingScannerCoordinator
    private var didDeliverPayload = false
    nonisolated(unsafe) private var didStartScanning = false
    nonisolated(unsafe) private var didMakeViewController = false
    private var didCancel = false
    nonisolated(unsafe) private var didStopScanning = false
    private var onPayload: ((String) -> Void)?

    public init(coordinator: any BridgePairingScannerCoordinator) {
        self.coordinator = coordinator
        self.state = nil
        self.message = nil
    }

    public func begin(onPayload: @escaping (String) -> Void) async {
        guard state == nil else { return }
        self.onPayload = onPayload
        state = .preparing

        if coordinator.availability == .unsupportedHardware {
            return failUnavailable(.unsupportedHardware)
        }

        var authorization = coordinator.authorization
        if authorization == .notDetermined {
            authorization = await coordinator.requestCameraAccess()
        }
        // Dismissal can happen while the system permission prompt is up. Do not
        // resurrect a dismissed sheet when that prompt eventually completes.
        guard !didCancel else { return }

        switch authorization {
        case .authorized:
            guard coordinator.availability == .supported else {
                return failUnavailable(.cameraUnavailable)
            }
            state = .scanning
            message = "Point the iPad camera at the RidesBridge pairing QR code."
        case .notDetermined:
            failUnavailable(.runtime("camera permission was not resolved"))
        case .denied:
            failUnavailable(.permissionDenied)
        case .restricted:
            failUnavailable(.permissionRestricted)
        }
    }

    public func makeViewController() -> UIViewController {
        didMakeViewController = true
        return coordinator.makeViewController(
            onPayload: { [weak self] payload in self?.receive(payload: payload) },
            onError: { [weak self] error in self?.receive(error: error) }
        )
    }

    public func startScanning() async {
        guard state == .scanning, !didDeliverPayload, !didStartScanning, !didCancel else { return }
        didStartScanning = true
        do {
            try await coordinator.startScanning()
        } catch is CancellationError {
            cancel()
        } catch {
            receive(error: .runtime(error.localizedDescription))
        }
    }

    /// Cancels both active scanning and the in-flight preparation/permission
    /// transition. It is safe for sheet dismissal, representable dismantling,
    /// and repeated lifecycle callbacks.
    public func cancel() {
        switch state {
        case .captured, .cancelled, .unavailable, .failed:
            return
        case .none, .preparing, .scanning:
            break
        }

        didCancel = true
        stopScanningIfNeeded()
        onPayload = nil
        state = .cancelled
        message = "QR scanning cancelled."
    }

    private func receive(payload: String) {
        guard state?.isScanning == true, !didDeliverPayload, !didCancel else { return }
        didDeliverPayload = true
        stopScanningIfNeeded()
        state = .captured
        message = "Pairing QR captured."
        let handler = onPayload
        onPayload = nil
        handler?(payload)
    }

    private func receive(error: BridgePairingScannerError) {
        guard state?.isScanning == true, !didCancel else { return }
        stopScanningIfNeeded()
        onPayload = nil
        fail(error)
    }

    private func stopScanningIfNeeded() {
        guard !didStopScanning, didMakeViewController || didStartScanning else { return }
        didStopScanning = true
        coordinator.stopScanning()
    }

    private func failUnavailable(_ error: BridgePairingScannerError) {
        onPayload = nil
        state = .unavailable(error)
        message = error.localizedDescription
    }

    private func fail(_ error: BridgePairingScannerError) {
        state = .failed(error)
        message = error.localizedDescription
    }

    deinit {
        guard !didStopScanning && (didMakeViewController || didStartScanning) else { return }
        // Deinitialization is nonisolated in Swift even for a MainActor class.
        // Route the final native stop back to the coordinator's actor.
        let coordinator = coordinator
        Task { @MainActor in coordinator.stopScanning() }
    }
}

public struct BridgePairingScannerView: View {
    @Environment(\.dismiss) private var dismiss
    @StateObject private var model: BridgePairingScannerModel
    private let coordinator: any BridgePairingScannerCoordinator
    private let onPayload: (String) -> Void

    public init(
        coordinator: any BridgePairingScannerCoordinator,
        onPayload: @escaping (String) -> Void
    ) {
        self.coordinator = coordinator
        self.onPayload = onPayload
        _model = StateObject(wrappedValue: BridgePairingScannerModel(coordinator: coordinator))
    }

    public var body: some View {
        NavigationStack {
            Group {
                if model.state?.isScanning == true {
                    ScannerViewController(model: model)
                        .ignoresSafeArea()
                } else if let message = model.message {
                    ContentUnavailableView(
                        scannerTitle,
                        systemImage: scannerSymbol,
                        description: Text(message)
                    )
                } else {
                    ProgressView("Preparing camera…")
                }
            }
            .navigationTitle("Scan pairing QR")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Cancel") {
                        model.cancel()
                        dismiss()
                    }
                }
            }
        }
        .task {
            await model.begin(onPayload: { payload in
                // BridgePairingScannerModel has already stopped the native scanner. Dismiss
                // before starting the async import so a captured one-time PIN is never replayed.
                dismiss()
                Task { @MainActor in onPayload(payload) }
            })
        }
        .onDisappear {
            // Covers interactive sheet dismissal, navigation teardown, and a
            // permission prompt that is dismissed before it resolves.
            model.cancel()
        }
    }

    private var scannerTitle: String {
        switch model.state {
        case .failed(let error), .unavailable(let error):
            switch error {
            case .unsupportedHardware: "QR scanning unavailable"
            case .cameraUnavailable: "Camera unavailable"
            case .permissionDenied, .permissionRestricted: "Camera permission needed"
            case .runtime: "QR scanner error"
            }
        case .cancelled: "Scan cancelled"
        case .captured: "QR captured"
        default: "Preparing camera"
        }
    }

    private var scannerSymbol: String {
        switch model.state {
        case .cancelled: "xmark.circle"
        case .captured: "checkmark.circle"
        default: "camera.fill"
        }
    }
}

private struct ScannerViewController: UIViewControllerRepresentable {
    let model: BridgePairingScannerModel

    final class Coordinator {
        let model: BridgePairingScannerModel

        init(model: BridgePairingScannerModel) {
            self.model = model
        }
    }

    func makeCoordinator() -> Coordinator {
        Coordinator(model: model)
    }

    func makeUIViewController(context: Context) -> UIViewController {
        let viewController = model.makeViewController()
        Task { @MainActor in await model.startScanning() }
        return viewController
    }

    func updateUIViewController(_ uiViewController: UIViewController, context: Context) {}

    static func dismantleUIViewController(_ uiViewController: UIViewController, coordinator: Coordinator) {
        coordinator.model.cancel()
    }
}

@MainActor
public final class NativeBridgePairingScannerCoordinator: NSObject, BridgePairingScannerCoordinator, DataScannerViewControllerDelegate {
    nonisolated(unsafe) private var scanner: DataScannerViewController?
    private var onPayload: ((String) -> Void)?
    private var onError: ((BridgePairingScannerError) -> Void)?
    private var didStop = false

    public override init() {
        super.init()
    }

    public var availability: BridgePairingScannerAvailability {
        guard DataScannerViewController.isSupported else { return .unsupportedHardware }
        guard DataScannerViewController.isAvailable else { return .cameraUnavailable }
        return .supported
    }

    public var authorization: BridgePairingCameraAuthorization {
        switch AVCaptureDevice.authorizationStatus(for: .video) {
        case .notDetermined: .notDetermined
        case .authorized: .authorized
        case .denied: .denied
        case .restricted: .restricted
        @unknown default: .restricted
        }
    }

    public func requestCameraAccess() async -> BridgePairingCameraAuthorization {
        guard authorization == .notDetermined else { return authorization }
        _ = await AVCaptureDevice.requestAccess(for: .video)
        return authorization
    }

    public func makeViewController(
        onPayload: @escaping (String) -> Void,
        onError: @escaping (BridgePairingScannerError) -> Void
    ) -> UIViewController {
        // A coordinator is retained by the parent view and can be reused for a
        // later presentation. Never orphan the previous native scanner.
        stopScanning()
        didStop = false
        self.onPayload = onPayload
        self.onError = onError
        let scanner = DataScannerViewController(
            recognizedDataTypes: [.barcode(symbologies: [.qr])],
            qualityLevel: .balanced,
            recognizesMultipleItems: false,
            isHighFrameRateTrackingEnabled: false,
            isPinchToZoomEnabled: true,
            isGuidanceEnabled: true,
            isHighlightingEnabled: true
        )
        scanner.delegate = self
        self.scanner = scanner
        return scanner
    }

    public func startScanning() async throws {
        guard let scanner, !didStop else { return }
        do {
            try scanner.startScanning()
        } catch {
            let handler = onError
            stopScanning()
            handler?(.runtime(error.localizedDescription))
            throw error
        }
    }

    public func stopScanning() {
        guard !didStop else { return }
        didStop = true
        let scanner = scanner
        self.scanner = nil
        scanner?.stopScanning()
        onPayload = nil
        onError = nil
    }

    public func dataScanner(
        _ dataScanner: DataScannerViewController,
        didAdd addedItems: [RecognizedItem],
        allItems: [RecognizedItem]
    ) {
        guard !didStop else { return }
        for item in addedItems {
            guard case .barcode(let barcode) = item,
                  let payload = barcode.payloadStringValue,
                  !payload.isEmpty else { continue }
            let handler = onPayload
            stopScanning()
            handler?(payload)
            return
        }
    }

    public func dataScanner(
        _ dataScanner: DataScannerViewController,
        didRemove removedItems: [RecognizedItem],
        allItems: [RecognizedItem]
    ) {}

    public func dataScanner(
        _ dataScanner: DataScannerViewController,
        didUpdate updatedItems: [RecognizedItem],
        allItems: [RecognizedItem]
    ) {}

    public func dataScanner(
        _ dataScanner: DataScannerViewController,
        becameUnavailableWithError error: DataScannerViewController.ScanningUnavailable
    ) {
        guard !didStop else { return }
        let handler = onError
        stopScanning()
        handler?(.runtime(error.localizedDescription))
    }

    deinit {
        let scanner = scanner
        Task { @MainActor in
            scanner?.delegate = nil
            scanner?.stopScanning()
        }
    }
}
