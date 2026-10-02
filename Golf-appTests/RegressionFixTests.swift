import Foundation
import Testing
@testable import Golf_app

/// Round 5 (regression sweep): the putting meter empties for the same player's next putt (Q5-2), the remote's
/// hint for every screen (Q5-3) and Replay on the scorecards (Q5-4), the scorecard the Scores tab opens on and
/// hole columns that fit a nine (Q5-5), and sizes below small for the iPhone SE (Q5-7).
@MainActor
struct RegressionFixTests {
    private func state(_ json: String) throws -> GameProtocol.SimState {
        guard case .state(let state)? = GameProtocol.decode(Data(json.utf8)) else { throw CancellationError() }
        return state
    }

    // MARK: Q5-2 The meter on the same player's next putt

    private static func putting(strokes: Int, canShoot: Bool, player: String = "Obi") -> GameProtocol.SimState {
        GameProtocol.SimState(screen: "game", currentPlayer: player, hole: 3, strokes: strokes, putting: true, puttDistance: 5,
                              canShoot: canShoot, waitReason: canShoot ? "" : "Wait for the ball to stop")
    }

    @Test func theSamePlayersNextPuttStartsEmpty() {
        var meter = PuttMeter()
        meter.follow(Self.putting(strokes: 2, canShoot: true))
        meter.strike(distance: 6.4, toHole: 11)
        meter.follow(Self.putting(strokes: 2, canShoot: false)) // rolling: the meter keeps the putt
        #expect(meter.shown == 6.4)
        meter.finish(GameProtocol.ShotResult(player: "Obi", total: 5.9 / PuttModel.metersPerYard, lie: "green"))
        meter.follow(Self.putting(strokes: 3, canShoot: false)) // stroke counted, ball still settling
        #expect(meter.shown == 6.4)
        meter.follow(Self.putting(strokes: 3, canShoot: true)) // the next putt: no `turn` from the sim
        #expect(meter.shown == 0 && meter.peak == 0)
        #expect(meter.result?.summary == "Putted 5.9 m of 11.0 m", "the last putt's result stays")
    }

    @Test func aRepeatedStateKeepsAStrokeInProgress() {
        var meter = PuttMeter()
        meter.follow(Self.putting(strokes: 1, canShoot: true))
        meter.begin()
        meter.track(distance: 2.5)
        meter.follow(Self.putting(strokes: 1, canShoot: true)) // e.g. aim changed
        #expect(meter.shown == 2.5)
    }

    @Test func anotherPlayerUpStartsEmpty() {
        var meter = PuttMeter()
        meter.follow(Self.putting(strokes: 1, canShoot: true))
        meter.strike(distance: 3, toHole: 5)
        meter.follow(Self.putting(strokes: 1, canShoot: true, player: "Ann"))
        #expect(meter.shown == 0)
    }

    @Test func theSessionEmptiesTheMeterForTheNextStroke() throws {
        let motion = ShotReadinessTests.ManualMotion()
        let session = SwingSession(settings: try testSettings("RegressionFixTests.stroke"), motion: motion)
        session.game.handle(.hello(.init(simConnected: true, remotes: ["test"], game: nil, state: Self.putting(strokes: 1, canShoot: true))))
        session.selectClub(Club.bag.firstIndex { $0.isPutter } ?? 0)
        session.address()
        motion.feed(stride(from: 0.0, through: 2.0, by: 0.01).map { MotionSample(time: $0, rate: 0.02, angle: 0.1, face: 0) })
        motion.feed(SyntheticSwing.samples(.putt(impactRate: 2, face: 0), start: 2.1))
        #expect(session.meter.shown > 0, "the putt was struck")
        session.game.handle(.state(Self.putting(strokes: 1, canShoot: false)))
        session.game.handle(.shotResult(GameProtocol.ShotResult(player: "Obi", total: 3, lie: "green", holed: false, strokes: 2)))
        session.game.handle(.state(Self.putting(strokes: 2, canShoot: true)))
        #expect(session.meter.shown == 0)
        #expect(session.meter.result != nil)
    }

    // MARK: Q5-3 / Q5-4 The remote's hints

    private func link(_ json: String) throws -> GameLink {
        let link = GameLink(settings: try testSettings("RegressionFixTests.remote"), deviceName: "test")
        link.handle(.hello(.init(simConnected: true, remotes: ["test"], game: nil, state: try state(json))))
        return link
    }

