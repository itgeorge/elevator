import XCTest

final class ApartmentValidationUITests: XCTestCase {
    func testNumberDisplayStartsEmptyClearAndFooterMatchesKeypad() throws {
#if !targetEnvironment(simulator)
        throw XCTSkip("This keypad validation check uses the simulator fake reader.")
#else
        continueAfterFailure = false
        let app = launch(scene: "apartment-charge")
        defer { app.terminate() }
        let number = app.staticTexts["apartment-number-input"]
        let error = app.staticTexts["apartment-validation-error"]
        XCTAssertTrue(number.waitForExistence(timeout: 20))
        XCTAssertEqual(number.value as? String, "Empty")
        XCTAssertFalse(error.exists)
        XCTAssertFalse(app.buttons["apartment-save"].isEnabled)
        XCTAssertFalse(app.buttons["clear-apartment-number"].exists)
        XCTAssertFalse(app.buttons["Backspace"].exists)
        XCTAssertTrue(app.buttons["apartment-cancel"].exists)
        XCTAssertTrue(app.buttons["apartment-skip"].exists)
        XCTAssertTrue(app.buttons["apartment-key-0"].exists)
        capture(app, "apartment-empty-input")

        for digit in ["2", "5", "6"] { app.buttons["apartment-key-\(digit)"].tap() }
        XCTAssertEqual(number.value as? String, "256")
        XCTAssertTrue(error.waitForExistence(timeout: 5))
        XCTAssertFalse(app.buttons["apartment-save"].isEnabled)
        capture(app, "apartment-range-validation")

        app.buttons["clear-apartment-number"].tap()
        XCTAssertEqual(number.value as? String, "Empty")
        XCTAssertFalse(error.exists)
        XCTAssertFalse(app.buttons["clear-apartment-number"].exists)
        for digit in ["2", "5"] { app.buttons["apartment-key-\(digit)"].tap() }
        XCTAssertTrue(app.buttons["apartment-save"].isEnabled)
        app.buttons["apartment-cancel"].tap()
#endif
    }

    func testLandscapeFirstAndRotateOpenChargeAndEditKeypads() throws {
#if !targetEnvironment(simulator)
        throw XCTSkip("Landscape keypad layout uses the simulator fake reader.")
#else
        continueAfterFailure = false
        let app = launch(scene: "apartment-charge", orientation: .landscapeLeft)
        defer { app.terminate(); XCUIDevice.shared.orientation = .portrait }
        assertKeypadVisible(app)
        capture(app, "apartment-charge-landscape-first")
        let screen = app.windows.firstMatch.frame
        XCTAssertGreaterThan(screen.width, screen.height)
        XCUIDevice.shared.orientation = .portrait
        XCTAssertTrue(app.staticTexts["apartment-number-input"].waitForExistence(timeout: 5))
        assertKeypadVisible(app)
        capture(app, "apartment-charge-portrait-open-rotated")
        XCUIDevice.shared.orientation = .landscapeLeft
        assertKeypadVisible(app)

        app.terminate()
        XCUIDevice.shared.orientation = .portrait
        let edit = launch(scene: "apartment-edit")
        XCTAssertTrue(edit.staticTexts["apartment-number-input"].waitForExistence(timeout: 15))
        capture(edit, "apartment-edit-portrait-open")
        XCUIDevice.shared.orientation = .landscapeLeft
        assertKeypadVisible(edit)
        capture(edit, "apartment-edit-portrait-open-rotated")
        XCTAssertFalse(edit.buttons["apartment-skip"].exists)
        edit.buttons["apartment-cancel"].tap()
        edit.terminate()
        XCUIDevice.shared.orientation = .landscapeLeft
        let landscapeEdit = launch(scene: "apartment-edit", orientation: .landscapeLeft)
        defer { landscapeEdit.terminate() }
        assertKeypadVisible(landscapeEdit)
        capture(landscapeEdit, "apartment-edit-landscape-first")
        XCTAssertFalse(landscapeEdit.buttons["apartment-skip"].exists)
#endif
    }

