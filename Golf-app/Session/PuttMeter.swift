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
    /// Metres to the hole when the putt was struck, until its result arrives.
    private var pendingDistance: Double?

    /// What the meter fills to: the putt sent, else the live stroke.
    var shown: Double { struck ?? live }

    /// A stroke started: clear the last one.
    mutating func begin() {
        live = 0
        peak = 0
        struck = nil
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

    /// The top of the meter for this target and distance (metres): room above both, at least 3 m.
    static func range(target: Double?, distance: Double?) -> Double {
        max(3, (target ?? 0) * 1.5, (distance ?? 0) * 1.5)
    }
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
