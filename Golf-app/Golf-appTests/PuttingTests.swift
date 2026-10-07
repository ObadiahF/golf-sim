import Foundation
import Testing
@testable import Golf_app

/// The putt distance mapping (must match the sim's PuttModel.cs) and the putting view's state switching.
struct PuttModelTests {
    private let putter = Club.putter

    @Test func stimpIsFeetRolledAtTheStimpmeterSpeed() {
        let rolled = PuttModel.rollDistance(ballSpeed: PuttModel.stimpReleaseSpeed, stimp: 11.2)
        #expect(abs(rolled - 11.2 * 0.3048) < 1e-9)
    }

    @Test func defaultStimpIsTheSimsGreen() {
        // The sim's green slows the ball at rolling 0.06 x g: Stimp = 1.83² / (2 x 0.06 x 9.81) feet.
        let simGreen = 1.83 * 1.83 / (2 * 0.06 * 9.81) / 0.3048
        #expect(abs(simGreen - PuttModel.defaultStimp) < 0.05)
    }

    @Test func distanceGrowsWithSpeedSquared() {
        let one = PuttModel.rollDistance(ballSpeed: 1.5, stimp: 11)
        let two = PuttModel.rollDistance(ballSpeed: 3.0, stimp: 11)
        #expect(abs(two / one - 4) < 1e-9)
        #expect(PuttModel.rollDistance(ballSpeed: -1, stimp: 11) == 0)
    }

    @Test(arguments: [0.5, 2.0, 5.0, 12.0])
    func speedForDistanceRoundTrips(distance: Double) {
        for stimp in [8.0, 11.2, 13.0] {
            let speed = PuttModel.ballSpeed(forDistance: distance, stimp: stimp)
            #expect(abs(PuttModel.rollDistance(ballSpeed: speed, stimp: stimp) - distance) < 1e-9)
        }
    }

    @Test func fasterGreensRollFarther() {
        #expect(PuttModel.rollDistance(ballSpeed: 2, stimp: 13) > PuttModel.rollDistance(ballSpeed: 2, stimp: 9))
    }

    @Test func gentleAndFirmStrokesAreControllable() {
        // A gentle stroke (~1.1 rad/s at the phone) is a couple of metres; a firm one (~2.2 rad/s) 8-9 m.
        let gentle = PuttModel.rollDistance(rate: 1.1, scale: 1, stimp: PuttModel.defaultStimp)
        let firm = PuttModel.rollDistance(rate: 2.2, scale: 1, stimp: PuttModel.defaultStimp)
        #expect((2...3).contains(gentle))
        #expect((8...10).contains(firm))
    }

    @Test func meterAgreesWithTheShotSent() {
        let impact = Impact(rate: 1.6, face: 0, angle: 0, time: 0)
        let shot = Shot.from(impact, club: putter, scale: 1.2, faceSign: 1)
        let meter = PuttModel.rollDistance(rate: 1.6, scale: 1.2, stimp: 11.2)
        #expect(abs(PuttModel.rollDistance(ballSpeed: shot.ballSpeed, stimp: 11.2) - meter) < 1e-9)
        #expect(abs(shot.ballSpeed - 1.6 * putter.radius * 1.2 * putter.smash) < 1e-9)
    }

    @Test func stimpComesFromTheSimWhenItSaysSo() {
        #expect(PuttModel.stimp(of: nil) == PuttModel.defaultStimp)
        #expect(PuttModel.stimp(of: GameProtocol.SimState(screen: "game", stimp: 0)) == PuttModel.defaultStimp)
        #expect(PuttModel.stimp(of: GameProtocol.SimState(screen: "game", stimp: 9.5)) == 9.5)
    }

