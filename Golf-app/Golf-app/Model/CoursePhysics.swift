import Foundation

/// The game server's live ball-physics profile (`/api/physics`, Game-server docs/PROTOCOL-physics.md) and the
/// surfaces the app lets you tune. The server only stores overrides; a field it leaves out uses the sim's built-in
/// value, so the app keeps the one copy of those defaults it needs to show them.
nonisolated enum CoursePhysics {
    /// A tunable part of a surface's ground response.
    enum Field: String, CaseIterable, Sendable {
        /// Rolling resistance (fraction of g): lower rolls farther. Shown as Stimp feet on the green.
        case rolling
        /// Bounce energy kept, relative to a firm green (1).
        case restitution
        /// Sliding friction during a bounce: how much the turf grabs the ball.
        case friction
    }

    /// One value per field.
    struct Values: Equatable, Sendable {
        var rolling: Double
        var restitution: Double
        var friction: Double

        subscript(field: Field) -> Double {
            get {
                switch field {
                case .rolling: rolling
                case .restitution: restitution
                case .friction: friction
                }
            }
            set {
                switch field {
                case .rolling: rolling = newValue
                case .restitution: restitution = newValue
                case .friction: friction = newValue
                }
            }
        }
    }

    /// A row in the app: one or more sim surfaces tuned together (tee follows fairway; scrub and woods follow
    /// native). The first surface is the one whose override the row shows.
    struct Group: Identifiable, Equatable, Sendable {
        let title: String
        let surfaces: [String]
        /// The sim's built-in values for the first surface.
        let defaults: Values
        /// Slider range for rolling (the server accepts 0.02...3).
        let rollRange: ClosedRange<Double>

        var id: String { surfaces[0] }
        var isGreen: Bool { id == "green" }
    }

    /// The sim's built-in ground response, from Golf-sim Assets/GolfSim/Ball/Settings/BallPhysics.asset (the same
    /// numbers as BallPhysicsSettings.DefaultSurfaces()). The one copy on the app side: update it when the asset
    /// changes. Scrub and woods have their own (0.4, 0.75, 1.0); they keep them until native is changed.
    static let groups: [Group] = [
        Group(title: "Green", surfaces: ["green"], defaults: Values(rolling: 0.06, restitution: 1.0, friction: 0.4),
              rollRange: rolling(stimp: stimpRange.upperBound)...rolling(stimp: stimpRange.lowerBound)),
        Group(title: "Fairway & tee", surfaces: ["fairway", "tee"], defaults: Values(rolling: 0.11, restitution: 0.9, friction: 0.45),
              rollRange: 0.04...0.5),
        Group(title: "Rough", surfaces: ["rough"], defaults: Values(rolling: 0.7, restitution: 0.45, friction: 0.7),
              rollRange: 0.2...2),
        Group(title: "Native, scrub & woods", surfaces: ["native", "scrub", "woods"], defaults: Values(rolling: 0.8, restitution: 0.5, friction: 0.7),
              rollRange: 0.2...2),
        Group(title: "Bunker", surfaces: ["bunker"], defaults: Values(rolling: 1.5, restitution: 0.2, friction: 0.8),
              rollRange: 0.5...3),
    ]

    static let bounceRange = 0.0...1.2
    static let gripRange = 0.0...1.5
    /// Green speed the Roll slider covers, Stimp feet.
    static let stimpRange = 6.0...14.0
    /// The speed (m/s) the Roll caption quotes a roll distance for: about a ball landing and starting to run.
    static let rollCaptionSpeed = 4.0

    // MARK: Units

    static let gravity = 9.81

    /// Stimp feet of a green with this rolling resistance (the sim's PuttModel.StimpFeet).
    static func stimp(rolling: Double) -> Double {
        PuttModel.stimpReleaseSpeed * PuttModel.stimpReleaseSpeed / (2 * max(1e-4, rolling) * gravity) / PuttModel.metersPerFoot
    }

    /// Rolling resistance that gives this Stimp (feet).
    static func rolling(stimp: Double) -> Double {
        PuttModel.stimpReleaseSpeed * PuttModel.stimpReleaseSpeed / (2 * max(0.1, stimp) * PuttModel.metersPerFoot * gravity)
    }

    /// Metres a ball rolling at `speed` m/s runs on flat ground with this rolling resistance.
    static func rollDistance(speed: Double, rolling: Double) -> Double {
        speed * speed / (2 * max(1e-4, rolling) * gravity)
    }

    // MARK: Profile <-> rows

    /// What each row shows: the server's override for the row's first surface, else the built-in value.
    static func values(from profile: Profile) -> [Group.ID: Values] {
        Dictionary(uniqueKeysWithValues: groups.map { group in
            let entry = profile.surfaces.first { $0.surface == group.id }
            var values = group.defaults
            for field in Field.allCases {
                if let value = entry?[field] { values[field] = value }
            }
            return (group.id, values)
        })
    }

    /// The PUT body that makes the server match the rows: every surface of every row, a field at its built-in
    /// value sent as null (no override) so the sim's own numbers, and a later change of them, still apply.
    static func update(from values: [Group.ID: Values]) -> Profile {
        Profile(surfaces: groups.flatMap { group in
            let row = values[group.id] ?? group.defaults
            var entry = Profile.Entry(surface: "")
            for field in Field.allCases {
                let value = (row[field] * 10_000).rounded() / 10_000
                entry[field] = abs(value - group.defaults[field]) < 1e-6 ? nil : value
            }
            return group.surfaces.map { surface in
                var copy = entry
                copy.surface = surface
                return copy
            }
        })
    }

    /// The JSON of GET/PUT/DELETE `/api/physics`: `{"surfaces": [{"surface": "green", "rolling": 0.07}, ...]}`.
    /// Decoding: an absent field is the built-in value. Encoding (PUT): every field is written, nil as null
    /// (null clears the override).
    struct Profile: Codable, Equatable, Sendable {
        struct Entry: Codable, Equatable, Sendable {
            var surface: String
            var rolling: Double?
            var restitution: Double?
            var friction: Double?

            subscript(field: Field) -> Double? {
                get {
                    switch field {
                    case .rolling: rolling
                    case .restitution: restitution
                    case .friction: friction
                    }
                }
                set {
                    switch field {
                    case .rolling: rolling = newValue
                    case .restitution: restitution = newValue
                    case .friction: friction = newValue
                    }
                }
            }

            func encode(to encoder: Encoder) throws {
                var container = encoder.container(keyedBy: CodingKeys.self)
                try container.encode(surface, forKey: .surface)
                try container.encode(rolling, forKey: .rolling) // nil -> null: back to the built-in value
                try container.encode(restitution, forKey: .restitution)
                try container.encode(friction, forKey: .friction)
            }
        }

        var surfaces: [Entry]

        /// True when no surface has an override.
        var isDefault: Bool { surfaces.allSatisfy { entry in Field.allCases.allSatisfy { entry[$0] == nil } } }
    }
}
