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

    static let bag: [Club] = [
        Club(name: "Driver", short: "DR", radius: 1.65, smash: 1.48, launch: 12, backspin: 2600, faceToStart: 0.75, detection: .fullSwing),
        Club(name: "3 Wood", short: "3W", radius: 1.60, smash: 1.45, launch: 11, backspin: 3600, faceToStart: 0.75, detection: .fullSwing),
        Club(name: "5 Iron", short: "5i", radius: 1.50, smash: 1.38, launch: 14, backspin: 5000, faceToStart: 0.75, detection: .fullSwing),
        Club(name: "7 Iron", short: "7i", radius: 1.45, smash: 1.33, launch: 17, backspin: 6500, faceToStart: 0.75, detection: .fullSwing),
        Club(name: "9 Iron", short: "9i", radius: 1.40, smash: 1.28, launch: 21, backspin: 8000, faceToStart: 0.75, detection: .fullSwing),
        Club(name: "Wedge", short: "PW", radius: 1.35, smash: 1.20, launch: 28, backspin: 8500, faceToStart: 0.75, detection: .wedge),
        Club(name: "Putter", short: "PT", radius: 0.90, smash: 1.60, launch: 1, backspin: 0, faceToStart: 0.9, detection: .putter),
    ]

    static var putter: Club { bag.first(where: \.isPutter)! }

    /// Ball speed (m/s) for a swing at this phone rotation rate (rad/s): rate × swing radius × scale × smash.
    func ballSpeed(rate: Double, scale: Double) -> Double { headSpeed(rate: rate, scale: scale) * smash }

    /// Clubhead speed (m/s) for a swing at this phone rotation rate (rad/s).
    func headSpeed(rate: Double, scale: Double) -> Double { rate * radius * scale }

    /// Club at `index`, clamped into the bag (stored indices can outlive a shorter table).
    static func at(_ index: Int) -> Club { bag[min(max(index, 0), bag.count - 1)] }
}
