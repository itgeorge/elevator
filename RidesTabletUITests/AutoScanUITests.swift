import XCTest

final class AutoScanUITests: XCTestCase {
    private var app: XCUIApplication!

    override func setUpWithError() throws {
        continueAfterFailure = false
        app = XCUIApplication()
        if !name.contains("PhysicalReadOnly") {
            app.launchEnvironment["RIDES_SIMULATOR_FAKE_READER"] = "1"
            app.launchEnvironment["RIDES_SCREENSHOT_SCENE"] = name.contains("AutoPlacement") ? "calibrated" : "history"
        }
        app.launch()
    }

    override func tearDownWithError() throws {
        app?.terminate()
        app = nil
    }

    func testDetectAndAutoControlsHaveEqualWidthAndStableAutoLabel() {
        let detect = app.buttons["detect-primary-action"]
        let auto = app.buttons["auto-scan-toggle"]
        XCTAssertTrue(detect.waitForExistence(timeout: 20))
        XCTAssertTrue(auto.waitForExistence(timeout: 10))
        XCTAssertTrue(auto.staticTexts["Auto"].exists)
        XCTAssertEqual(detect.frame.width, auto.frame.width, accuracy: 2)
        XCTAssertTrue(auto.waitForValue("Off"))
        auto.tap()
        XCTAssertTrue(auto.waitForValue("On"))
    }

    func testCancelOpaqueConfirmationsPreservesAutoAndCalibration() {
        let autoToggle = app.buttons["auto-scan-toggle"]
        XCTAssertTrue(autoToggle.waitForExistence(timeout: 20))
        XCTAssertTrue(app.buttons["new-charge-session"].isEnabled)

        autoToggle.tap()
        XCTAssertTrue(autoToggle.waitForValue("On"))
        app.buttons["new-charge-session"].tap()
        XCTAssertTrue(app.buttons["confirm-new-charge-session"].waitForExistence(timeout: 5))
        XCTAssertTrue(autoToggle.waitForValue("On"), "Showing the New confirmation must not stop Auto")
        app.buttons["cancel-confirmation"].tap()
        XCTAssertTrue(autoToggle.waitForValue("On"), "Canceling New must preserve Auto")

        app.buttons["restart-calibration"].tap()
        XCTAssertTrue(app.buttons["confirm-restart-calibration"].waitForExistence(timeout: 5))
        XCTAssertTrue(autoToggle.waitForValue("On"), "Showing calibration confirmation must not stop Auto")
        app.buttons["cancel-confirmation"].tap()
        XCTAssertTrue(autoToggle.waitForValue("On"), "Canceling calibration must preserve Auto")

        autoToggle.tap()
        XCTAssertTrue(autoToggle.waitForValue("Off"))
        XCTAssertTrue(autoToggle.isEnabled, "Calibration readiness must survive canceled confirmation")
        XCTAssertEqual(app.buttons["detect-primary-action"].label, "Detect")
    }

