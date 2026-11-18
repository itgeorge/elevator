import XCTest

final class PhysicalRidesUITests: XCTestCase {
    private let requiredPhysicalSecret = "rides-ui-followup-test-2026-11-19"
    private var secret: String {
        ProcessInfo.processInfo.environment["RIDES_UI_PHYSICAL_SECRET"]
            ?? ProcessInfo.processInfo.environment["TEST_RUNNER_RIDES_UI_PHYSICAL_SECRET"]
            ?? ""
    }
    private let bridgeAddress = "192.168.0.163"
    private var app = XCUIApplication()

    func testPhysicalRidesAcceptance() throws {
        #if !RIDES_PHYSICAL_UI_WRITES
        throw XCTSkip("Physical UI writes are disabled; build with RIDES_PHYSICAL_UI_WRITES for the authorized test token.")
        #elseif targetEnvironment(simulator)
        throw XCTSkip("Physical acceptance requires a real, non-simulator iPad.")
        #else
        continueAfterFailure = false
        let runnerOptIn = ProcessInfo.processInfo.environment["TEST_RUNNER_RIDES_UI_PHYSICAL_TEST"]
            ?? ProcessInfo.processInfo.environment["RIDES_UI_PHYSICAL_TEST"]
        guard runnerOptIn == "1" else { throw XCTSkip("Set TEST_RUNNER_RIDES_UI_PHYSICAL_TEST=1 to opt in.") }
        XCTAssertTrue(secret == requiredPhysicalSecret, "Set the fresh physical-test secret through the test-runner environment.")

        launchPhysicalApp()
        let current = app.descendants(matching: .any).matching(identifier: "current-rides-display").firstMatch
        let loaded = current.waitForExistence(timeout: 30)
        if !loaded {
            attach(app, name: "physical-bootstrap-failure")
            print("PHYSICAL_INITIAL_UI: \(app.debugDescription)")
        }
        XCTAssertTrue(loaded, "Paired Rides UI did not load")
        XCTAssertTrue(app.buttons["detect-primary-action"].exists)
        XCTAssertTrue(waitForLoadedRideValue(current, timeout: 30), "Real Detect did not publish a ride count")
        let initial = rideValue(current)
        XCTAssertTrue((0...500).contains(initial), "Live ride count must be within 0...500 (got \(initial))")
        attach(app, name: "initial-live-detect")

        // Set the in-memory secret through the same existing SecureFields as an operator.
        saveSecretThroughConnectionSheet()
        XCTAssertTrue(current.waitForExistence(timeout: 15))
        let existingApartment = app.buttons.matching(NSPredicate(format: "label BEGINSWITH %@", "Apartment number")).firstMatch
        XCTAssertFalse(existingApartment.exists,
                       "Preflight requires no apartment decodable under the selected test secret; stop before any token writes if any apartment is present.")

        // Isolate this run in a new local session, preserving the previous active session.
        let newSessionButton = app.buttons["new-charge-session"]
        XCTAssertTrue(newSessionButton.waitForExistence(timeout: 10))
        newSessionButton.tap()
        let confirmNewSession = app.buttons["confirm-new-charge-session"]
        XCTAssertTrue(confirmNewSession.waitForExistence(timeout: 10))
        confirmNewSession.tap()
        XCTAssertTrue(app.staticTexts["TOTAL · 0 charges"].waitForExistence(timeout: 10))

        // Discard the pending delta on the real iPad without issuing a charge.
        let tens = initial <= 470 ? 10 : -10
        let adjustTens = app.buttons["pending-rides-\(tens < 0 ? "minus" : "plus")-10"]
        for _ in 0..<3 { adjustTens.tap() }
        app.buttons["Clear pending change"].tap()
        XCTAssertEqual(rideValue(current), initial)
        XCTAssertTrue((app.descendants(matching: .any)["pending-rides-display"].value as? String)?.contains("No ride change") == true)
        XCTAssertTrue(app.staticTexts["TOTAL · 0 charges"].exists)
        attach(app, name: "clear-pending-without-charge")

        // Use a signed one-ride delta so detected count 500 remains in range.
        let delta = initial < 500 ? 1 : -1
        let adjust = app.buttons["pending-rides-\(delta < 0 ? "minus" : "plus")-1"]
        let charge = app.buttons["Charge token"]
        let apartmentInput = app.descendants(matching: .any)["apartment-number-input"]
        XCTAssertTrue(adjust.waitForExistence(timeout: 10))
        XCTAssertTrue(charge.waitForExistence(timeout: 10))

        // Open the first charge popup after the app is already landscape. Cancel remains
        // before any token write, so layout/hit-target failures are safe to stop on.
        setOrientation(.landscapeLeft, landscape: true)
        adjust.tap()
        charge.tap()
        XCTAssertTrue(apartmentInput.waitForExistence(timeout: 15), "Secret did not produce the missing-apartment prompt")
        attach(app, name: "physical-charge-landscape-first-open")
        assertApartmentPopupFits(footerIdentifiers: ["apartment-cancel", "apartment-skip", "apartment-save"])
        app.buttons["apartment-cancel"].tap()
        setOrientation(.portrait, landscape: false)
        XCTAssertEqual(rideValue(current), initial, "Apartment Cancel must leave rides unchanged")
        XCTAssertTrue(app.staticTexts["TOTAL · 0 charges"].exists, "Cancel must not create a completed charge")
        attach(app, name: "after-apartment-cancel")

        // Skip: charge rides only, preserving the missing apartment.
        charge.tap()
        XCTAssertTrue(app.buttons["apartment-skip"].waitForExistence(timeout: 15))
        attach(app, name: "apartment-skip-prompt")
        app.buttons["apartment-skip"].tap()
        XCTAssertTrue(waitForRideValue(current, initial + delta, timeout: 30))
        XCTAssertTrue(app.staticTexts["TOTAL · 1 charges"].waitForExistence(timeout: 15))

        // Save: apartment 42 and the second ride adjustment are written together.
        adjust.tap()
        charge.tap()
        XCTAssertTrue(apartmentInput.waitForExistence(timeout: 15))
        enterApartment(42)
        attach(app, name: "apartment-save-prompt")
        app.buttons["apartment-save"].tap()
        XCTAssertTrue(waitForRideValue(current, initial + delta * 2, timeout: 30))
        var apartmentButton = app.buttons.matching(NSPredicate(format: "label CONTAINS %@", "Apartment number 42")).firstMatch
        XCTAssertTrue(apartmentButton.waitForExistence(timeout: 20))
        XCTAssertTrue(app.staticTexts["TOTAL · 2 charges"].exists)

        // Edit existing 42 to 43 with a pending ride; open the edit popup only after
        // switching to landscape to exercise the same first-open orientation case.
        adjust.tap()
        setOrientation(.landscapeLeft, landscape: true)
        apartmentButton.tap()
        XCTAssertTrue(apartmentInput.waitForExistence(timeout: 10))
        attach(app, name: "physical-edit-landscape-first-open")
        assertApartmentPopupFits(footerIdentifiers: ["apartment-cancel", "apartment-save"])
        app.buttons["clear-apartment-number"].tap()
        app.buttons["apartment-key-4"].tap()
        app.buttons["apartment-key-3"].tap()
        attach(app, name: "apartment-edit-before-save")
        app.buttons["apartment-save"].tap()
        XCTAssertTrue(waitForRideValue(current, initial + delta * 2, timeout: 20), "Apartment-only edit changed current rides")
        setOrientation(.portrait, landscape: false)
        XCTAssertTrue(app.staticTexts["TOTAL · 2 charges"].exists, "Apartment-only edit added a charge")
        apartmentButton = app.buttons.matching(NSPredicate(format: "label CONTAINS %@", "Apartment number 43")).firstMatch
        XCTAssertTrue(apartmentButton.waitForExistence(timeout: 20))
        let pending = app.descendants(matching: .any)["pending-rides-display"]
        XCTAssertTrue((pending.value as? String)?.contains("\(initial + delta * 3)") == true,
                      "Apartment-only edit must preserve the pending ride adjustment")
        attach(app, name: "apartment-edit-after-save")
        XCUIDevice.shared.orientation = .landscapeLeft
        let landscape = XCTNSPredicateExpectation(predicate: NSPredicate { _, _ in self.app.frame.width > self.app.frame.height }, object: app)
        XCTAssertEqual(XCTWaiter.wait(for: [landscape], timeout: 15), .completed)
        attach(app, name: "physical-main-apartment-landscape")
        XCUIDevice.shared.orientation = .portrait
        let portrait = XCTNSPredicateExpectation(predicate: NSPredicate { _, _ in self.app.frame.height > self.app.frame.width }, object: app)
        XCTAssertEqual(XCTWaiter.wait(for: [portrait], timeout: 15), .completed)

        // Switch away and load this session before completing its pending third charge.
        app.buttons["new-charge-session"].tap()
        XCTAssertTrue(confirmNewSession.waitForExistence(timeout: 10))
        attach(app, name: "new-session-confirmation")
        confirmNewSession.tap()
        XCTAssertTrue(app.staticTexts["TOTAL · 0 charges"].waitForExistence(timeout: 15))
        app.buttons["load-charge-session"].tap()
        XCTAssertTrue(app.navigationBars["Load session"].waitForExistence(timeout: 10))
        let completedRun = app.buttons.matching(NSPredicate(format: "label CONTAINS %@", "2 charges")).firstMatch
        XCTAssertTrue(completedRun.waitForExistence(timeout: 10), "Completed acceptance session is not listed")
        attach(app, name: "load-acceptance-session")
        completedRun.tap()
        XCTAssertTrue(app.staticTexts["TOTAL · 2 charges"].waitForExistence(timeout: 15))
        charge.tap()
        XCTAssertTrue(waitForRideValue(current, initial + delta * 3, timeout: 30))
        XCTAssertTrue(app.staticTexts["TOTAL · 3 charges"].waitForExistence(timeout: 15))
        attach(app, name: "loaded-session-accepts-next-charge")

        app.terminate()
        launchPhysicalApp()
        let restartedCurrent = app.descendants(matching: .any).matching(identifier: "current-rides-display").firstMatch
        XCTAssertTrue(restartedCurrent.waitForExistence(timeout: 30))
        XCTAssertFalse(app.buttons.matching(NSPredicate(format: "label BEGINSWITH %@", "Apartment number")).firstMatch.exists,
                       "The apartment secret must not survive an app restart")
        saveSecretThroughConnectionSheet() // Secret is memory-only and must be re-entered after relaunch.
        XCTAssertTrue(app.staticTexts["TOTAL · 3 charges"].waitForExistence(timeout: 15))
        XCTAssertTrue(waitForRideValue(restartedCurrent, initial + delta * 3, timeout: 30))
        XCTAssertTrue(app.buttons.matching(NSPredicate(format: "label CONTAINS %@", "Apartment number 43")).firstMatch.exists)
        attach(app, name: "reopened-active-session")

        // Cross-profile reset to Venus then Mercury; apartment 43 must survive both.
        app.buttons["Reset token"].tap()
        XCTAssertTrue(app.buttons["reset-profile-venus"].waitForExistence(timeout: 10))
        XCTAssertFalse(app.buttons["Confirm reset"].isEnabled, "Reset must require explicit profile selection")
        app.buttons["reset-profile-venus"].tap()
        app.buttons["Confirm reset"].tap()
        XCTAssertTrue(app.buttons["Confirm reset"].waitForNonExistence(timeout: 30))
        XCTAssertTrue(waitForRideValue(restartedCurrent, 0, timeout: 30))
        XCTAssertTrue(app.buttons.matching(NSPredicate(format: "label CONTAINS %@", "Apartment number 43")).firstMatch.waitForExistence(timeout: 15))
        app.buttons["Reset token"].tap()
        XCTAssertTrue(app.buttons["reset-profile-mercury"].waitForExistence(timeout: 10))
        app.buttons["reset-profile-mercury"].tap()
        app.buttons["Confirm reset"].tap()
        XCTAssertTrue(app.buttons["Confirm reset"].waitForNonExistence(timeout: 30))
        XCTAssertTrue(waitForRideValue(restartedCurrent, 0, timeout: 30))
        XCTAssertTrue(app.buttons.matching(NSPredicate(format: "label CONTAINS %@", "Apartment number 43")).firstMatch.waitForExistence(timeout: 15))
        XCTAssertTrue(app.staticTexts["TOTAL · 3 charges"].exists, "Reset must preserve completed-charge history")
        attach(app, name: "after-cross-profile-reset")

        // Opaque modal keeps confirmation explicit and prevents accidental outside dismissal.
        let restart = app.buttons["restart-calibration"]
        XCTAssertTrue(restart.waitForExistence(timeout: 10))
        restart.tap()
        let confirmRestart = app.buttons.matching(NSPredicate(
            format: "identifier == %@", "confirm-restart-calibration"
        )).firstMatch
        XCTAssertTrue(confirmRestart.waitForExistence(timeout: 10))
        attach(app, name: "calibration-confirmation")
        app.buttons["cancel-confirmation"].tap()
        XCTAssertTrue(restartedCurrent.waitForExistence(timeout: 10), "Cancel must preserve the loaded token")
        XCTAssertTrue(app.staticTexts["TOTAL · 3 charges"].exists)
        restart.tap()
        XCTAssertTrue(confirmRestart.waitForExistence(timeout: 10))
        confirmRestart.tap()
        XCTAssertTrue(app.buttons["detect-primary-action"].waitForExistence(timeout: 10))
        XCTAssertEqual(app.buttons["detect-primary-action"].label, "Calibrate: NO token")
        XCTAssertTrue(app.staticTexts["TOTAL · 3 charges"].exists)
        attach(app, name: "after-calibration-restart")
        #endif
    }

