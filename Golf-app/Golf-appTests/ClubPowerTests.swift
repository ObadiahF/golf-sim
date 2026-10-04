import Foundation
import Testing
@testable import Golf_app

/// Per-club power: a percentage on top of the swing scale, stored on the phone.
@MainActor
struct ClubPowerTests {
    private let impact = Impact(rate: 20, face: 0, angle: 90, time: 0)
    private var wedge: Club { Club.bag.first { $0.name == "Pitching Wedge" }! }
    private var driver: Club { Club.bag[0] }

    @Test func defaultsToFullPowerAndPersists() throws {
        let settings = try testSettings("ClubPowerTests")
        let powers = AppSettings.poweredClubs.map { settings.power(for: $0) }
        #expect(powers == Array(repeating: 1, count: Club.bag.count - 1))
        #expect(AppSettings.poweredClubs.map(\.name) == ["Driver", "3 Wood", "5 Wood", "4 Hybrid", "5 Iron", "6 Iron", "7 Iron", "8 Iron",
                                                         "9 Iron", "Pitching Wedge", "Gap Wedge", "Sand Wedge", "Lob Wedge"])
        settings.setPower(0.8, for: wedge)
        let defaults = try #require(UserDefaults(suiteName: "ClubPowerTests"))
        let reloaded = AppSettings(defaults: defaults)
        #expect(abs(reloaded.power(for: wedge) - 0.8) < 1e-9)
        #expect(reloaded.power(for: driver) == 1)
        reloaded.resetClubPower()
        #expect(AppSettings(defaults: defaults).power(for: wedge) == 1)
    }

    @Test func powerIsClampedSteppedAndNotForThePutter() throws {
        let settings = try testSettings("ClubPowerTests.clamp")
        settings.setPower(3, for: wedge)
        #expect(settings.power(for: wedge) == 1.5)
        settings.setPower(0.1, for: wedge)
        #expect(settings.power(for: wedge) == 0.5)
        settings.setPower(0.83, for: wedge)
        #expect(abs(settings.power(for: wedge) - 0.85) < 1e-9)
        settings.setPower(0.5, for: .putter)
        #expect(settings.power(for: .putter) == 1)
        let defaults = try #require(UserDefaults(suiteName: "ClubPowerTests.bad"))
        defaults.set(["Wedge": 9.0, "Driver": 0.9], forKey: "clubPower")
        let loaded = AppSettings(defaults: defaults)
        #expect(loaded.power(for: wedge) == 1 && loaded.power(for: driver) == 0.9)
        defaults.removePersistentDomain(forName: "ClubPowerTests.bad")
    }

    @Test func settingsSavedForTheOldWedgeMoveToThePitchingWedge() throws {
        let defaults = try #require(UserDefaults(suiteName: "ClubPowerTests.renamed"))
        defer { defaults.removePersistentDomain(forName: "ClubPowerTests.renamed") }
        defaults.set(["Wedge": 0.8], forKey: "clubPower")
        defaults.set(["Wedge": 1.5], forKey: "clubSensitivity")
        let loaded = AppSettings(defaults: defaults)
        #expect(abs(loaded.power(for: wedge) - 0.8) < 1e-9)
        #expect(loaded.sensitivity(for: wedge) == 1.5)
        #expect(Club.index(named: "Wedge") == Club.index(named: "Pitching Wedge"))
    }

    @Test func powerScalesThatClubsBallSpeedOnly() throws {
        let settings = try testSettings("ClubPowerTests.speed")
        settings.scale = 1.5
        settings.puttScale = 1.2
        func speed(_ club: Club) -> Double { Shot.from(impact, club: club, scale: settings.scale(for: club), faceSign: 0).ballSpeed }
        let before = Club.bag.map(speed)
        settings.setPower(0.8, for: wedge)
        let after = Club.bag.map(speed)
        for (index, club) in Club.bag.enumerated() {
            let expected = club == wedge ? before[index] * 0.8 : before[index]
            #expect(abs(after[index] - expected) < 1e-9, "\(club.name)")
        }
        #expect(settings.scale(for: wedge) == 1.5 * 0.8)
        #expect(settings.scale(for: .putter) == 1.2) // the putter keeps the putt scale
    }
}

@MainActor
struct ClubSensitivityTests {
    @Test func sensitivityIsStoredSteppedAndScalesDetection() throws {
        let settings = try testSettings("ClubSensitivityTests")
        #expect(settings.detection(for: .putter) == SwingThresholds.putter)
        settings.setSensitivity(1.53, for: .putter)
        #expect(abs(settings.sensitivity(for: .putter) - 1.5) < 1e-9)
        settings.setSensitivity(9, for: .putter)
        #expect(settings.sensitivity(for: .putter) == AppSettings.sensitivityRange.upperBound)
        #expect(abs(settings.detection(for: .putter).startRate - SwingThresholds.putter.startRate / 2) < 1e-9)
        #expect(settings.detection(for: .putter).stillRate == SwingThresholds.putter.stillRate)
        #expect(settings.detection(for: Club.bag[0]) == Club.bag[0].detection) // other clubs untouched
        let reloaded = AppSettings(defaults: try #require(UserDefaults(suiteName: "ClubSensitivityTests")))
        #expect(reloaded.sensitivity(for: .putter) == AppSettings.sensitivityRange.upperBound)
        reloaded.resetSensitivity()
        #expect(reloaded.sensitivity(for: .putter) == 1)
    }
}
