import Foundation

/// The SwingRemote <-> golf sim datagrams (JSON over UDP). See README.md for the full spec.
/// Version 2 adds `v`, `type`, `id` and `club` to the original v1 shot datagram, which stays
/// readable by v1 receivers (they ignore unknown keys).
nonisolated enum SimProtocol {
    static let port: UInt16 = 4242
    static let version = 2

    static let encoder = JSONEncoder()
    static let decoder = JSONDecoder()

    /// Phone -> sim. Shot datagram; the v1 fields are speed, launch, azimuth, back, side.
    struct ShotMessage: Codable, Equatable {
        var v = SimProtocol.version
        var type = "shot"
        var id: Int
        var club: String
        /// Ball speed, m/s.
        var speed: Double
        /// Launch angle, degrees.
        var launch: Double
        /// Start direction, degrees, + right.
        var azimuth: Double
        /// Backspin, rpm.
        var back: Double
        /// Sidespin, rpm, + curves right.
        var side: Double

        init(id: Int, shot: Shot) {
            self.id = id
            club = shot.club.name
            speed = Self.round(shot.ballSpeed, 2)
            launch = Self.round(shot.launch, 1)
            azimuth = Self.round(shot.azimuth, 2)
            back = Self.round(shot.backspin, 0)
            side = Self.round(shot.sidespin, 0)
        }

        private static func round(_ x: Double, _ places: Int) -> Double {
            let f = pow(10, Double(places))
            return (x * f).rounded() / f
        }
    }

    /// Phone -> sim. Discovery broadcast and heartbeat; the sim answers with a `hello`.
    struct DiscoverMessage: Codable, Equatable {
        var v = SimProtocol.version
        var type = "discover"
        var app = "SwingRemote"
    }

    /// Sim -> phone. One type for every reply; fields are present depending on `type`:
    /// hello (name, status), ack (id, status, message), result (id, carry, total, offline, surface, outcome).
    struct Reply: Codable, Equatable {
        var v: Int?
        var type: String
        var id: Int?
        var status: String?
        var name: String?
        var message: String?
        var carry: Double?
        var total: Double?
        var offline: Double?
        var surface: String?
        var outcome: String?
    }

    static func encode<T: Encodable>(_ message: T) -> Data? { try? encoder.encode(message) }
    static func decodeReply(_ data: Data) -> Reply? { try? decoder.decode(Reply.self, from: data) }
}

/// What the sim reported once the ball came to rest. Distances in yards.
nonisolated struct SimResult: Equatable, Sendable {
    var id: Int
    var carry: Double
    var total: Double
    var offline: Double
    var surface: String
    var outcome: String

    init(id: Int, carry: Double, total: Double, offline: Double = 0, surface: String, outcome: String) {
        self.id = id
        self.carry = carry
        self.total = total
        self.offline = offline
        self.surface = surface
        self.outcome = outcome
    }

    /// A `shotResult` from the game server for the shot with this id.
    init(id: Int, _ result: GameProtocol.ShotResult) {
        self.init(id: id, carry: result.carry ?? 0, total: result.total ?? 0, surface: result.lie ?? "", outcome: result.outcome)
    }

    init?(_ reply: SimProtocol.Reply) {
        guard reply.type == "result", let id = reply.id else { return nil }
        self.id = id
        carry = reply.carry ?? 0
        total = reply.total ?? 0
        offline = reply.offline ?? 0
        surface = reply.surface ?? ""
        outcome = reply.outcome ?? ""
    }
}

/// Where the last shot datagram got to.
nonisolated enum Delivery: Equatable, Sendable {
    case sending
    case received
    /// The ball was still moving in the sim; the shot was ignored.
    case busy
    case rejected(String)
    /// No ack after the retries (old receiver, firewall, or wrong host).
    case noReply
}
