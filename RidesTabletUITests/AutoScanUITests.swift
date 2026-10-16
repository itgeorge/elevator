import XCTest

final class AutoScanUITests: XCTestCase {
    private var app: XCUIApplication!

    override func setUpWithError() throws {
        continueAfterFailure = false
        app = XCUIApplication()
        if !name.contains("PhysicalReadOnly") {
            app.launchEnvironment["RIDES_SIMULATOR_FAKE_READER"] = "1"
            app.launchEnvironment["RIDES_SCREENSHOT_SCENE"] = "history"
        }
        app.launch()
    }

    override func tearDownWithError() throws {
        app?.terminate()
        app = nil
    }

    func testAutoScanLeavesPendingRidesAloneAndManualDetectStopsIt() {
        let autoToggle = app.buttons["auto-scan-toggle"]
        let pending = app.descendants(matching: .any)["pending-rides-display"]
        let detect = app.buttons["detect-primary-action"]

        XCTAssertTrue(autoToggle.waitForExistence(timeout: 20))
        XCTAssertTrue(pending.waitForExistence(timeout: 10))
        XCTAssertTrue(detect.waitForExistence(timeout: 10))
        let initialPending = pending.value as? String
        XCTAssertNotNil(initialPending)

        autoToggle.tap()
        XCTAssertTrue(autoToggle.waitForValue("On"))
        Thread.sleep(forTimeInterval: 2)
        XCTAssertEqual(pending.value as? String, initialPending)

        detect.tap()
        XCTAssertTrue(autoToggle.waitForValue("Off"))
        let currentRides = app.descendants(matching: .any).matching(identifier: "current-rides-display").firstMatch
        XCTAssertTrue(currentRides.waitForExistence(timeout: 10))
        let currentValue = currentRides.value as? String
        XCTAssertNotNil(currentValue)
        XCTAssertTrue((pending.value as? String)?.contains("Pending total \(currentValue!) rides") == true)
    }

    func testAutoScanConfirmsRemovalAndNewPlacement() {
        let autoToggle = app.buttons["auto-scan-toggle"]
        let currentRides = app.descendants(matching: .any).matching(identifier: "current-rides-display").firstMatch
        let pending = app.descendants(matching: .any)["pending-rides-display"]
        let simulationMenu = app.buttons["Simulation scenarios"]

        XCTAssertTrue(autoToggle.waitForExistence(timeout: 20))
        XCTAssertTrue(currentRides.waitForExistence(timeout: 10))
        autoToggle.tap()
        XCTAssertTrue(autoToggle.waitForValue("On"))

        simulationMenu.tap()
        app.buttons["No chip"].tap()
        XCTAssertTrue(currentRides.waitForNonExistence(timeout: 12), "Auto-scan should confirm token removal")
        XCTAssertTrue(autoToggle.waitForValue("On"))

        simulationMenu.tap()
        app.buttons["Good token"].tap()
        XCTAssertTrue(currentRides.waitForValue("73", timeout: 12), "Auto-scan should confirm the newly placed token")
        XCTAssertTrue((pending.value as? String)?.contains("Pending total 73 rides") == true)

        let attachment = XCTAttachment(screenshot: app.screenshot())
        attachment.name = "Auto-scan confirmed new token placement"
        attachment.lifetime = .keepAlways
        add(attachment)

        autoToggle.tap()
        XCTAssertTrue(autoToggle.waitForValue("Off"))
    }

    func testPhysicalReadOnlyAutoScanPreservesPending() throws {
#if targetEnvironment(simulator)
        throw XCTSkip("Physical reader smoke is intended for a paired iPad and bridge.")
#else
        let autoToggle = app.buttons["auto-scan-toggle"]
        let currentRides = app.descendants(matching: .any).matching(identifier: "current-rides-display").firstMatch
        let pending = app.descendants(matching: .any)["pending-rides-display"]
        let detect = app.buttons["detect-primary-action"]

        XCTAssertTrue(autoToggle.waitForExistence(timeout: 30), "Wait for saved pairing to reconnect")
        autoToggle.tap()
        XCTAssertTrue(autoToggle.waitForValue("On", timeout: 30))
        XCTAssertTrue(currentRides.waitForExistence(timeout: 30), "Wait for a known token after auto-scan")
        XCTAssertTrue(pending.waitForExistence(timeout: 10))

        let addOneRide = app.buttons["pending-rides-plus-1"]
        XCTAssertTrue(addOneRide.waitForExistence(timeout: 10))
        addOneRide.tap()
        let pendingAfterAdjustment = pending.value as? String
        XCTAssertNotNil(pendingAfterAdjustment)
        Thread.sleep(forTimeInterval: 10) // Covers multiple probes and a periodic full read.
        XCTAssertEqual(pending.value as? String, pendingAfterAdjustment)

        let portrait = XCTAttachment(screenshot: app.screenshot())
        portrait.name = "Physical read-only auto-scan portrait"
        portrait.lifetime = .keepAlways
        add(portrait)

        XCUIDevice.shared.orientation = .landscapeLeft
        defer { XCUIDevice.shared.orientation = .portrait }
        let landscape = XCTAttachment(screenshot: app.screenshot())
        landscape.name = "Physical read-only auto-scan landscape"
        landscape.lifetime = .keepAlways
        add(landscape)

        detect.tap()
        XCTAssertTrue(autoToggle.waitForValue("Off", timeout: 30))
#endif
    }

    func testLandscapeHistoryAndAutoScanScreenshot() {
        let device = XCUIDevice.shared
        device.orientation = .landscapeLeft
        defer { device.orientation = .portrait }

        let historyTitle = app.staticTexts["Completed charges"]
        let autoToggle = app.buttons["auto-scan-toggle"]
        XCTAssertTrue(historyTitle.waitForExistence(timeout: 15))
        XCTAssertTrue(autoToggle.waitForExistence(timeout: 10))
        XCTAssertTrue(app.buttons["detect-primary-action"].exists)

        let attachment = XCTAttachment(screenshot: app.screenshot())
        attachment.name = "Auto-scan history landscape"
        attachment.lifetime = .keepAlways
        add(attachment)
    }
}

private extension XCUIElement {
    func waitForNonExistence(timeout: TimeInterval) -> Bool {
        let predicate = NSPredicate(format: "exists == false")
        let expectation = XCTNSPredicateExpectation(predicate: predicate, object: self)
        return XCTWaiter.wait(for: [expectation], timeout: timeout) == .completed
    }

    func waitForValue(_ expectedValue: String, timeout: TimeInterval = 10) -> Bool {
        let predicate = NSPredicate(format: "value == %@", expectedValue)
        let expectation = XCTNSPredicateExpectation(predicate: predicate, object: self)
        return XCTWaiter.wait(for: [expectation], timeout: timeout) == .completed
    }
}
