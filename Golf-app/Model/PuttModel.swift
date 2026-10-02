import Foundation

/// Green speed and putt distance on flat ground: the one place the app turns ball speed into roll distance.
/// The sim has the same function with the same constants (Golf-sim `Ball/Runtime/PuttModel.cs`), so the
/// power meter and the sim agree; change both together.
///
/// A Stimpmeter releases the ball at 1.83 m/s and the green's Stimp reading is how many feet it rolls. A
/// rolling ball slows at a constant rate, so distance grows with speed squared:
/// distance = stimp (m) × (ball speed / 1.83)². The sim's green is Stimp 11.2 and it sends its own in `state`.
nonisolated enum PuttModel {
    /// Ball speed leaving a Stimpmeter ramp, m/s (6 ft/s).
    static let stimpReleaseSpeed = 1.83
    static let metersPerFoot = 0.3048
    static let metersPerYard = 0.9144
    /// The sim's green (rolling resistance 0.05 g), used until the sim reports its Stimp.
    static let defaultStimp = 11.2
    /// The sim aims its read (and the meter's target) to finish this far past the hole, metres.
    static let overshoot = 0.4

    /// Metres a putt at this ball speed (m/s) rolls on a flat green of this Stimp (feet).
    static func rollDistance(ballSpeed: Double, stimp: Double) -> Double {
        let ratio = max(0, ballSpeed) / stimpReleaseSpeed
        return stimp * metersPerFoot * ratio * ratio
    }

    /// Ball speed (m/s) that rolls this many metres on a flat green of this Stimp (feet).
    static func ballSpeed(forDistance distance: Double, stimp: Double) -> Double {
        stimpReleaseSpeed * (max(0, distance) / max(0.1, stimp * metersPerFoot)).squareRoot()
    }

    /// Metres a putter stroke at this phone rotation rate (rad/s) would roll: the same speed `Shot.from` sends.
    static func rollDistance(rate: Double, scale: Double, stimp: Double, club: Club = .putter) -> Double {
        rollDistance(ballSpeed: club.ballSpeed(rate: rate, scale: scale), stimp: stimp)
    }

    /// The sim's Stimp, or the default when it hasn't said (older sims send nothing, or 0).
    static func stimp(of state: GameProtocol.SimState?) -> Double {
        guard let stimp = state?.stimp, stimp > 0 else { return defaultStimp }
        return stimp
    }
}
