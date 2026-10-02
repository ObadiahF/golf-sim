import Foundation

/// The game server's WebSocket messages: JSON text frames `{"type": ..., ...}`. The server's
/// `WsMessage.java` / `docs/PROTOCOL.md` is the source of truth. Remote commands are relayed to the
/// sim unchanged; sim updates are relayed to every remote.
nonisolated enum GameProtocol {
    static let encoder = JSONEncoder()
    static let decoder = JSONDecoder()

    // MARK: Remote -> sim

    enum NavKey: String, Codable, CaseIterable, Sendable {
        case up, down, left, right, select, back
    }

    struct Nav: Codable, Equatable {
        var type = "nav"
        var key: NavKey
    }

    struct ClubChoice: Codable, Equatable {
        var type = "club"
        var club: String
    }

    /// Turns the aim by `delta` degrees, + right.
    struct Aim: Codable, Equatable {
        var type = "aim"
        var delta: Double
    }

    /// A message that is only its type: ping, aimReset, mulligan, skip.
    struct Bare: Codable, Equatable {
        var type: String
        static let ping = Bare(type: "ping")
        static let aimReset = Bare(type: "aimReset")
        /// Undo the last shot: the ball goes back and the stroke doesn't count.
        static let mulligan = Bare(type: "mulligan")
        /// The current player picks up on this hole and scores the maximum.
        static let skip = Bare(type: "skip")
    }

    // A shot is `SimProtocol.ShotMessage`, the same JSON as the UDP datagram (the server ignores `v`).

    // MARK: Sim -> remotes

    /// What the sim is showing. `screen`: menu, loading, game, paused, replay (instant replay on the TV),
    /// holeComplete (scorecard between holes) or results (final scorecard). Unknown screens show the remote.
    struct SimState: Codable, Equatable, Sendable {
        var screen: String
        /// 0 or absent in practice (no server game).
        var gameId: Int?
        var currentPlayer: String?
        var hole: Int?
        var par: Int?
        /// Strokes the current player has taken on this hole.
        var strokes: Int?
        /// One of `Club.bag`'s names.
        var club: String?
        /// Degrees, + right.
        var aim: Double?
        /// Yards.
        var distanceToPin: Double?
        var lie: String?

        // Putting mode (all optional: older sims leave them out, Unity writes false / 0 outside putting mode).
        /// The current player is putting: the app shows its putting view and power meter.
        var putting: Bool?
        /// Metres to the pin, to the centimetre.
        var puttDistance: Double?
        /// Metres the pin sits above (+) or below (-) the ball.
        var elevation: Double?
        /// Green speed, Stimpmeter feet (see `PuttModel`).
        var stimp: Double?
        /// Metres the putt that finishes 40 cm past the hole would roll on a flat green: the meter's target.
        var puttPlaysAs: Double?
        /// full, partial or off: how much of the break line the sim draws.
        var puttingAssist: String?

        // Shot readiness (optional: older sims leave them out, meaning a swing is always hit).
        /// A swing would be hit now; false between shots (ball moving, next player's turn), screen still "game".
        var canShoot: Bool?
        /// Why not, e.g. "Wait for the next turn"; empty while `canShoot`.
        var waitReason: String?
        /// The TV offers an instant replay of the last shot ("▲ Replay"; `nav up` starts it). Optional: today's sim
        /// leaves it out, and the phone then infers the offer from `waitReason` (see `offersReplay`).
        var canReplay: Bool?

        var isGame: Bool { screen == "game" }
        /// The sim is playing a hole and would hit a swing now.
        var acceptsShots: Bool { isGame && canShoot != false }
        /// What to tell the player while the sim is in a game but can't take a swing; nil when it can.
        var shotWait: String? {
            guard isGame, canShoot == false else { return nil }
            return waitReason.flatMap { $0.isEmpty ? nil : $0 } ?? "Wait for the next shot"
        }
        var isPutting: Bool { isGame && putting == true }
        /// The sim's `waitReason` between turns: the ball is at rest and the next player isn't up yet.
        static let betweenTurns = "Wait for the next turn"
        /// The TV offers a replay of the last shot, so the phone shows a Replay button. Without `canReplay`, between
        /// turns: that is when the sim's ReplayDirector offers it (not while the ball moves, nor during a replay).
        var offersReplay: Bool { isGame && (canReplay ?? (canShoot == false && waitReason == Self.betweenTurns)) }
        var showsScorecard: Bool { screen == "holeComplete" || screen == "results" }
        /// On a scorecard the TV offers "▲ Replay" of the hole's last shot (only when the sim says so).
        var offersScorecardReplay: Bool { showsScorecard && canReplay == true }
        var player: String? { currentPlayer.flatMap { $0.isEmpty ? nil : $0 } }
    }

    /// Distances in yards. `lie` is the resting surface, or water / ob (penalty) / holed.
    struct ShotResult: Codable, Equatable, Sendable {
        var player: String
        var carry: Double?
        var total: Double?
        var lie: String?
        var holed: Bool?
        var strokes: Int?

        /// "Fairway", "In the water (+1)", "Out of bounds (+1)", "In the hole!".
        var lieText: String {
            if holed == true || lie == "holed" { return "In the hole!" }
            return switch lie ?? "" {
            case "water": "In the water (+1)"
            case "ob": "Out of bounds (+1)"
            case let surface: surface.capitalized
            }
        }

        /// The UDP result's outcome names, for the shot card.
        var outcome: String {
            if holed == true || lie == "holed" { return "Holed" }
            return switch lie { case "water": "InWater"; case "ob": "OutOfBounds"; default: "Stopped" }
        }
    }

    struct Turn: Codable, Equatable, Sendable {
        var player: String
        var hole: Int
        var strokes: Int?
    }

    /// The sim got a `shot` it couldn't hit; `id` echoes the shot's id when it had one.
    struct ShotRejected: Codable, Equatable, Sendable {
        var reason: String
        var id: Int?
    }

    // MARK: Server -> clients

    struct Hello: Codable, Equatable, Sendable {
        var role: String?
        var simConnected: Bool
        var remotes: [String]?
        var game: GameView?
        var state: SimState?
    }

    // MARK: Decoding

    enum Incoming: Equatable, Sendable {
        case hello(Hello)
        case simStatus(connected: Bool)
        case state(SimState)
        case shotResult(ShotResult)
        case turn(Turn)
        case shotRejected(ShotRejected)
        case gameStarted(GameView)
        case scorecard(GameView)
        case gameFinished(GameView)
        case error(String)
        case pong
        /// A type this app doesn't know (newer server); ignored.
        case other(String)
    }

    private struct Envelope: Decodable { var type: String }
    private struct GameEnvelope: Decodable { var game: GameView }
    private struct SimStatusBody: Decodable { var connected: Bool }
    private struct ErrorBody: Decodable { var message: String? }

    static func encode<T: Encodable>(_ message: T) -> Data? { try? encoder.encode(message) }

    /// Decodes one frame; nil when it isn't valid JSON or a known type is malformed.
    static func decode(_ data: Data) -> Incoming? {
        guard let type = try? decoder.decode(Envelope.self, from: data).type else { return nil }
        func body<T: Decodable>(_: T.Type) -> T? { try? decoder.decode(T.self, from: data) }
        switch type {
        case "hello": return body(Hello.self).map(Incoming.hello)
        case "simStatus": return body(SimStatusBody.self).map { .simStatus(connected: $0.connected) }
        case "state": return body(SimState.self).map(Incoming.state)
        case "shotResult": return body(ShotResult.self).map(Incoming.shotResult)
        case "turn": return body(Turn.self).map(Incoming.turn)
        case "shotRejected": return body(ShotRejected.self).map(Incoming.shotRejected)
        case "gameStarted": return body(GameEnvelope.self).map { .gameStarted($0.game) }
        case "scorecard": return body(GameEnvelope.self).map { .scorecard($0.game) }
        case "gameFinished": return body(GameEnvelope.self).map { .gameFinished($0.game) }
        case "error": return .error(body(ErrorBody.self)?.message ?? "Server error")
        case "pong": return .pong
        default: return .other(type)
        }
    }
}

