import Foundation

/// A practice facility on the TV (the sim's `state.practice`), picked from the sim's main menu. The phone needs no
/// mode of its own for them: the range plays in the gameplay view and the putting green in the putting view, as
/// `PlayScreen.of(state)` already chooses; this only changes their titles and stats.
nonisolated enum PracticeFacility: String, Sendable {
    /// The driving range: flags from 50 to 300 yards, the ball teed up again after every shot.
    case range
    /// The putting green: a cycle of putts to six cups, the ball moved to the next putt after each.
    case puttingGreen

    var title: String {
        switch self {
        case .range: "Driving Range"
        case .puttingGreen: "Putting Green"
        }
    }
}

extension GameProtocol.SimState {
    /// The facility on the TV, or nil (a round, the practice hole, an older sim).
    var facility: PracticeFacility? { practice.flatMap(PracticeFacility.init(rawValue:)) }

    /// The play screens' title: the player up in a round, else the facility, else "Practice".
    var playTitle: String { player ?? facility?.title ?? "Practice" }

    /// "Made 3 of 7" on the putting green ("No putts yet" before the first); nil anywhere else.
    var madeSummary: String? {
        guard facility == .puttingGreen else { return nil }
        let tried = attempts ?? 0
        return tried == 0 ? "No putts yet" : "Made \(made ?? 0) of \(tried)"
    }
}