    func testOutsideTapCancelsEditButSkipsCharge() throws {
#if !targetEnvironment(simulator)
        throw XCTSkip("Outside-dismiss keypad behavior uses the simulator fake reader.")
#else
        continueAfterFailure = false
        let edit = launch(scene: "apartment-edit")
        defer { edit.terminate(); XCUIDevice.shared.orientation = .portrait }
        XCTAssertTrue(edit.staticTexts["apartment-number-input"].waitForExistence(timeout: 15))
        edit.buttons["apartment-entry-backdrop"].coordinate(withNormalizedOffset: CGVector(dx: 0.02, dy: 0.5)).tap()
        XCTAssertTrue(edit.staticTexts["apartment-number-input"].waitForNonExistence(timeout: 10))
        XCTAssertTrue(edit.staticTexts["TOTAL · 0 charges"].exists, "Outside edit dismissal must not charge")

        let charge = launch(scene: "apartment-charge")
        defer { charge.terminate() }
        XCTAssertTrue(charge.staticTexts["apartment-number-input"].waitForExistence(timeout: 15))
        charge.buttons["apartment-entry-backdrop"].coordinate(withNormalizedOffset: CGVector(dx: 0.02, dy: 0.5)).tap()
        XCTAssertTrue(charge.staticTexts["apartment-number-input"].waitForNonExistence(timeout: 20))
        XCTAssertTrue(charge.staticTexts["TOTAL · 1 charges"].waitForExistence(timeout: 20), "Outside charge dismissal must perform Skip exactly once")
#endif
    }

    func testNewSessionDisabledWhenHistoryIsEmptyAndConfirmationsAreOpaque() throws {
#if !targetEnvironment(simulator)
        throw XCTSkip("Modal confirmation checks use the simulator fake reader.")
#else
        continueAfterFailure = false
        let app = launch(scene: "known")
        defer { app.terminate() }
        let newSession = app.buttons["new-charge-session"]
        XCTAssertTrue(newSession.waitForExistence(timeout: 15))
        XCTAssertFalse(newSession.isEnabled)

        let history = launch(scene: "apartment-history")
        defer { history.terminate() }
        XCTAssertTrue(history.staticTexts["Apt #"].waitForExistence(timeout: 15))
        let chargedApartment = history.staticTexts.matching(NSPredicate(
            format: "identifier BEGINSWITH %@ AND label == %@", "charge-apartment-", "52"
        )).firstMatch
        XCTAssertTrue(chargedApartment.exists, "History must show the apartment captured with that charge")
        let missingApartment = history.staticTexts.matching(NSPredicate(
            format: "identifier BEGINSWITH %@ AND label == %@", "charge-apartment-", "—"
        )).firstMatch
        XCTAssertTrue(missingApartment.exists, "Historical charges without an apartment render an em dash")
        history.buttons["new-charge-session"].tap()
        let confirm = history.buttons["confirm-new-charge-session"]
        XCTAssertTrue(confirm.waitForExistence(timeout: 5))
        XCTAssertTrue(history.buttons["cancel-confirmation"].exists)
        XCTAssertFalse(history.staticTexts["Completed charges"].isHittable)
        history.buttons["cancel-confirmation"].tap()

        history.buttons["restart-calibration"].tap()
        XCTAssertTrue(history.buttons["confirm-restart-calibration"].waitForExistence(timeout: 5))
        XCTAssertTrue(history.buttons["cancel-confirmation"].exists)
#endif
    }

    private func launch(scene: String, orientation: UIDeviceOrientation = .portrait) -> XCUIApplication {
        XCUIDevice.shared.orientation = orientation
        let app = XCUIApplication()
        app.launchEnvironment["RIDES_SIMULATOR_FAKE_READER"] = "1"
        app.launchEnvironment["RIDES_SCREENSHOT_SCENE"] = scene
        app.launch()
        return app
    }

    private func assertKeypadVisible(_ app: XCUIApplication, file: StaticString = #filePath, line: UInt = #line) {
        XCTAssertTrue(app.staticTexts["apartment-number-input"].waitForExistence(timeout: 10), file: file, line: line)
        for identifier in ["apartment-key-0", "apartment-cancel", "apartment-save"] {
            XCTAssertTrue(app.buttons[identifier].isHittable, "\(identifier) must fit and be tappable", file: file, line: line)
        }
        XCTAssertTrue(app.buttons["apartment-entry-backdrop"].exists, file: file, line: line)
    }

    private func capture(_ app: XCUIApplication, _ name: String) {
        let attachment = XCTAttachment(screenshot: app.screenshot())
        attachment.name = name
        attachment.lifetime = .keepAlways
        add(attachment)
    }
}