    #if RIDES_PHYSICAL_UI_WRITES && !targetEnvironment(simulator)
    func testPhysicalReadOnlyFinalStateAndSecretFingerprint() throws {
        continueAfterFailure = false
        let runnerOptIn = ProcessInfo.processInfo.environment["TEST_RUNNER_RIDES_UI_PHYSICAL_TEST"]
            ?? ProcessInfo.processInfo.environment["RIDES_UI_PHYSICAL_TEST"]
        guard runnerOptIn == "1" else { throw XCTSkip("Set TEST_RUNNER_RIDES_UI_PHYSICAL_TEST=1 to opt in.") }
        XCTAssertTrue(secret == requiredPhysicalSecret, "Set the fresh physical-test secret through the test-runner environment.")

        // Orient before process launch so the history layout is selected on its first frame.
        XCUIDevice.shared.orientation = .landscapeLeft
        launchPhysicalApp()
        let current = app.descendants(matching: .any).matching(identifier: "current-rides-display").firstMatch
        XCTAssertTrue(current.waitForExistence(timeout: 30), "Real Detect did not publish a ride count")
        XCTAssertTrue(waitForLoadedRideValue(current, timeout: 30), "Real Detect did not publish a ride count")
        XCTAssertGreaterThan(app.frame.width, app.frame.height, "App must launch directly into landscape")
        saveSecretThroughConnectionSheet()
        XCTAssertEqual(rideValue(current), 0, "Fresh readback must show the displayed ride count at zero")
        XCTAssertTrue(app.buttons.matching(NSPredicate(format: "label CONTAINS %@", "Apartment number 43")).firstMatch.waitForExistence(timeout: 15),
                       "Fresh readback under the new secret must decode apartment 43")

        let historyViewport = app.scrollViews["completed-charge-history-scroll"]
        let eurHeader = app.staticTexts["history-column-5"]
        let eurCells = app.staticTexts.matching(NSPredicate(format: "identifier BEGINSWITH %@", "charge-eur-"))
        XCTAssertTrue(historyViewport.waitForExistence(timeout: 10), "Completed-charge history is missing")
        XCTAssertGreaterThanOrEqual(historyViewport.frame.width, 465,
                                    "Landscape history viewport must fit the minimum table width and padding")
        XCTAssertTrue(eurHeader.waitForExistence(timeout: 10), "EUR column header is missing")
        XCTAssertGreaterThan(eurCells.count, 0, "Expected saved charge EUR rows in physical history")
        let eurTargets = [eurHeader] + (0..<eurCells.count).map { eurCells.element(boundBy: $0) }
        for target in eurTargets {
            XCTAssertTrue(historyViewport.frame.contains(target.frame),
                          "EUR header/row must be wholly inside the history viewport: target=\(target.frame), viewport=\(historyViewport.frame)")
            XCTAssertTrue(target.isHittable, "EUR header/row must be hittable: \(target.identifier)")
        }
        attach(app, name: "physical-final-landscape-history-all-columns")
        attach(app, name: "physical-final-hardware-readback-0-apartment-43")

        app.buttons["Connection diagnostics"].tap()
        let fingerprint = app.staticTexts["apartmentSecretFingerprint"]
        XCTAssertTrue(fingerprint.waitForExistence(timeout: 10), "Saved secret fingerprint is not displayed")
        XCTAssertTrue(app.buttons["clearApartmentSecretButton"].exists)
        XCTAssertFalse(app.secureTextFields["Apartment secret"].exists)
        XCTAssertFalse(app.secureTextFields["Confirm apartment secret"].exists)
        XCTAssertFalse(app.buttons["saveApartmentSecretButton"].exists)
        attach(app, name: "physical-saved-secret-fingerprint-settings")
        app.buttons["Done"].tap()
    }