// MARK: - Scorecard (REST and WebSocket)

/// A game and its scorecard, as the server returns it.
nonisolated struct GameView: Codable, Equatable, Sendable, Identifiable {
    var id: Int
    /// IN_PROGRESS, FINISHED or ABANDONED.
    var status: String
    var holesCount: Int
    var courseName: String?
    var createdAt: String?
    var finishedAt: String?
    /// Par per hole (index 0 = hole 1), null until someone has a score on it.
    var pars: [Int?]
    /// In turn order.
    var players: [PlayerCard]
    var winners: [String]

    struct PlayerCard: Codable, Equatable, Sendable, Identifiable {
        var name: String
        var turnOrder: Int
        /// Strokes per hole, null where not played yet.
        var strokes: [Int?]
        var holesPlayed: Int
        var total: Int
        var par: Int
        var toPar: Int

        var id: String { name }
    }

    var isInProgress: Bool { status == "IN_PROGRESS" }
    var isFinished: Bool { status == "FINISHED" }
}

// MARK: - Stats (REST)

nonisolated enum Stats {
    struct Player: Codable, Equatable, Sendable, Identifiable {
        var name: String
        var gamesPlayed: Int
        var finishedRounds: Int
        var wins: Int
        // Scores count only complete 9- and 18-hole rounds (shorter games still count as rounds and wins).
        /// Lowest 9-hole / 18-hole total; nil without one.
        var best9: Int?
        var best18: Int?
        /// Average 9-hole / 18-hole total, one decimal; nil without one.
        var avg9: Double?
        var avg18: Double?
        /// Average to par per 18 holes (a 9-hole round's is doubled).
        var averageToPar: Double?
        /// Per 18 holes, over the last 5 9- or 18-hole rounds.
        var recentAverageToPar: Double?
        /// World-Handicap-style index; nil until 3 9- or 18-hole rounds (and from older servers).
        var handicap: Double?
        var holes: HoleTallies?
        var lastPlayedAt: String?

        var id: String { name }
    }

    /// How a player's holes went, across every game.
    struct HoleTallies: Codable, Equatable, Sendable {
        var played: Int
        var holesInOne: Int
        var eagles: Int
        var birdies: Int
        var pars: Int
        var bogeys: Int
        var doubleBogeysOrWorse: Int
    }

    struct Round: Codable, Equatable, Sendable {
        var player: String
        var gameId: Int
        var status: String
        var courseName: String?
        var holesCount: Int
        var playedAt: String?
        var holesPlayed: Int
        var total: Int
        var toPar: Int
        var won: Bool
    }

    struct WinCount: Codable, Equatable, Sendable {
        var player: String
        var wins: Int
        var finishedRounds: Int
    }

    struct AverageToPar: Codable, Equatable, Sendable {
        var player: String
        var finishedRounds: Int
        /// Per 18 holes.
        var averageToPar: Double
    }

    struct HandicapRank: Codable, Equatable, Sendable {
        var player: String
        var finishedRounds: Int
        var handicap: Double
    }

    struct BirdieCount: Codable, Equatable, Sendable {
        var player: String
        /// Birdies, eagles and holes-in-one.
        var birdiesOrBetter: Int
        var holesPlayed: Int
    }

    /// Top 10s. The last two lists are newer; older servers leave them out.
    struct Leaderboard: Codable, Equatable, Sendable {
        var bestRounds: [Round]
        var mostWins: [WinCount]
        var bestAverageToPar: [AverageToPar]
        var lowestHandicap: [HandicapRank]?
        var mostBirdies: [BirdieCount]?

        var isEmpty: Bool { bestRounds.isEmpty && mostWins.isEmpty && bestAverageToPar.isEmpty }
    }
}

/// "E", "+3", "-2": score relative to par.
nonisolated func formatToPar(_ value: Int) -> String {
    value == 0 ? "E" : value > 0 ? "+\(value)" : "\(value)"
}

/// "E", "+2.5", "-0.4": an average relative to par.
nonisolated func formatToPar(_ value: Double) -> String {
    abs(value) < 0.05 ? "E" : String(format: "%+.1f", value)
}
