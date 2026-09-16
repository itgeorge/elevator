import UIKit
import Network

final class ProbeViewController: UIViewController {
    private let host = "172.20.10.4"
    private let port = 48123
    private let serviceType = "_hotspotprobe._tcp"

    private let textView = UITextView()
    private var browser: NWBrowser?
    private var directResult: String?
    private var discoveredServices = [String]()
    private var bonjourResolutionResult: String?
    private var bonjourConnection: NWConnection?
    private var bonjourConnectionStarted = false
    private var bonjourResolutionFinished = false
    private var directFinished = false
    private var browseFinished = false
    private var reportSent = false

    override func viewDidLoad() {
        super.viewDidLoad()
        view.backgroundColor = .systemBackground
        textView.isEditable = false
        textView.font = .monospacedSystemFont(ofSize: 17, weight: .regular)
        textView.textColor = .label
        textView.text = "iPad Hotspot Probe\n\nIf prompted, tap Allow for Local Network.\nRunning one HTTP request and one Bonjour browse; no retries."
        textView.translatesAutoresizingMaskIntoConstraints = false
        view.addSubview(textView)
        NSLayoutConstraint.activate([
            textView.leadingAnchor.constraint(equalTo: view.safeAreaLayoutGuide.leadingAnchor, constant: 20),
            textView.trailingAnchor.constraint(equalTo: view.safeAreaLayoutGuide.trailingAnchor, constant: -20),
            textView.topAnchor.constraint(equalTo: view.safeAreaLayoutGuide.topAnchor, constant: 20),
            textView.bottomAnchor.constraint(equalTo: view.safeAreaLayoutGuide.bottomAnchor, constant: -20)
        ])
        startDirectRequest()
        startBonjourBrowse()
    }

    private func startDirectRequest() {
        guard let url = URL(string: "http://\(host):\(port)/health") else { return }
        var request = URLRequest(url: url)
        request.timeoutInterval = 6
        URLSession.shared.dataTask(with: request) { [weak self] data, response, error in
            let result: String
            if let error {
                result = "HTTP error: \(error.localizedDescription)"
            } else if let http = response as? HTTPURLResponse {
                let body = String(data: data ?? Data(), encoding: .utf8) ?? "<non-UTF8>"
                result = "HTTP \(http.statusCode): \(body)"
            } else {
                result = "HTTP error: no HTTP response"
            }
            DispatchQueue.main.async {
                guard let self else { return }
                self.directResult = result
                self.directFinished = true
                self.refreshText()
                self.finishIfReady()
            }
        }.resume()
    }

    private func startBonjourBrowse() {
        let browser = NWBrowser(
            for: .bonjour(type: serviceType, domain: "local."),
            using: NWParameters.tcp
        )
        self.browser = browser
        browser.stateUpdateHandler = { [weak self] state in
            DispatchQueue.main.async {
                guard let self else { return }
                if case .failed(let error) = state {
                    self.discoveredServices.append("Browse error: \(error.localizedDescription)")
                    self.refreshText()
                }
            }
        }
        browser.browseResultsChangedHandler = { [weak self] results, _ in
            DispatchQueue.main.async {
                guard let self else { return }
                self.discoveredServices = results.compactMap { result in
                    guard case let .service(name, type, domain, interface) = result.endpoint else { return nil }
                    let interfaceName = interface?.debugDescription ?? "unknown interface"
                    return "\(name) \(type) \(domain) via \(interfaceName)"
                }
                if let endpoint = results.compactMap({ result -> NWEndpoint? in
                    guard case .service = result.endpoint else { return nil }
                    return result.endpoint
                }).first {
                    self.startBonjourConnection(to: endpoint)
                }
                self.refreshText()
            }
        }
        browser.start(queue: .main)
        DispatchQueue.main.asyncAfter(deadline: .now() + 8) { [weak self] in
            guard let self else { return }
            self.browser?.cancel()
            self.browser = nil
            self.browseFinished = true
            if !self.bonjourConnectionStarted {
                self.bonjourResolutionResult = "no service discovered"
                self.bonjourResolutionFinished = true
            }
            self.refreshText()
            self.finishIfReady()
        }
    }

    private func startBonjourConnection(to endpoint: NWEndpoint) {
        guard !bonjourConnectionStarted else { return }
        bonjourConnectionStarted = true
        let connection = NWConnection(to: endpoint, using: .tcp)
        bonjourConnection = connection
        connection.stateUpdateHandler = { [weak self] state in
            DispatchQueue.main.async {
                guard let self, !self.bonjourResolutionFinished else { return }
                switch state {
                case .ready:
                    self.bonjourResolutionResult = "resolved; TCP connection ready"
                    self.bonjourResolutionFinished = true
                    connection.cancel()
                case .failed(let error):
                    self.bonjourResolutionResult = "resolution/connect error: \(error.localizedDescription)"
                    self.bonjourResolutionFinished = true
                case .cancelled:
                    self.bonjourResolutionResult = "resolution cancelled"
                    self.bonjourResolutionFinished = true
                default:
                    break
                }
                self.refreshText()
                self.finishIfReady()
            }
        }
        connection.start(queue: .main)
        DispatchQueue.main.asyncAfter(deadline: .now() + 6) { [weak self] in
            guard let self, !self.bonjourResolutionFinished else { return }
            connection.cancel()
            self.bonjourResolutionResult = "resolution timeout"
            self.bonjourResolutionFinished = true
            self.refreshText()
            self.finishIfReady()
        }
    }

    private func finishIfReady() {
        guard directFinished && browseFinished && bonjourResolutionFinished && !reportSent else { return }
        reportSent = true
        let report: [String: Any] = [
            "direct": directResult ?? "missing",
            "bonjour": discoveredServices,
            "bonjourResolution": bonjourResolutionResult ?? "missing",
            "serviceType": serviceType,
            "device": "iPad Air 4"
        ]
        guard let data = try? JSONSerialization.data(withJSONObject: report),
              let url = URL(string: "http://\(host):\(port)/report") else { return }
        var request = URLRequest(url: url)
        request.httpMethod = "POST"
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        request.httpBody = data
        request.timeoutInterval = 4
        URLSession.shared.dataTask(with: request) { [weak self] _, _, error in
            DispatchQueue.main.async {
                if let error {
                    self?.appendStatus("Report POST error: \(error.localizedDescription)")
                } else {
                    self?.appendStatus("Report POST: sent")
                }
            }
        }.resume()
    }

    private func appendStatus(_ line: String) {
        textView.text += "\n\n\(line)"
    }

    private func refreshText() {
        let direct = directResult ?? "pending"
        let bonjour = discoveredServices.isEmpty ? "none observed yet" : discoveredServices.joined(separator: "\n  ")
        let resolution = bonjourResolutionResult ?? "pending"
        textView.text = """
        iPad Hotspot Probe

        HTTP GET http://\(host):\(port)/health
        \(direct)

        Bonjour browse \(serviceType)
          \(bonjour)
        Resolution: \(resolution)

        One request/browse/connection only; no retries.
        """
    }
}
