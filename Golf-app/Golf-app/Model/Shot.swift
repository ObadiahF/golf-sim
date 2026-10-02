import Foundation

nonisolated enum Units {
    static let mphPerMetersPerSecond = 2.23694
    static func mph(_ metersPerSecond: Double) -> Double { metersPerSecond * mphPerMetersPerSecond }
}

/// The moment the swing detector picked as impact.
nonisolated struct Impact: Equatable, Sendable {
    /// Phone rotation rate at impact, rad/s.
    var rate: Double
    /// Face angle relative to address, degrees (raw yaw, before the face-sign setting).
    var face: Double
    /// How far from the address pose the phone was at that sample, degrees.
    var angle: Double
    var time: Double
}

/// Launch conditions for one shot, in the sim's units (m/s, degrees, rpm).
nonisolated struct Shot: Equatable, Sendable {
    static let maxFace = 15.0
    static let sidespinPerFaceDegree = 180.0

    var club: Club
    var clubSpeed: Double
    var ballSpeed: Double
    var launch: Double
    /// Start direction, degrees, + right.
    var azimuth: Double
    var backspin: Double
    /// rpm, + curves right.
    var sidespin: Double
    /// Face angle at impact after the face-sign setting, degrees, + open (right).
    var face: Double

    var ballSpeedMph: Double { Units.mph(ballSpeed) }
    var clubSpeedMph: Double { Units.mph(clubSpeed) }

    /// Turns a detected impact into a shot: clubhead speed = rotation rate x swing radius x scale.
    /// `faceSign` is +1 or -1 (flip it if fades and draws come out backwards), or 0 for a straight shot at the aim.
    static func from(_ impact: Impact, club: Club, scale: Double, faceSign: Double) -> Shot {
        let head = club.headSpeed(rate: impact.rate, scale: scale)
        let face = min(maxFace, max(-maxFace, impact.face * faceSign))
        return Shot(
            club: club,
            clubSpeed: head,
            ballSpeed: club.ballSpeed(rate: impact.rate, scale: scale),
            launch: club.launch,
            azimuth: face * club.faceToStart,
            backspin: club.backspin,
            sidespin: club.isPutter ? 0 : face * sidespinPerFaceDegree,
            face: face
        )
    }
}
