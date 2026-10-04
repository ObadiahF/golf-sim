import Foundation

/// REST client for the game server (`/api`, bearer token): start games, scorecards, players, leaderboard, physics.
nonisolated struct GameAPI: Sendable {
    enum APIError: LocalizedError, Equatable {
        case noServer
        case server(status: Int, message: String)
        case badResponse

        var errorDescription: String? {
            switch self {
            case .noServer: "No game server address. Set it in Settings."
            case .server(_, let message): message
            case .badResponse: "Unexpected reply from the server"
            }
        }
    }

    let baseURL: URL
    var token = AppConfig.serverToken
    var session: URLSession = .shared
    /// The room code from Settings (normalized); "" = the default room. Scopes starting and finding the current game.
    var room = ""

    /// Starts a game with these players in turn order in this room (abandoning only this room's game in progress);
    /// the server tells the room's sim to start it.
    func startGame(players: [String], holes: Int) async throws -> GameView {
        struct Body: Encodable { var players: [String]; var holes: Int }
        return try await request("games" + AppConfig.roomQuery(room), method: "POST", body: Body(players: players, holes: holes))
    }

    /// This room's game in progress, or nil.
    func currentGame() async throws -> GameView? {
        let data = try await send("games/current" + AppConfig.roomQuery(room), method: "GET", body: NoBody?.none) // 204 when there is none
        return data.isEmpty ? nil : try decode(data)
    }

    func game(id: Int) async throws -> GameView { try await request("games/\(id)") }

    /// Ends a game in progress early (abandoned); the sim returns to its menu.
    func endGame(id: Int) async throws -> GameView {
        struct Body: Encodable { var status = "ABANDONED" }
        return try await request("games/\(id)/end", method: "POST", body: Body())
    }

    /// Most recent games first.
    func recentGames(limit: Int = 10) async throws -> [GameView] { try await request("games?limit=\(limit)") }

    func players() async throws -> [Stats.Player] { try await request("players") }

    func leaderboard() async throws -> Stats.Leaderboard { try await request("leaderboard") }

    /// The live ball-physics profile (only the overrides; see `CoursePhysics`).
    func physics() async throws -> CoursePhysics.Profile { try await request("physics") }

    /// Sets (number) or clears (nil) the listed fields; the server pushes the result to the sim. Returns the new profile.
    func savePhysics(_ update: CoursePhysics.Profile) async throws -> CoursePhysics.Profile {
        try await request("physics", method: "PUT", body: update)
    }

    /// Clears every override: the sim goes back to its built-in values.
    func resetPhysics() async throws -> CoursePhysics.Profile {
        try decode(try await send("physics", method: "DELETE", body: NoBody?.none))
    }

    // MARK: Plumbing

    private struct ErrorBody: Decodable { var message: String? }
    private struct NoBody: Encodable {}

    private func request<T: Decodable>(_ path: String) async throws -> T {
        try decode(try await send(path, method: "GET", body: NoBody?.none))
    }

    private func request<T: Decodable, B: Encodable>(_ path: String, method: String, body: B) async throws -> T {
        try decode(try await send(path, method: method, body: body))
    }

    /// Sends the request and returns the body of a 2xx reply; anything else throws `APIError.server`.
    private func send<B: Encodable>(_ path: String, method: String, body: B?) async throws -> Data {
        guard let url = URL(string: "api/" + path, relativeTo: baseURL) else { throw APIError.badResponse }
        var request = URLRequest(url: url, timeoutInterval: 8)
        request.httpMethod = method
        request.setValue("Bearer \(token)", forHTTPHeaderField: "Authorization")
        request.setValue("application/json", forHTTPHeaderField: "Accept")
        if let body {
            request.setValue("application/json", forHTTPHeaderField: "Content-Type")
            request.httpBody = try GameProtocol.encoder.encode(body)
        }
        let (data, response) = try await session.data(for: request)
        guard let http = response as? HTTPURLResponse else { throw APIError.badResponse }
        guard (200..<300).contains(http.statusCode) else {
            let message = (try? GameProtocol.decoder.decode(ErrorBody.self, from: data))?.message
            throw APIError.server(status: http.statusCode, message: message ?? "Server error \(http.statusCode)")
        }
        return data
    }

    private func decode<T: Decodable>(_ data: Data) throws -> T {
        do { return try GameProtocol.decoder.decode(T.self, from: data) } catch { throw APIError.badResponse }
    }
}

extension GameAPI {
    /// The API on the PC from Settings, or nil when no host is known yet.
    static func forSettings(_ settings: AppSettings) -> GameAPI? {
        settings.serverURL.map { GameAPI(baseURL: $0, room: settings.room) }
    }
}
