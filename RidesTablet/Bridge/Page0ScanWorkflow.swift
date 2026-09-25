import Foundation

enum Page0ScanWorkflow {
    static func assemblePage0(
        scan: BridgePage0ScanResponse,
        missing: BridgePage0MissingBlocksResponse
    ) throws -> [UInt32] {
        guard let block4 = UInt32(scan.block4, radix: 16),
              let block5 = UInt32(scan.block5, radix: 16),
              let block6 = UInt32(scan.block6, radix: 16) else {
            throw BridgeContractError.invalidPage0Response
        }

        var blocks = [UInt32](repeating: 0, count: 8)
        for entry in missing.blocks {
            guard let value = UInt32(entry.value, radix: 16) else {
                throw BridgeContractError.invalidPage0Response
            }
            blocks[entry.block] = value
        }
        blocks[4] = block4
        blocks[5] = block5
        blocks[6] = block6
        return blocks
    }
}
