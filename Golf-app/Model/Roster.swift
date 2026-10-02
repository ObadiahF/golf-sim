import Foundation

/// Rules for the player list, matching the server's (8 players, 40 characters, unique names).
nonisolated enum Roster {
    static let maxPlayers = 8
    static let maxNameLength = 40

    enum Problem: Error, Equatable {
        case empty, tooLong, duplicate, full

        var message: String {
            switch self {
            case .empty: "Type a name"
            case .tooLong: "Names can be up to \(Roster.maxNameLength) characters"
            case .duplicate: "That name is already playing"
            case .full: "Up to \(Roster.maxPlayers) players"
            }
        }
    }

    /// The trimmed name if it can join `players`, or why not.
    static func validate(_ name: String, joining players: [String]) -> Result<String, Problem> {
        let name = name.trimmingCharacters(in: .whitespacesAndNewlines)
        if name.isEmpty { return .failure(.empty) }
        if name.count > maxNameLength { return .failure(.tooLong) }
        if players.contains(where: { $0.caseInsensitiveCompare(name) == .orderedSame }) { return .failure(.duplicate) }
        if players.count >= maxPlayers { return .failure(.full) }
        return .success(name)
    }
}