    func testRestartCalibrationStopsAutoAndClearsLoadedTokenWithoutRemovingHistory() {
        let autoToggle = app.buttons["auto-scan-toggle"]
        let restart = app.buttons["restart-calibration"]
        let primary = app.buttons["detect-primary-action"]
        let pending = app.descendants(matching: .any)["pending-rides-display"]
        let historyTotal = app.staticTexts["TOTAL · 3 charges"]

        XCTAssertTrue(autoToggle.waitForExistence(timeout: 20))
        XCTAssertTrue(restart.waitForExistence(timeout: 10))
        XCTAssertTrue(pending.waitForValue("Increase of 33 rides. Pending total 169 rides", timeout: 10))
        XCTAssertTrue(historyTotal.exists)

        autoToggle.tap()
        XCTAssertTrue(autoToggle.waitForValue("On"))
        restart.tap()
        app.buttons["confirm-restart-calibration"].tap()

        XCTAssertTrue(autoToggle.waitForValue("Off"))
        XCTAssertFalse(autoToggle.isEnabled)
        XCTAssertEqual(primary.label, "Calibrate: NO token")
        let status = app.descendants(matching: .any)["token-operation-status"]
        XCTAssertTrue((status.value as? String)?.contains("Remove all tokens, then calibrate") == true)
        XCTAssertFalse(pending.exists)
        XCTAssertTrue(historyTotal.exists, "Restarting calibration must retain completed-charge history")
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

    func testAutoPlacementShowsDetectingUntilFullReadCompletes() {
        // setUp launches the history fixture for existing tests; relaunch cleanly
        // with a plain fake and delayed first scan for this transient-state smoke.
        app.terminate()
        app.launchEnvironment["RIDES_SCREENSHOT_SCENE"] = "calibrated"
        app.launchEnvironment["RIDES_SIMULATOR_FAKE_READER"] = "1"
        app.launchEnvironment["RIDES_SIMULATOR_SCAN_DELAY_MS"] = "4000"
        app.launch()

        let autoToggle = app.buttons["auto-scan-toggle"]
        XCTAssertTrue(autoToggle.waitForExistence(timeout: 20))
        autoToggle.tap()
        XCTAssertTrue(autoToggle.waitForValue("On"))

        let status = app.descendants(matching: .any)["token-operation-status"]
        let detecting = NSPredicate(format: "value CONTAINS %@", "Detecting")
        let detectingExpectation = XCTNSPredicateExpectation(predicate: detecting, object: status)
        XCTAssertEqual(XCTWaiter.wait(for: [detectingExpectation], timeout: 10), .completed)

        let currentRides = app.descendants(matching: .any).matching(identifier: "current-rides-display").firstMatch
        XCTAssertTrue(currentRides.waitForValue("73", timeout: 10))
        autoToggle.tap()
        XCTAssertTrue(autoToggle.waitForValue("Off"))
    }

    func testAutoScanWeakSignalThenConfirmsNewPlacement() {
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
        let status = app.descendants(matching: .any)["token-operation-status"]
        let weakSignal = NSPredicate(format: "value CONTAINS %@", "Signal too weak")
        let weakExpectation = XCTNSPredicateExpectation(predicate: weakSignal, object: status)
        XCTAssertEqual(XCTWaiter.wait(for: [weakExpectation], timeout: 12), .completed,
                       "Low signal is gated rather than treated as confirmed token removal")
        XCTAssertTrue(autoToggle.waitForValue("On"))

        // Pause the fake's 10 Hz updates while changing its debug-only menu fixture.
        // Continuous weak-to-good recovery is covered by the model tests.
        autoToggle.tap()
        XCTAssertTrue(autoToggle.waitForValue("Off"))
        simulationMenu.tap()
        app.buttons["Good token"].tap()
        autoToggle.tap()
        XCTAssertTrue(autoToggle.waitForValue("On"))
        XCTAssertTrue(currentRides.waitForValue("73", timeout: 12), "Auto-scan should confirm the newly placed token")
        XCTAssertTrue((pending.value as? String)?.contains("Pending total 73 rides") == true)

        let attachment = XCTAttachment(screenshot: app.screenshot())
        attachment.name = "Auto-scan confirmed new token placement"
        attachment.lifetime = .keepAlways
        add(attachment)

        autoToggle.tap()
        XCTAssertTrue(autoToggle.waitForValue("Off"))
    }

    func testAutoUnavailableUntilBothCalibrationSteps() {
        app.terminate()
        app.launchEnvironment.removeValue(forKey: "RIDES_SCREENSHOT_SCENE")
        app.launchEnvironment.removeValue(forKey: "RIDES_SIMULATOR_SCAN_DELAY_MS")
        app.launchEnvironment["RIDES_SIMULATOR_FAKE_READER"] = "1"
        app.launch()

        let autoToggle = app.buttons["auto-scan-toggle"]
        let simulationMenu = app.buttons["Simulation scenarios"]
        let primary = app.buttons["detect-primary-action"]
        XCTAssertTrue(autoToggle.waitForExistence(timeout: 20))
        XCTAssertTrue(autoToggle.isEnabled == false)
        simulationMenu.tap()
        app.buttons["No chip"].tap()
        primary.tap()
        XCTAssertTrue(autoToggle.isEnabled == false, "Empty-reader calibration alone must not enable Auto")
        simulationMenu.tap()
        app.buttons["Good token"].tap()
        primary.tap()
        XCTAssertTrue(autoToggle.waitForValue("Off"))
        XCTAssertTrue(autoToggle.isEnabled)
        let status = app.descendants(matching: .any)["token-operation-status"]
        XCTAssertTrue((status.value as? String)?.contains("Calibration complete") == true)
        autoToggle.tap()
        XCTAssertTrue(autoToggle.waitForValue("On"))
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
        guard autoToggle.isEnabled else {
            throw XCTSkip("Physical Auto smoke requires both signal calibration steps.")
        }
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

    func testApartmentSessionsAndConfirmationScreenshots() {
        launchScene("apartment-history", orientation: .portrait)
        attachScreenshot("main-apt-history-portrait")
        launchScene("apartment-history", orientation: .landscapeLeft)
        XCTAssertTrue(waitForLandscapeLayout())
        attachScreenshot("main-apt-history-landscape")
        launchScene("apartment-history", orientation: .portrait)
        XCTAssertTrue(waitForPortraitLayout())

        let load = app.buttons["load-charge-session"]
        XCTAssertTrue(load.waitForExistence(timeout: 15))
        load.tap()
        XCTAssertTrue(app.navigationBars["Load session"].waitForExistence(timeout: 5))
        attachScreenshot("load-session-list")
        let sessionRows = app.buttons.matching(NSPredicate(format: "identifier BEGINSWITH %@", "saved-session-"))
        XCTAssertGreaterThan(sessionRows.count, 0)
        sessionRows.firstMatch.tap()
        XCTAssertTrue(app.staticTexts["TOTAL · 4 charges"].waitForExistence(timeout: 5))
        XCTAssertTrue(app.staticTexts["Apt #"].exists)

        app.buttons["new-charge-session"].tap()
        XCTAssertTrue(app.buttons["confirm-new-charge-session"].waitForExistence(timeout: 5))
        attachScreenshot("new-session-confirmation")
        app.buttons["confirm-new-charge-session"].tap()
        XCTAssertTrue(app.staticTexts["TOTAL · 0 charges"].waitForExistence(timeout: 5))
        app.buttons["load-charge-session"].tap()
        XCTAssertTrue(app.navigationBars["Load session"].waitForExistence(timeout: 5))
        let oldSessions = app.buttons.matching(NSPredicate(format: "identifier BEGINSWITH %@", "saved-session-"))
        XCTAssertGreaterThan(oldSessions.count, 1)
        oldSessions.element(boundBy: 1).tap()
        XCTAssertTrue(app.staticTexts["TOTAL · 4 charges"].waitForExistence(timeout: 5))

        app.buttons["restart-calibration"].tap()
        XCTAssertTrue(app.buttons["confirm-restart-calibration"].waitForExistence(timeout: 5))
        attachScreenshot("calibration-confirmation")
        app.buttons["confirm-restart-calibration"].tap()
    }

    func testApartmentChargeAndEditKeypadScreenshotsAndSave() {
        launchScene("apartment-charge", orientation: .portrait)
        let chargeInput = app.staticTexts["apartment-number-input"]
        XCTAssertTrue(chargeInput.waitForExistence(timeout: 15))
        XCTAssertTrue(app.buttons["apartment-skip"].exists)
        attachScreenshot("apartment-charge-keypad-skip-portrait")
        launchScene("apartment-charge", orientation: .landscapeLeft)
        let landscapeChargeInput = app.staticTexts["apartment-number-input"]
        XCTAssertTrue(landscapeChargeInput.waitForExistence(timeout: 10))
        XCTAssertTrue(waitForLandscapeLayout())
        attachScreenshot("apartment-charge-keypad-skip-landscape")
        launchScene("apartment-charge", orientation: .portrait)
        XCTAssertTrue(app.staticTexts["apartment-number-input"].waitForExistence(timeout: 10))
        for digit in ["5", "2"] { app.buttons["apartment-key-\(digit)"].tap() }
        app.buttons["apartment-save"].tap()

        let apartment = app.buttons.matching(NSPredicate(format: "label CONTAINS %@", "Apartment number 52")).firstMatch
        XCTAssertTrue(apartment.waitForExistence(timeout: 10))
        apartment.tap()
        XCTAssertTrue(app.staticTexts["apartment-number-input"].waitForExistence(timeout: 5))
        XCTAssertFalse(app.buttons["apartment-skip"].exists)
        attachScreenshot("apartment-edit-keypad-no-skip-portrait")
        launchScene("apartment-edit", orientation: .landscapeLeft)
        XCTAssertTrue(app.staticTexts["apartment-number-input"].waitForExistence(timeout: 10))
        XCTAssertTrue(waitForLandscapeLayout())
        XCTAssertFalse(app.buttons["apartment-skip"].exists)
        attachScreenshot("apartment-edit-keypad-no-skip-landscape")
        XCUIDevice.shared.orientation = .portrait
        app.buttons["apartment-cancel"].tap()
        attachScreenshot("apartment-after-save")
    }

    func testConnectionApartmentSecretAndClearPendingSmoke() {
        launchScene("history")
        let pending = app.descendants(matching: .any)["pending-rides-display"]
        XCTAssertTrue(pending.waitForExistence(timeout: 15))
        app.buttons["Clear pending change"].tap()
        XCTAssertTrue((pending.value as? String)?.contains("No ride change") == true)

        app.buttons["Connection diagnostics"].tap()
        let secretField = app.secureTextFields["apartmentSecretField"]
        XCTAssertTrue(secretField.waitForExistence(timeout: 10))
        let confirmField = app.secureTextFields["confirmApartmentSecretField"]
        XCTAssertTrue(confirmField.waitForExistence(timeout: 5))
        attachScreenshot("connection-apartment-secret-settings-empty")

        secretField.tap()
        secretField.typeText("simulator-ui-fingerprint-secret")
        confirmField.tap()
        confirmField.typeText("simulator-ui-fingerprint-secret")
        app.buttons["saveApartmentSecretButton"].tap()
        let fingerprint = app.staticTexts["apartmentSecretFingerprint"]
        XCTAssertTrue(fingerprint.waitForExistence(timeout: 10))
        XCTAssertTrue(app.buttons["clearApartmentSecretButton"].exists)
        XCTAssertFalse(app.secureTextFields["apartmentSecretField"].exists)
        XCTAssertFalse(app.secureTextFields["confirmApartmentSecretField"].exists)
        XCTAssertFalse(app.buttons["saveApartmentSecretButton"].exists)
        attachScreenshot("connection-apartment-secret-fingerprint")

        app.buttons["clearApartmentSecretButton"].tap()
        XCTAssertTrue(app.secureTextFields["apartmentSecretField"].waitForExistence(timeout: 5))
        XCTAssertTrue(app.secureTextFields["confirmApartmentSecretField"].exists)
        XCTAssertTrue(app.buttons["saveApartmentSecretButton"].exists)
        XCTAssertFalse(app.staticTexts["apartmentSecretFingerprint"].exists)
    }

    private func launchScene(_ scene: String, orientation: UIDeviceOrientation = .portrait) {
        app.terminate()
        XCUIDevice.shared.orientation = orientation
        Thread.sleep(forTimeInterval: 0.5)
        app.launchEnvironment["RIDES_SIMULATOR_FAKE_READER"] = "1"
        app.launchEnvironment["RIDES_SCREENSHOT_SCENE"] = scene
        app.launch()
    }

    private func waitForLandscapeLayout(timeout: TimeInterval = 8) -> Bool {
        let deadline = Date().addingTimeInterval(timeout)
        while Date() < deadline {
            let frame = app.windows.firstMatch.frame
            if frame.width > frame.height { return true }
            Thread.sleep(forTimeInterval: 0.2)
        }
        return false
    }

    private func waitForPortraitLayout(timeout: TimeInterval = 8) -> Bool {
        let deadline = Date().addingTimeInterval(timeout)
        while Date() < deadline {
            let frame = app.windows.firstMatch.frame
            if frame.height > frame.width { return true }
            Thread.sleep(forTimeInterval: 0.2)
        }
        return false
    }

    private func attachScreenshot(_ name: String) {
        let attachment = XCTAttachment(screenshot: app.screenshot())
        attachment.name = name
        attachment.lifetime = .keepAlways
        add(attachment)
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
