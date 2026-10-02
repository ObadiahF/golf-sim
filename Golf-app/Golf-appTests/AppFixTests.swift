import Foundation
import SwiftUI
import Testing
@testable import Golf_app

/// The hold-to-repeat timer, the server address (default, validation, WebSocket query encoding) and the
/// link state the game screens follow (outage banner, score reloads).
@MainActor
struct AppFixTests {
    // MARK: Hold to repeat (aim)

    @Test func holdRepeatRunsOnlyWhilePressedEnabledAndActive() {
        #expect(HoldRepeat.shouldRun(pressed: true, enabled: true, phase: .active))
        #expect(!HoldRepeat.shouldRun(pressed: false, enabled: true, phase: .active))
        #expect(!HoldRepeat.shouldRun(pressed: true, enabled: false, phase: .active))
        #expect(!HoldRepeat.shouldRun(pressed: true, enabled: true, phase: .inactive))
        #expect(!HoldRepeat.shouldRun(pressed: true, enabled: true, phase: .background))
    }

    /// Counts the repeat's actions.
    final class Counter { var fired = 0 }

    @Test func holdRepeatStopsWhenCancelled() async {
        let counter = Counter()
        let repeater = Task { await HoldRepeat.repeating(delay: .milliseconds(20), interval: .milliseconds(10)) { counter.fired += 1 } }
        await waitUntil { counter.fired >= 4 }
        #expect(counter.fired >= 4, "repeats while held")
        repeater.cancel() // what the view's task does on release, disappear, disable or background
        await repeater.value
        let stopped = counter.fired
        try? await Task.sleep(for: .milliseconds(150))
        #expect(counter.fired == stopped, "no aim after the repeat is cancelled")
    }

    @Test func holdRepeatCancelledBeforeTheDelayNeverRepeats() async {
        let counter = Counter()
        let repeater = Task { await HoldRepeat.repeating(delay: .milliseconds(200), interval: .milliseconds(10)) { counter.fired += 1 } }
        try? await Task.sleep(for: .milliseconds(50))
        repeater.cancel() // a tap: released before the repeat starts
        await repeater.value
        try? await Task.sleep(for: .milliseconds(250))
        #expect(counter.fired == 0)
    }

    // MARK: Server address

    @Test func hostedServerIsTheDefault() throws {
        let settings = try testSettings("AppFixTests.default")
        #expect(settings.serverURL?.absoluteString == "https://golf-server.obadiahfusco.xyz")
        let ws = try #require(settings.serverURL.flatMap { AppConfig.webSocketURL(server: $0) })
        #expect(ws.absoluteString == "wss://golf-server.obadiahfusco.xyz/ws")
        settings.server = "192.168.1.20" // the Settings override for a local or LAN server
        #expect(settings.serverURL?.absoluteString == "http://192.168.1.20:8080")
    }

    @Test func serverAddressValidation() {
        #expect(AppConfig.serverAddressProblem("") == nil)
        #expect(AppConfig.serverAddressProblem("192.168.1.20:8080") == nil)
        #expect(AppConfig.serverAddressProblem("https://golf.example") == nil)
        #expect(AppConfig.serverAddressProblem("ftp://bad host") != nil)
        #expect(AppConfig.serverAddressProblem("ftp://x") != nil)
        #expect(AppConfig.serverAddressProblem("http://") != nil)
        #expect(AppConfig.serverAddressProblem("ftp://x")?.contains("8,080") == false)
    }

    @Test func webSocketQueryIsPercentEncoded() throws {
        let server = try #require(URL(string: "https://golf.example"))
        let url = try #require(GameLink.socketURL(server: server, name: "Ann+Bob & Co=1/2?é"))
        let query = try #require(url.query(percentEncoded: true))
        #expect(query == "token=golf-sim-dev-token&role=remote&name=Ann%2BBob%20%26%20Co%3D1%2F2%3F%C3%A9")
        #expect(url.absoluteString.hasPrefix("wss://golf.example/ws?"))
        // Decoding it (as the server does) gives the name back, with the plus intact.
        let name = URLComponents(url: url, resolvingAgainstBaseURL: false)?.queryItems?.first { $0.name == "name" }?.value
        #expect(name == "Ann+Bob & Co=1/2?é")
    }

    // MARK: Link state for the screens

    @Test func outageAndScoreReloads() throws {
        let link = GameLink(settings: try testSettings("AppFixTests.link"), deviceName: "test")
        #expect(link.outage != nil, "not connected yet")
        link.handle(.hello(.init(simConnected: true, remotes: [], game: nil, state: nil)))
        #expect(link.outage == nil)
        let revision = link.scoresRevision
        let game = GameView(id: 1, status: "FINISHED", holesCount: 9, pars: [], players: [], winners: [])
        link.handle(.scorecard(game))
        link.handle(.gameFinished(game))
        #expect(link.scoresRevision == revision + 2)
        link.handle(.shotResult(.init(player: "Ann", lie: "fairway")))
        #expect(link.scoresRevision == revision + 2)
    }

    @Test func scorecardFollowsTheHoleBeingPlayed() {
        func card(_ holes: Int, played: [Int]) -> GameView {
            GameView(id: 1, status: "IN_PROGRESS", holesCount: holes, pars: [],
                     players: played.enumerated().map { .init(name: "P\($0)", turnOrder: $0, strokes: [], holesPlayed: $1, total: 0, par: 0, toPar: 0) },
                     winners: [])
        }
        #expect(ScorecardTable.focusHole(card(18, played: [0, 0])) == 0)
        #expect(ScorecardTable.focusHole(card(18, played: [10, 9])) == 10)
        #expect(ScorecardTable.focusHole(card(18, played: [18, 18])) == 17)
        #expect(ScorecardTable.focusHole(card(9, played: [])) == 0)
        #expect(ScorecardTable.nineTitle(1, of: 2) == "Back 9")
    }
}
