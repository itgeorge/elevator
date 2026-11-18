import XCTest

final class ApartmentValidationUITests: XCTestCase {
    func testNumberDisplayStartsEmptyAndShowsRangeOnlyForInvalidEntry() throws {
#if !targetEnvironment(simulator)
        throw XCTSkip("This keypad validation check uses the simulator fake reader.")
#else
        continueAfterFailure = false
        let app = XCUIApplication()
        app.launchEnvironment["RIDES_SIMULATOR_FAKE_READER"] = "1"
        app.launchEnvironment["RIDES_SCREENSHOT_SCENE"] = "apartment-charge"
        app.launch()
        defer { app.terminate() }
        let number = app.staticTexts["apartment-number-input"]
        let error = app.staticTexts["apartment-validation-error"]
        XCTAssertTrue(number.waitForExistence(timeout: 20))
        XCTAssertEqual(number.value as? String, "Empty")
        XCTAssertFalse(app.staticTexts["Enter 0–255"].exists)
        XCTAssertFalse(error.exists)
        XCTAssertFalse(app.buttons["Save"].isEnabled)
        capture(app, "apartment-empty-input")

        for digit in ["2", "5", "6"] { app.buttons["apartment-key-\(digit)"].tap() }
        XCTAssertEqual(number.value as? String, "256")
        XCTAssertTrue(error.waitForExistence(timeout: 5))
        XCTAssertFalse(app.buttons["Save"].isEnabled)
        capture(app, "apartment-range-validation")

        app.buttons["Backspace"].tap()
        XCTAssertEqual(number.value as? String, "25")
        XCTAssertFalse(error.exists)
        XCTAssertTrue(app.buttons["Save"].isEnabled)
        app.buttons["Cancel"].tap()
#endif
    }

    private func capture(_ app: XCUIApplication, _ name: String) {
        let attachment = XCTAttachment(screenshot: app.screenshot())
        attachment.name = name
        attachment.lifetime = .keepAlways
        add(attachment)
    }
}
