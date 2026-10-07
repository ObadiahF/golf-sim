import Foundation

/// The putting power meter: how far the stroke in progress would roll (live from the phone's rotation rate),
/// the strongest moment of it, the putt that was sent, and how that putt finished against its distance.
/// Pure value type so tests can drive it.
nonisolated struct PuttMeter: Equatable, Sendable {
    /// How a putt finished.
    struct Result: Equatable, Sendable {
        /// Metres the ball went.
        var rolled: Double
        /// Metres to the hole when it was struck.
        var distance: Double
        var holed: Bool

        /// "Putted 4.2 m of 5.0 m", or "Holed from 5.0 m!".
        var summary: String {
            holed ? String(format: "Holed from %.1f m!", distance) : String(format: "Putted %.1f m of %.1f m", rolled, distance)
        }
    }

    /// Metres the stroke would roll at the current rotation rate.
    private(set) var live = 0.0
    /// The most the stroke in progress has reached.
    private(set) var peak = 0.0
    /// Roll estimate of the putt that was sent; nil while a stroke is in progress (or before the first putt).
    private(set) var struck: Double?
    /// The last putt's result, once the sim reports it.
    private(set) var result: Result?
    /// Metres at the top of the meter, set when a putt is set up and kept while it is aimed (the plays-as target
    /// moves with the aim; the scale doesn't).
    private(set) var range = Self.range(target: nil, distance: nil)
    /// Metres to the hole when the putt was struck, until its result arrives.
    private var pendingDistance: Double?
    /// The hole and Play screen of the sim state last followed, to notice a new hole or leaving the putting view.
    private var followedHole: Int?
    private var wasPutting = false
    /// The stroke the sim last said it would take (player, hole, strokes so far), to notice the next one.
    private var readyStroke: Stroke?

    /// Who is up and how many strokes they have taken on which hole: a new value is a new stroke.
    private struct Stroke: Equatable, Sendable {
        var player: String?
        var hole: Int?
        var strokes: Int?
    }

    /// What the meter fills to: the putt sent, else the live stroke.
    var shown: Double { struck ?? live }

    /// A stroke started: clear the last one.
    mutating func begin() {
        live = 0
        peak = 0
        struck = nil
    }

    /// Back to empty, so the last putt's strength doesn't read as a preset for the next one (new turn, address).
    /// A putt still waiting for its result keeps waiting; `clearingResult` also drops "Putted 4.2 m of 5.0 m".
    mutating func reset(clearingResult: Bool = false) {
        begin()
        if clearingResult { result = nil }
    }

    /// Follows the sim's state: a new hole clears the meter and the last putt's result, leaving the putting
    /// view clears the meter, and so does a new stroke: the sim taking a swing again (`canShoot`) with another
    /// player up or another stroke count, which is how the same player's next putt starts (the sim sends no
    /// `turn` between one player's own strokes). While the putt rolls (`canShoot: false`) the meter keeps it.
    /// Entering the putting view, a new hole or a new stroke sets up a putt: the meter's `range` is fixed for it.
    mutating func follow(_ state: GameProtocol.SimState?) {
        let putting = PlayScreen.of(state) == .putting
        var setUp = putting && !wasPutting
        if let hole = state?.hole, hole > 0, hole != followedHole {
            followedHole = hole
            reset(clearingResult: true)
            setUp = putting
        } else if wasPutting, !putting {
            reset()
        } else if let stroke = Self.readyStroke(state), let readyStroke, stroke != readyStroke {
            reset()
            setUp = putting
        }
        if setUp { range = Self.range(target: state?.puttPlaysAs, distance: state?.puttDistance) }
        readyStroke = Self.readyStroke(state) ?? readyStroke
        wasPutting = putting
    }

    /// The stroke the sim would take now; nil while it can't take one (or isn't playing a hole).
    private static func readyStroke(_ state: GameProtocol.SimState?) -> Stroke? {
        guard let state, state.acceptsShots else { return nil }
        return Stroke(player: state.player, hole: state.hole, strokes: state.strokes)
    }

    /// One motion sample during the stroke, as a roll distance.
    mutating func track(distance: Double) {
        live = distance
        peak = max(peak, distance)
    }

    /// The putt was sent with this roll estimate; `toHole` (from the sim's state) is kept for the result.
    mutating func strike(distance: Double, toHole: Double?) {
        struck = distance
        live = distance
        peak = max(peak, distance)
        pendingDistance = toHole
        result = nil
    }

    /// The sim's shot result for the putt that was sent (yards); ignored if no putt is waiting for one.
    mutating func finish(_ shot: GameProtocol.ShotResult) {
        guard let distance = pendingDistance else { return }
        pendingDistance = nil
        let holed = shot.holed == true || shot.lie == "holed"
        result = Result(rolled: holed ? distance : (shot.total ?? 0) * PuttModel.metersPerYard, distance: distance, holed: holed)
    }

    /// The sim didn't take the putt: stop waiting for its result and let the meter follow the next stroke.
    mutating func cancel() {
        pendingDistance = nil
        struck = nil
    }

    /// The top of the meter for this target and distance (metres): room above both, at least 3 m, rounded up to
    /// a tick so the scale reads in whole steps.
    static func range(target: Double?, distance: Double?) -> Double {
        let top = max(3, (target ?? 0) * 1.5, (distance ?? 0) * 1.5)
        let step = tickStep(top)
        return (top / step).rounded(.up) * step
    }

    /// Metres between the meter's ticks: one, or two on long meters.
    static func tickStep(_ range: Double) -> Double { range > 12 ? 2 : 1 }
}

/// Which screen the Play tab shows for the sim's state.
nonisolated enum PlayScreen: Equatable, Sendable {
    /// TV remote: menus, scorecards, loading.
    case remote
    /// The club wheel, aim and swing.
    case gameplay
    /// The putting view: distance, slope and the power meter.
    case putting

    static func of(_ state: GameProtocol.SimState?) -> PlayScreen {
        guard let state, state.isGame else { return .remote }
        return state.isPutting ? .putting : .gameplay
    }
}