    @Test func soundPanelHint() throws {
        let link = try link(#"{"type":"state","screen":"settings"}"#)
        #expect(RemoteView.hint(link) == "Up/Down choose · Left/Right change · Back close")
        #expect(RemoteView.screenTitle(link) == "Sound")
    }

    @Test(arguments: ["menu", "courseSelect", "loading", "game", "paused", "settings", "replay", "holeComplete", "scorecard", "results"])
    func everyScreenHasItsOwnHint(screen: String) throws {
        let hint = RemoteView.hint(try link(#"{"type":"state","screen":"\#(screen)"}"#))
        let menu = RemoteView.hint(try link(#"{"type":"state","screen":"menu"}"#))
        #expect(screen == "menu" || hint != menu, "only the menu says how to start a round")
    }

    @Test func unknownScreensGetTheDPadsGeneralHint() throws {
        #expect(RemoteView.hint(try link(#"{"type":"state","screen":"credits"}"#)) == "Arrows to move, OK to select, Back to go back.")
    }

    @Test func scorecardOffersReplayOnlyWhenTheSimSays() throws {
        let offered = try link(#"{"type":"state","screen":"holeComplete","canReplay":true}"#)
        #expect(offered.offersScorecardReplay)
        #expect(RemoteView.hint(offered) == "Press OK for the next hole.\n▲ Replay last shot")
        #expect(try link(#"{"type":"state","screen":"results","canReplay":true}"#).offersScorecardReplay)
        #expect(!(try link(#"{"type":"state","screen":"holeComplete","canReplay":false}"#)).offersScorecardReplay)
        #expect(!(try link(#"{"type":"state","screen":"holeComplete"}"#)).offersScorecardReplay, "older sims")
        #expect(!(try link(#"{"type":"state","screen":"game","canReplay":true}"#)).offersScorecardReplay, "gameplay's own button")
        offered.handle(.simStatus(connected: false))
        #expect(!offered.offersScorecardReplay)
    }

    // MARK: Q5-5 The Scores tab

    private func game(_ id: Int, _ status: String) -> GameView {
        GameView(id: id, status: status, holesCount: 9, pars: [], players: [], winners: [])
    }

    @Test func scoresOpenOnTheGameInProgress() {
        let recent = [game(3, "ABANDONED"), game(2, "FINISHED")]
        #expect(ScoresView.shownGame(live: game(4, "IN_PROGRESS"), recent: recent)?.id == 4)
        #expect(ScoresView.shownGame(live: nil, recent: [game(5, "IN_PROGRESS")] + recent)?.id == 5)
    }

    @Test func scoresSkipAnAbandonedGameForTheLastFinishedOne() {
        let recent = [game(2, "ABANDONED"), game(1, "FINISHED")]
        #expect(ScoresView.shownGame(live: nil, recent: recent)?.id == 1)
        #expect(ScoresView.shownGame(live: game(2, "ABANDONED"), recent: recent)?.id == 1)
        // The live copy is newer than a fetched one still in progress.
        #expect(ScoresView.shownGame(live: game(3, "FINISHED"), recent: [game(3, "IN_PROGRESS"), game(1, "FINISHED")])?.id == 3)
    }

    @Test func scoresWithoutAFinishedGameShowTheLatest() {
        #expect(ScoresView.shownGame(live: nil, recent: [game(2, "ABANDONED"), game(1, "ABANDONED")])?.id == 2)
        #expect(ScoresView.shownGame(live: nil, recent: []) == nil)
    }

    @Test func aNineFitsBesideTheNamesOnA17Pro() {
        // The Scores card on a 402-point-wide iPhone 17 Pro: 402 - 2 x 16 margin - 2 x 14 card padding.
        let width: CGFloat = 342
        let columns = ScorecardTable.Columns.fit(width: width, holes: 9, subtotal: false)
        #expect(ScorecardTable.Columns.cellRange.contains(columns.cell))
        #expect(columns.name >= ScorecardTable.Columns.minName)
        let used = columns.name + ScorecardTable.fixedWidth + ScorecardTable.Columns.holesWidth(cell: columns.cell, holes: 9, subtotal: false)
        #expect(used <= width, "holes 1-9, TOT and ± all show without scrolling")
    }

    @Test func shortCardsGiveNamesTheRoom() {
        let three = ScorecardTable.Columns.fit(width: 342, holes: 3, subtotal: false)
        #expect(three.cell == 28)
        #expect(three.name > 150, "\"White Hayden\" isn't cut short on a 3-hole card")
        #expect(ScorecardTable.Columns.fit(width: 0, holes: 9, subtotal: false) == .unmeasured)
        // Too narrow for every column: the holes keep their smallest size and scroll.
        #expect(ScorecardTable.Columns.fit(width: 250, holes: 9, subtotal: true).cell == ScorecardTable.Columns.cellRange.lowerBound)
    }

    // MARK: Q5-7 Below small

    @Test func tightLevelsShrinkPastSmall() {
        #expect(ScreenFit(height: 400, level: -0.5).size(320, 220) == 170)
        #expect(ScreenFit(height: 400, level: -0.5).tight && !ScreenFit(height: 400, level: 0).tight)
        #expect(ScreenFit.levels.last == -1, "the scroll fallback uses the smallest")
    }
}
