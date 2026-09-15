import XCTest
@testable import RidesTablet

final class FakeProxmarkTests: XCTestCase {
    func testFakeReturnsConfiguredAsyncOutcomesAndCountsCalls() async {
        let fake = FakeProxmark(
            detect: .noChip,
            tune: .measured(millivolts: 420),
            read: .failure("read failed")
        )

        let detect = await fake.detect()
        let tune = await fake.tune()
        let read = await fake.read()
        XCTAssertEqual(detect, .noChip)
        XCTAssertEqual(tune, .measured(millivolts: 420))
        XCTAssertEqual(read, .failure("read failed"))
        XCTAssertEqual(fake.detectCallCount, 1)
        XCTAssertEqual(fake.tuneCallCount, 1)
        XCTAssertEqual(fake.readCallCount, 1)
    }

    func testSimulationScenariosDriveAtomicScanAndWriteFailure() async {
        let fake = FakeProxmark()
        fake.apply(.weakSignal)
        let weak = await fake.detectTuneRead()
        XCTAssertEqual(weak, .known(Token.sample(rideCount: 73, sequence: .mercury), signalMillivolts: 120))

        fake.apply(.unknownFamily)
        let unknown = await fake.detectTuneRead()
        if case .unknown = unknown {
            // expected
        } else {
            XCTFail("Expected unknown-family simulation")
        }

        fake.apply(.writeFailure)
        let token = Token.sample()
        let write = await fake.write(token)
        XCTAssertEqual(write, .failure("Charge could not be written. Try again."))
        XCTAssertNil(fake.lastWrittenToken)
        XCTAssertEqual(fake.detectCallCount, 2)
        XCTAssertEqual(fake.tuneCallCount, 2)
        XCTAssertEqual(fake.readCallCount, 2)
    }

    func testSuccessfulWriteBecomesTheNextRead() async {
        let fake = FakeProxmark()
        let token = Token.sample(rideCount: 222, sequence: .venus)

        let write = await fake.write(token)
        let read = await fake.read()
        XCTAssertEqual(write, .success)
        XCTAssertEqual(read, .known(token))
        XCTAssertEqual(fake.lastWrittenToken, token)
    }

    func testSuccessfulResetOverwriteBecomesZeroRideRead() async {
        let fake = FakeProxmark()
        let image = ResetSequence.for(.saturn).resetImage()

        let overwrite = await fake.overwrite(image)
        XCTAssertEqual(overwrite, .success)
        let read = await fake.read()
        guard case .known(let token) = read else {
            return XCTFail("Expected a known reset token")
        }
        XCTAssertEqual(token.sequence, .saturn)
        XCTAssertEqual(token.rideCount, 0)
        XCTAssertEqual(token.blocks, image)
    }

    func testFailedResetOverwriteDoesNotMutateTheNextRead() async {
        let original = Token.sample(rideCount: 73, sequence: .mercury)
        let fake = FakeProxmark(read: .known(original), overwrite: .failure("reset failed"))
        let image = ResetSequence.for(.neptune).resetImage()

        let overwrite = await fake.overwrite(image)
        let read = await fake.read()
        XCTAssertEqual(overwrite, .failure("reset failed"))
        XCTAssertNil(fake.lastOverwrittenImage)
        XCTAssertEqual(read, .known(original))
    }
}
