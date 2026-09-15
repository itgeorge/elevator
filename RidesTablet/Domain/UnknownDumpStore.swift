import Foundation

public struct UnknownDumpStore: Sendable {
    public let directory: URL

    public init(directory: URL? = nil, fileManager: FileManager = .default) {
        self.directory = directory ?? fileManager.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
            .appendingPathComponent("RidesTablet/UnknownDumps", isDirectory: true)
    }

    /// Persists the eight page-0 blocks as the same big-endian 32-byte image used by RidesCli.
    public func save(_ token: UnknownToken, rideLabel: String = "UNKNOWN") throws -> URL {
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        let suffix = rideLabel.isEmpty ? "UNKNOWN" : rideLabel
        let name = "elevator-t55xx-\(token.blocks.map(Token.hex).joined(separator: "-"))--rides-\(suffix).bin"
        let url = directory.appendingPathComponent(name)
        var data = Data(capacity: token.blocks.count * 4)
        for block in token.blocks {
            data.append(UInt8((block >> 24) & 0xff))
            data.append(UInt8((block >> 16) & 0xff))
            data.append(UInt8((block >> 8) & 0xff))
            data.append(UInt8(block & 0xff))
        }
        try data.write(to: url, options: .atomic)
        return url
    }
}
