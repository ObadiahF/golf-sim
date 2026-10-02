import Foundation
import Observation

/// User settings, persisted in UserDefaults.
@Observable
final class AppSettings {
    private enum Key {
        static let host = "host"
        static let clubIndex = "clubIndex"
        static let scale = "swingScale"
        static let puttScale = "puttScale"
        static let flipFace = "flipFace"
        static let shapeShots = "shapeShots"
        static let server = "server"
        static let players = "players"
        static let holes = "roundHoles"
    }

    @ObservationIgnored private let defaults: UserDefaults

    /// The PC running the sim: last discovered or typed (IP or host name). Empty = not known yet.
    var host: String { didSet { defaults.set(host, forKey: Key.host) } }
    var clubIndex: Int { didSet { defaults.set(clubIndex, forKey: Key.clubIndex) } }
    /// Multiplies the measured clubhead speed (raise it for safe half swings).
    var scale: Double { didSet { defaults.set(scale, forKey: Key.scale) } }
    /// Multiplies the measured putter head speed (raise it if putts come up short with a normal stroke).
    var puttScale: Double { didSet { defaults.set(puttScale, forKey: Key.puttScale) } }
    /// Flip if fades and draws come out backwards (depends on how the phone is held).
    var flipFace: Bool { didSet { defaults.set(flipFace, forKey: Key.flipFace) } }
    /// Curve and push/pull shots by the face angle the phone measured. Off by default: the phone alone can't
    /// measure the face reliably (it turns ~20 degrees between readings at impact), so shots fly straight at the aim.
    var shapeShots: Bool { didSet { defaults.set(shapeShots, forKey: Key.shapeShots) } }
    /// A local or LAN game server ("192.168.1.20:9000"); empty = the hosted server.
    var server: String { didSet { defaults.set(server, forKey: Key.server) } }
    /// Player names for the next round, in turn order (only kept on this phone).
    var players: [String] { didSet { defaults.set(players, forKey: Key.players) } }
    /// Holes in the next round: 9 or 18.
    var holes: Int { didSet { defaults.set(holes, forKey: Key.holes) } }

    /// The game server's base URL: the one typed in Settings, else the hosted server.
    var serverURL: URL? { AppConfig.serverURL(server.isEmpty ? defaultServer : server) }

    /// The hosted game server, unless Settings names a local or LAN one.
    var defaultServer: String { AppConfig.hostedServer }

    var club: Club { Club.at(clubIndex) }
    /// The sample's default was -1 (phone screen facing the golfer); flipping makes it +1.
    var faceSign: Double { flipFace ? 1 : -1 }
    /// What the measured face angle is multiplied by: the face sign, or 0 (straight shots) unless shaping is on.
    var faceFactor: Double { shapeShots ? faceSign : 0 }

    static let scaleRange = 1.0...2.5
    static let puttScaleRange = 0.5...2.0

    /// The speed scale for this club: the putt scale for the putter, the swing scale for the rest.
    func scale(for club: Club) -> Double { club.isPutter ? puttScale : scale }

    init(defaults: UserDefaults = .standard) {
        self.defaults = defaults
        host = defaults.string(forKey: Key.host) ?? ""
        clubIndex = defaults.integer(forKey: Key.clubIndex)
        let stored = defaults.double(forKey: Key.scale)
        scale = Self.scaleRange.contains(stored) ? stored : 1
        let storedPutt = defaults.double(forKey: Key.puttScale)
        puttScale = Self.puttScaleRange.contains(storedPutt) ? storedPutt : 1
        flipFace = defaults.bool(forKey: Key.flipFace)
        shapeShots = defaults.bool(forKey: Key.shapeShots)
        server = defaults.string(forKey: Key.server) ?? ""
        players = defaults.stringArray(forKey: Key.players) ?? []
        let holes = defaults.integer(forKey: Key.holes)
        self.holes = AppConfig.roundLengths.contains(holes) ? holes : AppConfig.roundLengths[0]
    }
}
