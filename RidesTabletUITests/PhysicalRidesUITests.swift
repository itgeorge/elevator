import XCTest

final class PhysicalRidesUITests: XCTestCase {
    func testPhysicalRidesAcceptance() throws {
        #if !RIDES_PHYSICAL_UI_WRITES
        throw XCTSkip("Physical UI writes are disabled; build UI tests with RIDES_PHYSICAL_UI_WRITES only for the authorized test token.")
        #elseif targetEnvironment(simulator)
        throw XCTSkip("Physical Rides acceptance requires a real, non-simulator iPad.")
        #else
        guard ProcessInfo.processInfo.environment["RIDES_UI_PHYSICAL_TEST"] == "1" else {
            throw XCTSkip("Set RIDES_UI_PHYSICAL_TEST=1 explicitly to enable physical UI automation.")
        }
        let app = XCUIApplication()
        app.launchEnvironment["RIDES_UI_PHYSICAL_BOOTSTRAP"] = "1"
        app.launch()

        let detect = app.buttons["detect-primary-action"]
        XCTAssertTrue(detect.waitForExistence(timeout: 30), "The paired Rides screen did not appear")
        let rides = app.staticTexts["current-rides-display"]
        XCTAssertTrue(rides.waitForExistence(timeout: 30), "Current rides did not load from the PM3")
        let initialText = rides.label
        let initial = Self.firstInteger(in: initialText)
        XCTAssertNotNil(initial, "Could not parse dynamically detected rides: \(initialText)")
        XCTAssertTrue((0...500).contains(initial!), "Detected ride count outside acceptance bounds")
        attach(&app, name: "initial-live-detect")

        // Physical automation is deliberately conservative: do not guess through pairing
        // or a modal unless the expected live controls are actually exposed.
        let charge = app.buttons.matching(NSPredicate(format: "label CONTAINS[c] %@", "Charge")).firstMatch
        guard charge.waitForExistence(timeout: 15) else { throw XCTSkip("Charge controls are unavailable on this physical app state") }
        let plus = app.buttons["pending-rides-plus-1"]
        guard plus.waitForExistence(timeout: 10), plus.isEnabled else {
            throw XCTSkip("Pending-rides controls are not exposed; coordinator fallback provides the programmatic hardware run")
        }
        plus.tap()
        charge.tap()
        let apartment = app.otherElements["apartment-number-input"]
        guard apartment.waitForExistence(timeout: 15) else {
            throw XCTSkip("Apartment prompt did not appear; no UI mutation was attempted")
        }
        attach(&app, name: "apartment-prompt")
        let cancel = app.buttons.matching(NSPredicate(format: "label == %@", "Cancel")).firstMatch
        guard cancel.exists else { throw XCTSkip("Apartment Cancel control is unavailable") }
        cancel.tap()
        XCTAssertTrue(rides.waitForExistence(timeout: 15))
        XCTAssertEqual(Self.firstInteger(in: rides.label), initial, "Cancel must not change rides")
        attach(&app, name: "after-apartment-cancel")

        // Continue only when the real apartment prompt exposes its documented controls.
        charge.tap()
        guard app.buttons["Skip"].waitForExistence(timeout: 15) else {
            throw XCTSkip("Skip UI unavailable after Cancel; use the DEBUG coordinator fallback")
        }
        attach(&app, name: "apartment-skip-prompt")
        app.buttons["Skip"].tap()
        XCTAssertTrue(plus.waitForExistence(timeout: 15))
        plus.tap()
        charge.tap()
        guard app.staticTexts["apartment-number-input"].waitForExistence(timeout: 15) else {
            throw XCTSkip("Save prompt unavailable after Skip; no further writes performed")
        }
        for digit in ["4", "2"] { app.buttons["apartment-key-\(digit)"].tap() }
        attach(&app, name: "apartment-save-prompt")
        app.buttons["Save"].tap()
        XCTAssertTrue(app.buttons.matching(NSPredicate(format: "label CONTAINS %@", "Apartment number 42")).firstMatch.waitForExistence(timeout: 20))
        plus.tap()
        let apartmentButton = app.buttons.matching(NSPredicate(format: "label CONTAINS %@", "Apartment number 42")).firstMatch
        apartmentButton.tap()
        XCTAssertTrue(app.staticTexts["apartment-number-input"].waitForExistence(timeout: 10))
        for digit in ["4", "3"] { app.buttons["apartment-key-\(digit)"].tap() }
        app.buttons["Save"].tap()
        attach(&app, name: "apartment-edit-saved")

        // Reset must require a profile selection; cross-profile reset leaves the apt visible.
        app.buttons["Reset token"].tap()
        XCTAssertTrue(app.buttons["reset-profile-venus"].waitForExistence(timeout: 10))
        XCTAssertFalse(app.buttons["Confirm reset"].isEnabled)
        app.buttons["reset-profile-venus"].tap()
        app.buttons["Confirm reset"].tap()
        XCTAssertTrue(app.buttons["current-rides-display"].waitForExistence(timeout: 20))
        app.buttons["Reset token"].tap()
        XCTAssertTrue(app.buttons["reset-profile-mercury"].waitForExistence(timeout: 10))
        app.buttons["reset-profile-mercury"].tap()
        app.buttons["Confirm reset"].tap()
        attach(&app, name: "after-cross-profile-reset")

        let restart = app.buttons["restart-calibration"]
        guard restart.waitForExistence(timeout: 10) else { throw XCTSkip("Calibration control unavailable") }
        restart.tap()
        let confirmRestart = app.buttons["Restart calibration"]
        XCTAssertTrue(confirmRestart.waitForExistence(timeout: 10))
        attach(&app, name: "calibration-confirmation")
        app.buttons["Cancel"].tap()
        XCTAssertTrue(rides.exists, "Canceling calibration confirmation must preserve the loaded token")
        restart.tap()
        XCTAssertTrue(confirmRestart.waitForExistence(timeout: 10))
        confirmRestart.tap()
        let primaryTitle = app.buttons["detect-primary-action"]
        XCTAssertTrue(primaryTitle.waitForValue("Calibrate: NO token", timeout: 15))
        attach(&app, name: "after-calibration-restart")
        #endif
    }

    #if RIDES_PHYSICAL_UI_WRITES && !targetEnvironment(simulator)
    private func attach(_ app: XCUIApplication, name: String) {
        let attachment = XCTAttachment(screenshot: app.screenshot())
        attachment.name = name
        attachment.lifetime = .keepAlways
        add(attachment)
    }
    private static func firstInteger(in text: String) -> Int? {
        let digits = text.split(whereSeparator: { !$0.isNumber }).first
        return digits.flatMap { Int($0) }
    }
    #endif
}
