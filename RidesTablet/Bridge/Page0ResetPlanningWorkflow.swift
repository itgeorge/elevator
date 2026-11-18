import Foundation

enum Page0ResetPlanningWorkflow {
    struct PlannedMutation: Equatable, Sendable {
        let block: Int
        let expected: String
        let desired: String
    }

    static func planMutations(
        currentBlocks: [BridgePage0BlockValue],
        profile: ResetSequence,
        apartmentSecret: String? = nil
    ) throws -> [PlannedMutation] {
        guard currentBlocks.count == Page0Blocks1To6.allowlist.count,
              zip(Page0Blocks1To6.allowlist, currentBlocks.map(\.block)).allSatisfy(==) else {
            throw BridgeContractError.invalidPage0Response
        }

        let currentByBlock = Dictionary(uniqueKeysWithValues: currentBlocks.map { ($0.block, $0.value) })
        var image = profile.resetImage()
        let currentBlock3 = UInt32(currentByBlock[3]!, radix: 16)!
        let currentBlock4 = UInt32(currentByBlock[4]!, radix: 16)!
        if let apartmentSecret, let apt = ApartmentBlockCodec.decode(secret: apartmentSecret, block3: currentBlock3, block4: currentBlock4), apt.building == 0 {
            image[4] = ApartmentBlockCodec.encode(secret: apartmentSecret, block3: image[3], building: apt.building, apartment: apt.apartment)
        } else {
            // With no usable secret, retain block 4 rather than destroying an unknown apartment payload.
            image[4] = currentBlock4
        }

        let identityMatches = (1...4).allSatisfy { block in
            currentByBlock[block] == formatBlock(image[block])
        }

        let block5 = UInt32(currentByBlock[5]!, radix: 16)!
        let block6 = UInt32(currentByBlock[6]!, radix: 16)!
        let rideRead = RideBlockResolver.resolve(block5: block5, block6: block6)
        let mirrorsMatchSequence = rideRead.status == .success && rideRead.sequence == profile.sequence

        let candidateBlocks = identityMatches && mirrorsMatchSequence ? [5, 6] : Array(1...6)
        return candidateBlocks.compactMap { block in
            let expected = currentByBlock[block]!
            let desired = formatBlock(image[block])
            guard expected != desired else { return nil }
            return PlannedMutation(block: block, expected: expected, desired: desired)
        }
    }

    private static func formatBlock(_ word: UInt32) -> String {
        String(format: "%08X", word)
    }
}
