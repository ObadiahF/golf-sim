import Foundation
import SwiftUI
import Testing
@testable import Golf_app

struct GameProtocolTests {
    private func encoded<T: Encodable>(_ message: T) -> [String: Any] { jsonObject(GameProtocol.encode(message)) }

    private func decode(_ json: String) -> GameProtocol.Incoming? { GameProtocol.decode(Data(json.utf8)) }

    static let gameJSON = #"""
    {"id":12,"status":"IN_PROGRESS","holesCount":9,"courseName":null,"createdAt":"2026-10-02T07:00:00Z","finishedAt":null,
     "pars":[4,null,null,null,null,null,null,null,null],
     "players":[{"name":"Ann","turnOrder":1,"strokes":[5,null,null,null,null,null,null,null,null],"holesPlayed":1,"total":5,"par":4,"toPar":1},
                {"name":"Bob","turnOrder":2,"strokes":[null,null,null,null,null,null,null,null,null],"holesPlayed":0,"total":0,"par":0,"toPar":0}],
     "winners":[]}
    """#

    @Test func remoteCommandsEncode() {
        #expect(encoded(GameProtocol.Nav(key: .select)) as NSDictionary == ["type": "nav", "key": "select"])
        #expect(encoded(GameProtocol.ClubChoice(club: "7 Iron")) as NSDictionary == ["type": "club", "club": "7 Iron"])
        #expect(encoded(GameProtocol.Aim(delta: -1)) as NSDictionary == ["type": "aim", "delta": -1])
        #expect(encoded(GameProtocol.Bare.aimReset) as NSDictionary == ["type": "aimReset"])
        #expect(encoded(GameProtocol.Bare.mulligan) as NSDictionary == ["type": "mulligan"])
        #expect(encoded(GameProtocol.Bare.skip) as NSDictionary == ["type": "skip"])
        #expect(encoded(GameProtocol.Bare.ping) as NSDictionary == ["type": "ping"])
        #expect(GameProtocol.NavKey.allCases.map(\.rawValue) == ["up", "down", "left", "right", "select", "back"])
    }

    @Test func shotUsesTheUDPShotFields() {
        let shot = Shot.from(Impact(rate: 20, face: 2, angle: 0, time: 0), club: Club.bag[0], scale: 1, faceSign: -1)
        let json = encoded(SimProtocol.ShotMessage(id: 77, shot: shot))
        #expect(json["type"] as? String == "shot")
        #expect(json["id"] as? Int == 77)
        #expect(json["club"] as? String == "Driver")
        for key in ["speed", "launch", "azimuth", "back", "side"] { #expect(json[key] is Double, "\(key)") }
    }

    @Test func decodesHelloWithGameAndState() throws {
        let json = #"{"type":"hello","role":"remote","simConnected":true,"remotes":["iPhone"],"game":\#(Self.gameJSON),"state":{"type":"state","screen":"game","gameId":12,"currentPlayer":"Ann","hole":2,"par":4,"strokes":1,"club":"5 Iron","aim":-2.0,"distanceToPin":151.5,"lie":"fairway"}}"#
        guard case .hello(let hello) = decode(json) else { Issue.record("not a hello"); return }
        #expect(hello.simConnected)
        #expect(hello.remotes == ["iPhone"])
        #expect(hello.game?.id == 12)
        #expect(hello.state?.player == "Ann")
        #expect(hello.state?.isGame == true)
        #expect(hello.state?.distanceToPin == 151.5)
    }

    @Test func decodesHelloWithNulls() {
        guard case .hello(let hello) = decode(#"{"type":"hello","role":"remote","simConnected":false,"remotes":[],"game":null,"state":null}"#)
        else { Issue.record("not a hello"); return }
        #expect(!hello.simConnected)
        #expect(hello.game == nil && hello.state == nil)
    }

    @Test func gameViewKeepsNullScores() throws {
        guard case .scorecard(let game) = decode(#"{"type":"scorecard","game":\#(Self.gameJSON)}"#) else { Issue.record("not a scorecard"); return }
        #expect(game.isInProgress)
        #expect(game.pars[0] == 4 && game.pars[1] == nil)
        #expect(game.players.map(\.name) == ["Ann", "Bob"])
        #expect(game.players[0].strokes[0] == 5 && game.players[0].strokes[1] == nil)
        #expect(game.players[0].toPar == 1)
    }

    @Test func decodesSimUpdatesAndServerMessages() {
        // Unity's JsonUtility writes every field, so practice states arrive with empty strings and zeros.
        let state = decode(#"{"type":"state","screen":"menu","gameId":0,"currentPlayer":"","hole":0,"par":0,"strokes":0,"club":"","aim":0.0,"distanceToPin":0.0,"lie":""}"#)
        guard case .state(let menu) = state else { Issue.record("not a state"); return }
        #expect(menu.player == nil && !menu.isGame)
        #expect(decode(#"{"type":"shotResult","player":"Ann","carry":150.2,"total":160.0,"lie":"green","holed":false,"strokes":2}"#)
            == .shotResult(.init(player: "Ann", carry: 150.2, total: 160, lie: "green", holed: false, strokes: 2)))
        #expect(decode(#"{"type":"turn","player":"Bob","hole":3,"strokes":0}"#) == .turn(.init(player: "Bob", hole: 3, strokes: 0)))
        #expect(decode(#"{"type":"simStatus","connected":true}"#) == .simStatus(connected: true))
        #expect(decode(#"{"type":"error","message":"no sim connected"}"#) == .error("no sim connected"))
        #expect(decode(#"{"type":"pong"}"#) == .pong)
        #expect(decode(#"{"type":"somethingNew","x":1}"#) == .other("somethingNew"))
        #expect(decode("not json") == nil)
        if case .gameStarted(let game) = decode(#"{"type":"gameStarted","game":\#(Self.gameJSON)}"#) { #expect(game.id == 12) } else { Issue.record("gameStarted") }
        if case .gameFinished = decode(#"{"type":"gameFinished","game":\#(Self.gameJSON)}"#) {} else { Issue.record("gameFinished") }
    }

    @Test func scorecardNinesAndRanking() {
        #expect(ScorecardTable.nines(holes: 9) == [Array(0..<9)])
        #expect(ScorecardTable.nines(holes: 18) == [Array(0..<9), Array(9..<18)])
        #expect(ScorecardTable.subtotalLabel(0, of: 1) == nil)
        #expect(ScorecardTable.subtotalLabel(0, of: 2) == "OUT" && ScorecardTable.subtotalLabel(1, of: 2) == "IN")
        #expect(ScorecardTable.sum([4, nil, 5]) == 9 && ScorecardTable.sum([nil, nil]) == nil)
        func player(_ name: String, _ handicap: Double?, _ average: Double?) -> Stats.Player {
            Stats.Player(name: name, gamesPlayed: 1, finishedRounds: 1, wins: 0, averageToPar: average, handicap: handicap)
        }
        let ranked = LeaderboardView.ranked([player("Cy", nil, 1), player("Bo", 8, 5), player("Al", 3, 9), player("Di", nil, nil)])
        #expect(ranked.map(\.name) == ["Al", "Bo", "Cy", "Di"])
        #expect(formatToPar(0.02) == "E" && formatToPar(2.5) == "+2.5")
    }

    @Test func toParAndAimLabels() {
        #expect(formatToPar(0) == "E")
        #expect(formatToPar(3) == "+3")
        #expect(formatToPar(-2) == "-2")
        #expect(AimControl.label(0) == "At the pin")
        #expect(AimControl.label(3) == "3° right")
        #expect(AimControl.label(-1.5) == "1.5° left")
    }

    @Test func clubWheelMapsDirectionsToClubs() {
        let size: CGFloat = 300
        #expect(ClubWheel<EmptyView>.index(at: CGPoint(x: 150, y: 0), size: size) == 0) // top: driver
        #expect(ClubWheel<EmptyView>.index(at: CGPoint(x: 160, y: 2), size: size) == 0)
        #expect(ClubWheel<EmptyView>.index(at: CGPoint(x: 120, y: 2), size: size) == 0)
        #expect(ClubWheel<EmptyView>.index(at: CGPoint(x: 300, y: 140), size: size) == 3) // just above right: 86° / (360/14) ≈ 3.35
        #expect(ClubWheel<EmptyView>.index(at: CGPoint(x: 140, y: 5), size: size) == 0)
        #expect(ClubWheel<EmptyView>.index(at: CGPoint(x: 0, y: 140), size: size) == 11) // just above left: 274° ≈ 10.65
    }

    @Test func rosterRules() {
        #expect(Roster.validate("  Ann ", joining: []) == .success("Ann"))
        #expect(Roster.validate("   ", joining: []) == .failure(.empty))
        #expect(Roster.validate("ann", joining: ["Ann"]) == .failure(.duplicate))
        #expect(Roster.validate(String(repeating: "x", count: 41), joining: []) == .failure(.tooLong))
        #expect(Roster.validate("Zed", joining: (1...8).map { "P\($0)" }) == .failure(.full))
    }

    @Test func serverURLs() throws {
        #expect(AppConfig.serverURL("192.168.1.20")?.absoluteString == "http://192.168.1.20:8080")
        #expect(AppConfig.serverURL(" pc.local:9000 ")?.absoluteString == "http://pc.local:9000")
        #expect(AppConfig.serverURL("http://10.0.0.2:8080/api")?.absoluteString == "http://10.0.0.2:8080")
        #expect(AppConfig.serverURL("https://golf.example")?.absoluteString == "https://golf.example")
        #expect(AppConfig.serverURL("") == nil)
        #expect(AppConfig.serverURL("ftp://x") == nil)
        let ws = try #require(AppConfig.serverURL("192.168.1.20").flatMap { AppConfig.webSocketURL(server: $0) })
        #expect(ws.absoluteString == "ws://192.168.1.20:8080/ws")
        #expect(AppConfig.webSocketURL(server: URL(string: "https://golf.example")!)?.absoluteString == "wss://golf.example/ws")
    }

    /// Every field today's sim writes (JsonUtility writes them all, wind included), so no "state" is dropped as
    /// malformed and leaves the phone on the last screen: a replay mid-putt shows the remote (Select skips it).
    @Test func decodesTheSimsFullState() throws {
        let json = #"{"type":"state","screen":"replay","canShoot":false,"waitReason":"Wait for the replay to finish","canReplay":false,"gameId":31,"currentPlayer":"Ann","hole":4,"par":3,"strokes":2,"club":"Putter","aim":-1.5,"distanceToPin":3.0,"lie":"green","wind":7,"windAngle":135,"mapOpen":false,"putting":true,"puttDistance":2.71,"elevation":-0.04,"stimp":9.3,"puttPlaysAs":3.05,"puttingAssist":"full","practice":"","attempts":0,"made":0}"#
        guard case .state(let state) = try #require(decode(json)) else { Issue.record("not a state"); return }
        #expect(state.puttPlaysAs == 3.05 && state.club == "Putter")
        #expect(PlayScreen.of(state) == .remote)
        #expect(PlayScreen.of(GameProtocol.SimState(screen: "game", putting: true)) == .putting)
    }

    @Test func shotResultLies() {
        let water = GameProtocol.ShotResult(player: "A", lie: "water")
        #expect(water.lieText == "In the water (+1)" && water.outcome == "InWater")
        #expect(GameProtocol.ShotResult(player: "A", lie: "ob").outcome == "OutOfBounds")
        #expect(GameProtocol.ShotResult(player: "A", lie: "holed", holed: true).lieText == "In the hole!")
        #expect(GameProtocol.ShotResult(player: "A", lie: "fairway").lieText == "Fairway")
        #expect(GameProtocol.SimState(screen: "holeComplete").showsScorecard)
        #expect(GameProtocol.SimState(screen: "results").showsScorecard)
        #expect(!GameProtocol.SimState(screen: "paused").showsScorecard)
    }
}