    private func launchPhysicalApp() {
        app = XCUIApplication()
        app.launchEnvironment["RIDES_UI_PHYSICAL_BOOTSTRAP"] = "1"
        app.launchEnvironment["RIDES_BRIDGE_ADDRESS_OVERRIDE"] = bridgeAddress
        app.launch()
    }

    private func saveSecretThroughConnectionSheet() {
        app.buttons["Connection diagnostics"].tap()
        let secretField = app.secureTextFields["Apartment secret"]
        let confirmField = app.secureTextFields["Confirm apartment secret"]
        XCTAssertTrue(secretField.waitForExistence(timeout: 10))
        secretField.tap()
        secretField.typeText(secret)
        confirmField.tap()
        confirmField.typeText(secret)
        attach(app, name: "physical-secret-password-fields")
        app.buttons["Save secret"].tap()
        app.buttons["Done"].tap()
        XCTAssertTrue(app.buttons["Charge token"].waitForExistence(timeout: 15))
        let ready = XCTNSPredicateExpectation(predicate: NSPredicate(format: "enabled == true"), object: app.buttons["new-charge-session"])
        XCTAssertEqual(XCTWaiter.wait(for: [ready], timeout: 30), .completed, "Apartment snapshot refresh did not finish")
    }

    private func setOrientation(_ orientation: UIDeviceOrientation, landscape: Bool) {
        XCUIDevice.shared.orientation = orientation
        let predicate = NSPredicate { _, _ in
            landscape ? self.app.frame.width > self.app.frame.height : self.app.frame.height > self.app.frame.width
        }
        let expectation = XCTNSPredicateExpectation(predicate: predicate, object: app)
        XCTAssertEqual(XCTWaiter.wait(for: [expectation], timeout: 15), .completed,
                       landscape ? "App did not enter landscape" : "App did not enter portrait")
    }

