import Foundation
import SwiftUI
import Testing
@testable import Golf_app

/// Round 4 (end-to-end QA): the Replay button between turns (E-6), the putting meter starting from empty
/// (E-9), best rounds listed per round length (E-10) and the control sizes that fit the screen (E-7).
@MainActor
struct EndToEndFixTests {
    private func state(_ json: String) throws -> GameProtocol.SimState {
        guard case .state(let state)? = GameProtocol.decode(Data(json.utf8)) else { throw CancellationError() }
        return state
    }

    // MARK: E-6 Replay between turns

    @Test func replayIsOfferedBetweenTurns() throws {
        let between = try state(#"{"type":"state","screen":"game","canShoot":false,"waitReason":"Wait for the next turn"}"#)
        #expect(between.offersReplay)
        // Not while the ball moves, the shot can be hit, or on any other screen.
        #expect(!(try state(#"{"type":"state","screen":"game","canShoot":false,"waitReason":"Wait for the ball to stop"}"#)).offersReplay)
        #expect(!(try state(#"{"type":"state","screen":"game","canShoot":true,"waitReason":""}"#)).offersReplay)
        #expect(!(try state(#"{"type":"state","screen":"game"}"#)).offersReplay) // older sims
        #expect(!(try state(#"{"type":"state","screen":"replay","canShoot":false,"waitReason":"Wait for the next turn"}"#)).offersReplay)
        #expect(!(try state(#"{"type":"state","screen":"holeComplete","canShoot":false}"#)).offersReplay) // the D-pad's Up there
    }

    @Test func canReplayFromTheSimWinsOverTheGuess() throws {
        #expect(try state(#"{"type":"state","screen":"game","canShoot":true,"canReplay":true}"#).offersReplay)
        #expect(!(try state(#"{"type":"state","screen":"game","canShoot":false,"waitReason":"Wait for the next turn","canReplay":false}"#)).offersReplay)
        #expect(!(try state(#"{"type":"state","screen":"menu","canReplay":true}"#)).offersReplay)
    }

    @Test func replayButtonNeedsTheSim() throws {
        let link = GameLink(settings: try testSettings("EndToEndFixTests.replay"), deviceName: "test")
        let between = try state(#"{"type":"state","screen":"game","canShoot":false,"waitReason":"Wait for the next turn"}"#)
        link.handle(.state(between))
        #expect(!link.offersReplay, "not connected")
        link.handle(.hello(.init(simConnected: true, remotes: [], game: nil, state: between)))
        #expect(link.offersReplay)
        link.handle(.simStatus(connected: false))
        #expect(!link.offersReplay)
    }

    // MARK: E-9 The putting meter

    private static let putting = GameProtocol.SimState(screen: "game", hole: 3, putting: true, puttDistance: 5)

    @Test func resetEmptiesTheMeterButKeepsAWaitingPutt() {
        var meter = PuttMeter()
        meter.strike(distance: 6.4, toHole: 5)
        meter.reset()
        #expect(meter.shown == 0 && meter.peak == 0 && meter.struck == nil)
        meter.finish(GameProtocol.ShotResult(player: "Obi", total: 4 / PuttModel.metersPerYard, lie: "green"))
        #expect(meter.result?.summary == "Putted 4.0 m of 5.0 m", "the putt's result still arrives")
        meter.reset()
        #expect(meter.result != nil, "a new turn keeps the last putt's result")
        meter.reset(clearingResult: true)
        #expect(meter.result == nil)
    }

    @Test func aNewHoleClearsTheMeterAndTheResult() {
        var meter = PuttMeter()
        meter.follow(Self.putting)
        meter.strike(distance: 6.4, toHole: 5)
        meter.finish(GameProtocol.ShotResult(player: "Obi", total: 5, lie: "green"))
        meter.follow(Self.putting) // the same hole again: nothing changes
        #expect(meter.shown == 6.4 && meter.result != nil)
        var next = Self.putting
        next.hole = 4
        meter.follow(next)
        #expect(meter.shown == 0 && meter.result == nil)
    }

    @Test func leavingThePuttingViewClearsTheMeter() {
        var meter = PuttMeter()
        meter.follow(Self.putting)
        meter.strike(distance: 6.4, toHole: 5)
        meter.follow(GameProtocol.SimState(screen: "replay", hole: 3))
        #expect(meter.shown == 0)
        meter.follow(Self.putting)
        meter.strike(distance: 2, toHole: 5)
        meter.follow(nil) // the sim went away
        #expect(meter.shown == 0)
    }

    @Test func theSessionEmptiesTheMeterOnANewTurn() throws {
        let motion = ShotReadinessTests.ManualMotion()
        let session = SwingSession(settings: try testSettings("EndToEndFixTests.turn"), motion: motion)
        session.game.handle(.hello(.init(simConnected: true, remotes: ["test"], game: nil, state: Self.putting)))
        session.selectClub(Club.bag.firstIndex { $0.isPutter } ?? 0)
        session.address()
        motion.feed(stride(from: 0.0, through: 2.0, by: 0.01).map { MotionSample(time: $0, rate: 0.02, angle: 0.1, face: 0) })
        motion.feed(SyntheticSwing.samples(.putt(impactRate: 2, face: 0), start: 2.1))
        #expect(session.meter.shown > 0, "the putt was struck")
        session.game.handle(.turn(.init(player: "Obi", hole: 3, strokes: 2)))
        #expect(session.meter.shown == 0)
    }

    // MARK: E-10 Best rounds per round length

    private func round(_ player: String, holes: Int, total: Int, toPar: Int) -> Stats.Round {
        Stats.Round(player: player, gameId: 1, status: "FINISHED", holesCount: holes, holesPlayed: holes, total: total, toPar: toPar, won: false)
    }

    @Test func bestRoundsAreListedPerLength() {
        // The server's order: per 18 holes, so a 9-hole +4 (+8 per 18) comes after an 18-hole +6.
        let rounds = [round("Ann", holes: 18, total: 78, toPar: 6), round("Bob", holes: 9, total: 40, toPar: 4),
                      round("Cy", holes: 9, total: 45, toPar: 9)]
        #expect(LeaderboardView.bestRoundLengths(rounds) == [9, 18])
        #expect(LeaderboardView.bestRounds(rounds, holes: 9).map(\.player) == ["Bob", "Cy"])
        #expect(LeaderboardView.bestRounds(rounds, holes: 18).map(\.total) == [78])
        #expect(LeaderboardView.bestRoundLengths(Array(rounds.dropFirst())) == [9])
        #expect(LeaderboardView.bestRoundLengths([]) == [9], "an empty list still shows its dash")
    }

    // MARK: E-7 Sizes that fit

    @Test func screenFitSizesRunFromSmallToRegular() {
        #expect(ScreenFit(height: 500, level: 1).size(320, 220) == 320)
        #expect(ScreenFit(height: 500, level: 0).size(320, 220) == 220)
        #expect(ScreenFit(height: 500, level: 0.5).size(320, 220) == 270)
        #expect(ScreenFit.levels.first == 1 && ScreenFit.levels.contains(0), "biggest first, through small")
        #expect(ScreenFit.levels == ScreenFit.levels.sorted(by: >))
        #expect(ScreenFit(height: 500).compact && !ScreenFit(height: 700).compact)
    }
}
