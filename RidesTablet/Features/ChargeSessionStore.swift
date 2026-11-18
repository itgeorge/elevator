import Foundation

public struct ChargeSession: Codable, Equatable, Identifiable, Sendable {
    public let id: UUID
    public let createdAt: Date
    public var updatedAt: Date
    public var charges: [CompletedCharge]

    public init(id: UUID = UUID(), createdAt: Date = Date(), updatedAt: Date? = nil, charges: [CompletedCharge] = []) {
        self.id = id
        self.createdAt = createdAt
        self.updatedAt = updatedAt ?? createdAt
        self.charges = charges
    }

    public var amountTotal: Decimal {
        charges.reduce(Decimal.zero) { $0 + $1.amountEUR }
    }

    public var rideTotal: Int {
        charges.reduce(0) { $0 + $1.addedRides }
    }
}

public final class FileChargeSessionStore {
    private let directory: URL
    private let fileManager: FileManager
    private let encoder = JSONEncoder()
    private let decoder = JSONDecoder()
    private let pointerURL: URL

    public init(directory: URL? = nil) {
        self.fileManager = .default
        if let directory {
            self.directory = directory
        } else {
            let base = fileManager.urls(for: .applicationSupportDirectory, in: .userDomainMask).first
                ?? fileManager.homeDirectoryForCurrentUser.appendingPathComponent("Library/Application Support", isDirectory: true)
            self.directory = base.appendingPathComponent("RidesTablet/ChargeSessions", isDirectory: true)
        }
        self.pointerURL = self.directory.appendingPathComponent("active-session.json")
        encoder.outputFormatting = [.sortedKeys]
    }

    /// Returns the selected session. If no sessions exist yet, creates the initial session.
    /// If the pointer is absent but session files exist, the newest valid session is selected.
    public func loadActiveSession() throws -> ChargeSession {
        try ensureDirectory()
        if fileManager.fileExists(atPath: pointerURL.path) {
            let pointer: ActiveSessionPointer
            do {
                pointer = try decoder.decode(ActiveSessionPointer.self, from: Data(contentsOf: pointerURL))
            } catch {
                throw StoreError.corruptFile(pointerURL, underlying: error)
            }
            return try load(id: pointer.id)
        }

        let sessions = try listSessions()
        if let newest = sessions.first {
            try writePointer(newest.id)
            return newest
        }
        return try createSession()
    }

    /// Lists all persisted sessions, newest first. A corrupt session is reported, not skipped.
    public func listSessions() throws -> [ChargeSession] {
        try ensureDirectory()
        let urls = try fileManager.contentsOfDirectory(at: directory, includingPropertiesForKeys: nil)
            .filter { $0.pathExtension == "json" && $0.lastPathComponent != pointerURL.lastPathComponent }
        var sessions: [ChargeSession] = []
        for url in urls {
            do {
                sessions.append(try decoder.decode(ChargeSession.self, from: Data(contentsOf: url)))
            } catch {
                throw StoreError.corruptFile(url, underlying: error)
            }
        }
        return sessions.sorted {
            if $0.createdAt != $1.createdAt { return $0.createdAt > $1.createdAt }
            return $0.id.uuidString > $1.id.uuidString
        }
    }

    /// Atomically replaces the JSON file for this session. Saving does not change the active session.
    public func save(_ session: ChargeSession) throws {
        try ensureDirectory()
        var updated = session
        updated.updatedAt = Date()
        let data = try encoder.encode(updated)
        try data.write(to: sessionURL(for: updated.id), options: .atomic)
    }

    public func setActiveSession(id: UUID) throws {
        _ = try load(id: id)
        try writePointer(id)
    }

    public func load(id: UUID) throws -> ChargeSession {
        let url = sessionURL(for: id)
        guard fileManager.fileExists(atPath: url.path) else { throw StoreError.sessionNotFound(id) }
        do {
            let session = try decoder.decode(ChargeSession.self, from: Data(contentsOf: url))
            guard session.id == id else { throw StoreError.sessionIDMismatch(expected: id, actual: session.id) }
            return session
        } catch let error as StoreError {
            throw error
        } catch {
            throw StoreError.corruptFile(url, underlying: error)
        }
    }

    public func createSession() throws -> ChargeSession {
        let session = ChargeSession()
        try save(session)
        try writePointer(session.id)
        return session
    }

    private func ensureDirectory() throws {
        try fileManager.createDirectory(at: directory, withIntermediateDirectories: true)
    }

    private func sessionURL(for id: UUID) -> URL {
        directory.appendingPathComponent("\(id.uuidString).json")
    }

    private func writePointer(_ id: UUID) throws {
        try ensureDirectory()
        let data = try encoder.encode(ActiveSessionPointer(id: id))
        try data.write(to: pointerURL, options: .atomic)
    }

    private struct ActiveSessionPointer: Codable {
        let id: UUID
    }

    public enum StoreError: Error, LocalizedError {
        case sessionNotFound(UUID)
        case corruptFile(URL, underlying: Error)
        case sessionIDMismatch(expected: UUID, actual: UUID)

        public var errorDescription: String? {
            switch self {
            case .sessionNotFound(let id): return "Charge session \(id) was not found."
            case .corruptFile(let url, let error): return "Could not read charge session at \(url.path): \(error.localizedDescription)"
            case .sessionIDMismatch(let expected, let actual): return "Charge session file ID mismatch (expected \(expected), found \(actual))."
            }
        }
    }
}