    private func assertApartmentPopupFits(footerIdentifiers: [String], file: StaticString = #filePath, line: UInt = #line) {
        XCTAssertTrue(app.frame.width > app.frame.height, "Popup screenshot must be landscape", file: file, line: line)
        let identifiers = (0...9).map { "apartment-key-\($0)" } + footerIdentifiers
        for identifier in identifiers {
            let button = app.buttons[identifier]
            XCTAssertTrue(button.waitForExistence(timeout: 5), "Missing popup target: \(identifier)", file: file, line: line)
            XCTAssertTrue(app.frame.contains(button.frame), "Popup target falls outside app bounds: \(identifier)", file: file, line: line)
            XCTAssertTrue(button.isHittable, "Popup target is not hittable: \(identifier)", file: file, line: line)
        }
    }

    private func enterApartment(_ number: Int) {
        for digit in String(number) { app.buttons["apartment-key-\(digit)"].tap() }
    }

    private func rideValue(_ element: XCUIElement) -> Int {
        Int((element.value as? String) ?? "") ?? -1
    }

    private func waitForRideValue(_ element: XCUIElement, _ expected: Int, timeout: TimeInterval) -> Bool {
        let predicate = NSPredicate { _, _ in self.rideValue(element) == expected }
        let expectation = XCTNSPredicateExpectation(predicate: predicate, object: element)
        return XCTWaiter.wait(for: [expectation], timeout: timeout) == .completed
    }

    private func waitForLoadedRideValue(_ element: XCUIElement, timeout: TimeInterval) -> Bool {
        let predicate = NSPredicate { _, _ in self.rideValue(element) >= 0 }
        let expectation = XCTNSPredicateExpectation(predicate: predicate, object: element)
        return XCTWaiter.wait(for: [expectation], timeout: timeout) == .completed
    }

    private func attach(_ app: XCUIApplication, name: String) {
        let attachment = XCTAttachment(screenshot: XCUIScreen.main.screenshot())
        attachment.name = name
        attachment.lifetime = .keepAlways
        add(attachment)
    }
    #endif
}
