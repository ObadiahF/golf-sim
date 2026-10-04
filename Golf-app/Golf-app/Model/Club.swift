import Foundation

/// How sensitive swing detection is for a club. Angles are degrees away from the address pose,
/// rates are the phone's rotation rate in rad/s.
nonisolated struct SwingThresholds: Hashable, Sendable {
    /// Rotation rate that counts as a swing starting.
    var startRate: Double
    /// The swing's peak rate must reach `startRate * peakFactor` to count as a real swing.
    var peakFactor: Double
    /// The backswing must take the phone at least this far from address.
    var awayAngle: Double
    /// After a shot, the phone must be tilted back within this of address (and still) to re-arm.
    var rearmAngle: Double
    /// Below this rotation rate the phone counts as held still.
    var stillRate: Double

    static let fullSwing = SwingThresholds(startRate: 6, peakFactor: 1.6, awayAngle: 45, rearmAngle: 25, stillRate: 0.8)
    static let wedge = SwingThresholds(startRate: 5, peakFactor: 1.6, awayAngle: 35, rearmAngle: 25, stillRate: 0.8)
    /// Putts are slow and short: low rates, small angles. A 1 m putt peaks around 0.75 rad/s at the phone and a
    /// 30 cm tap around 0.4; these let a ~20 cm putt fire (peak 0.31 rad/s). Tremor at address stays under 0.25.
    static let putter = SwingThresholds(startRate: 0.25, peakFactor: 1.25, awayAngle: 2, rearmAngle: 6, stillRate: 0.25)

    /// More sensitive (above 1) or less (below 1): the speed and backswing a swing needs divide by `sensitivity`.
    /// What counts as held still doesn't change.
    func scaled(sensitivity: Double) -> SwingThresholds {
        var t = self
        t.startRate /= sensitivity
        t.awayAngle /= sensitivity
        return t
    }
}

/// One club in the bag. The phone measures speed and face angle; launch and spin come from
/// this table, assuming good contact.
nonisolated struct Club: Identifiable, Hashable, Sendable {
    let name: String
    /// Two-letter label for the picker chip.
    let short: String
    /// Swing radius in m, from the hands' pivot to the clubhead.
    let radius: Double
    /// Ball speed / clubhead speed.
    let smash: Double
    /// Vertical launch angle, degrees.
    let launch: Double
    /// Backspin, rpm.
    let backspin: Double
    /// Fraction of the face angle that becomes start direction (the rest is path).
    let faceToStart: Double
    let detection: SwingThresholds

    var id: String { name }
    var isPutter: Bool { backspin == 0 }

    /// The 14 clubs, in the same order and with the same names as the sim's `Clubs.Bag`.
    static let bag: [Club] = [
        Club(name: "Driver", short: "DR", radius: 1.65, smash: 1.48, launch: 12, backspin: 2600, faceToStart: 0.75, detection: .fullSwing),
        Club(name: "3 Wood", short: "3W", radius: 1.60, smash: 1.45, launch: 11, backspin: 3600, faceToStart: 0.75, detection: .fullSwing),
        Club(name: "5 Wood", short: "5W", radius: 1.58, smash: 1.44, launch: 11.5, backspin: 4300, faceToStart: 0.75, detection: .fullSwing),
        Club(name: "4 Hybrid", short: "4H", radius: 1.55, smash: 1.42, launch: 12.5, backspin: 4400, faceToStart: 0.75, detection: .fullSwing),
        Club(name: "5 Iron", short: "5i", radius: 1.50, smash: 1.38, launch: 14, backspin: 5000, faceToStart: 0.75, detection: .fullSwing),
        Club(name: "6 Iron", short: "6i", radius: 1.48, smash: 1.36, launch: 15.5, backspin: 5800, faceToStart: 0.75, detection: .fullSwing),
        Club(name: "7 Iron", short: "7i", radius: 1.45, smash: 1.33, launch: 17, backspin: 6500, faceToStart: 0.75, detection: .fullSwing),
        Club(name: "8 Iron", short: "8i", radius: 1.42, smash: 1.30, launch: 19, backspin: 7300, faceToStart: 0.75, detection: .fullSwing),
        Club(name: "9 Iron", short: "9i", radius: 1.40, smash: 1.28, launch: 21, backspin: 8000, faceToStart: 0.75, detection: .fullSwing),
        Club(name: "Pitching Wedge", short: "PW", radius: 1.35, smash: 1.20, launch: 28, backspin: 8500, faceToStart: 0.75, detection: .wedge),
        Club(name: "Gap Wedge", short: "GW", radius: 1.33, smash: 1.16, launch: 30, backspin: 9000, faceToStart: 0.75, detection: .wedge),
        Club(name: "Sand Wedge", short: "SW", radius: 1.30, smash: 1.12, launch: 33, backspin: 9500, faceToStart: 0.75, detection: .wedge),
        Club(name: "Lob Wedge", short: "LW", radius: 1.28, smash: 1.08, launch: 36, backspin: 9800, faceToStart: 0.75, detection: .wedge),
        Club(name: "Putter", short: "PT", radius: 0.90, smash: 1.60, launch: 1, backspin: 0, faceToStart: 0.9, detection: .putter),
    ]

    /// Names the bag used to have, and what they are called now (saved settings and older sims).
    static let renamed = ["Wedge": "Pitching Wedge"]

    /// The index of the club with this name (or an old name for it), if it's in the bag.
    static func index(named name: String) -> Int? {
        let current = renamed[name] ?? name
        return bag.firstIndex { $0.name == current }
    }

    static var putter: Club { bag.first(where: \.isPutter)! }

    /// Ball speed (m/s) for a swing at this phone rotation rate (rad/s): rate × swing radius × scale × smash.
    func ballSpeed(rate: Double, scale: Double) -> Double { headSpeed(rate: rate, scale: scale) * smash }

    /// Clubhead speed (m/s) for a swing at this phone rotation rate (rad/s).
    func headSpeed(rate: Double, scale: Double) -> Double { rate * radius * scale }

    /// Club at `index`, clamped into the bag (stored indices can outlive a shorter table).
    static func at(_ index: Int) -> Club { bag[min(max(index, 0), bag.count - 1)] }
}