    @Test func gentleOneMetrePuttFiresThePutter() {
        // 0.75 rad/s at impact rolls about a metre: it must clear the putter's start and peak thresholds.
        let samples = SyntheticSwing.samples(.putt(impactRate: 0.75), noise: 0.02)
        var detector = SwingDetector(thresholds: .putter)
        let fired = samples.compactMap { sample -> Impact? in
            if case .fired(let impact) = detector.process(sample) { return impact }
            return nil
        }
        #expect(fired.count == 1)
        let rolled = PuttModel.rollDistance(rate: fired.first?.rate ?? 0, scale: 1, stimp: PuttModel.defaultStimp)
        #expect((0.6...1.5).contains(rolled))
    }
}

@MainActor
struct PuttingStateTests {
    private func state(_ json: String) throws -> GameProtocol.SimState {
        guard case .state(let state)? = GameProtocol.decode(Data(json.utf8)) else { throw CancellationError() }
        return state
    }

    @Test func menuIsTheRemote() throws {
        #expect(PlayScreen.of(nil) == .remote)
        #expect(PlayScreen.of(try state(#"{"type":"state","screen":"menu"}"#)) == .remote)
    }

    @Test func olderSimsWithoutPuttingFieldsShowGameplay() throws {
        let s = try state(#"{"type":"state","screen":"game","club":"Putter","distanceToPin":5,"lie":"green"}"#)
        #expect(s.putting == nil)
        #expect(PlayScreen.of(s) == .gameplay)
    }

    @Test func unityStateOutsidePuttingShowsGameplay() throws {
        // JsonUtility writes every field: false / 0 / "" when not putting.
        let s = try state(#"{"type":"state","screen":"game","club":"7 Iron","putting":false,"puttDistance":0.0,"elevation":0.0,"stimp":0.0,"puttPlaysAs":0.0,"puttingAssist":"partial"}"#)
        #expect(PlayScreen.of(s) == .gameplay)
    }

    @Test func puttingStateShowsThePuttingView() throws {
        let s = try state(#"{"type":"state","screen":"game","club":"Putter","lie":"green","putting":true,"puttDistance":5.03,"elevation":-0.12,"stimp":11.2,"puttPlaysAs":5.21,"puttingAssist":"full"}"#)
        #expect(PlayScreen.of(s) == .putting)
        #expect(s.puttDistance == 5.03)
        #expect(s.elevation == -0.12)
        #expect(s.puttPlaysAs == 5.21)
        #expect(s.puttingAssist == "full")
        #expect(PuttCard.slope(s.elevation) == "12 cm downhill")
    }

    @Test func pausedWhilePuttingIsTheRemote() throws {
        #expect(PlayScreen.of(try state(#"{"type":"state","screen":"paused","putting":true}"#)) == .remote)
    }

    @Test func linkFollowsTheSimIntoAndOutOfPutting() throws {
        let link = GameLink(settings: try testSettings("PuttingStateTests"))
        link.handle(.state(try state(#"{"type":"state","screen":"game","putting":true,"puttDistance":3}"#)))
        #expect(PlayScreen.of(link.state) == .putting)
        link.handle(.state(try state(#"{"type":"state","screen":"game","putting":false}"#)))
        #expect(PlayScreen.of(link.state) == .gameplay)
        link.handle(.simStatus(connected: false))
        #expect(PlayScreen.of(link.state) == .remote)
    }

    @Test func slopeText() {
        #expect(PuttCard.slope(0.004) == "Flat")
        #expect(PuttCard.slope(0.25) == "25 cm uphill")
        #expect(PuttCard.slope(nil) == "–")
    }
}

struct PuttMeterTests {
    @Test func fillsLiveThenFreezesOnTheStrike() {
        var meter = PuttMeter()
        meter.begin()
        meter.track(distance: 1.2)
        meter.track(distance: 3.4)
        meter.track(distance: 2.0)
        #expect(meter.shown == 2.0)
        #expect(meter.peak == 3.4)
        meter.strike(distance: 3.1, toHole: 5.0)
        #expect(meter.shown == 3.1)
        meter.begin()
        #expect(meter.shown == 0)
        #expect(meter.struck == nil)
    }

    @Test func resultSaysHowFarOfHowFar() {
        var meter = PuttMeter()
        meter.strike(distance: 4.5, toHole: 5.0)
        meter.finish(GameProtocol.ShotResult(player: "Obi", carry: 0.1, total: 4.2 / PuttModel.metersPerYard, lie: "green", holed: false, strokes: 2))
        #expect(meter.result?.summary == "Putted 4.2 m of 5.0 m")
    }

    @Test func holedPutt() {
        var meter = PuttMeter()
        meter.strike(distance: 5.4, toHole: 5.0)
        meter.finish(GameProtocol.ShotResult(player: "Obi", carry: 0, total: 5.5, lie: "holed", holed: true, strokes: 3))
        #expect(meter.result?.summary == "Holed from 5.0 m!")
    }

    @Test func resultsWithoutAPuttAreIgnored() {
        var meter = PuttMeter()
        meter.finish(GameProtocol.ShotResult(player: "Obi", total: 150, lie: "fairway"))
        #expect(meter.result == nil)
        meter.strike(distance: 2, toHole: nil) // not in putting mode: no distance to compare with
        meter.finish(GameProtocol.ShotResult(player: "Obi", total: 2, lie: "green"))
        #expect(meter.result == nil)
    }

    @Test func rangeLeavesRoomAboveTheTarget() {
        #expect(PuttMeter.range(target: nil, distance: nil) == 3)
        #expect(PuttMeter.range(target: 6, distance: 5) == 9)
        #expect(PuttMeter.range(target: 5.4, distance: 5.03) == 9) // 8.1 up to the next tick
        #expect(PuttMeter.range(target: 9, distance: 8) == 14) // 13.5: two-metre ticks
    }

    private func putting(_ player: String, strokes: Int, distance: Double, playsAs: Double) -> GameProtocol.SimState? {
        let json = #"{"type":"state","screen":"game","canShoot":true,"currentPlayer":"\#(player)","hole":3,"strokes":\#(strokes),"club":"Putter","putting":true,"puttDistance":\#(distance),"puttPlaysAs":\#(playsAs)}"#
        guard case .state(let state)? = GameProtocol.decode(Data(json.utf8)) else { return nil }
        return state
    }

    @Test func rangeIsSetPerPuttNotPerAim() {
        var meter = PuttMeter()
        meter.follow(putting("Ann", strokes: 1, distance: 5.03, playsAs: 5.4))
        #expect(meter.range == 9)
        meter.follow(putting("Ann", strokes: 1, distance: 5.03, playsAs: 6.9)) // aimed: the target moves, not the scale
        meter.follow(putting("Ann", strokes: 1, distance: 5.03, playsAs: 3.1))
        #expect(meter.range == 9)
        meter.follow(putting("Bob", strokes: 2, distance: 1.2, playsAs: 1.5)) // the next putt
        #expect(meter.range == 3)
        meter.follow(GameProtocol.SimState(screen: "game", hole: 3, putting: false))
        meter.follow(putting("Bob", strokes: 2, distance: 7.6, playsAs: 8)) // back on the green
        #expect(meter.range == 12)
    }
}

@MainActor
struct PuttScaleSettingTests {
    @Test func puttScaleIsSeparateAndPersisted() throws {
        let settings = try testSettings("PuttScaleSettingTests")
        #expect(settings.puttScale == 1)
        settings.puttScale = 1.35
        settings.scale = 2
        #expect(settings.scale(for: .putter) == 1.35)
        #expect(settings.scale(for: Club.bag[0]) == 2)
        let defaults = try #require(UserDefaults(suiteName: "PuttScaleSettingTests"))
        #expect(AppSettings(defaults: defaults).puttScale == 1.35)
    }

    @Test func outOfRangePuttScaleFallsBackToOne() throws {
        let defaults = try #require(UserDefaults(suiteName: "PuttScaleSettingTests.bad"))
        defaults.set(9.0, forKey: "puttScale")
        #expect(AppSettings(defaults: defaults).puttScale == 1)
        defaults.removePersistentDomain(forName: "PuttScaleSettingTests.bad")
    }
}
