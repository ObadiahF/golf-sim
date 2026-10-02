import SwiftUI

/// The live or last scorecard, and the all-time leaderboard, from the game server.
struct ScoresView: View {
    enum Page: String, CaseIterable { case scorecard = "Scorecard", leaderboard = "Leaderboard" }

    let session: SwingSession
    @State private var page = Page.scorecard
    @State private var recentGames: [GameView] = []
    @State private var board: Stats.Leaderboard?
    @State private var players: [Stats.Player] = []
    @State private var problem: String?

    private var card: GameView? { Self.shownGame(live: session.game.game, recent: recentGames) }

    /// How many recent games to look through for the last finished one.
    static let recentLimit = 10

    /// The game in progress (the live scorecard from the WebSocket first), else the latest finished game, so an
    /// abandoned game (End game, or a new game started over it) doesn't hide the last real round. With no
    /// finished game, the latest one of any kind.
    static func shownGame(live: GameView?, recent: [GameView]) -> GameView? {
        let games = [live].compactMap { $0 } + recent.filter { $0.id != live?.id } // the live copy is newer
        return games.first(where: \.isInProgress)
            ?? games.filter(\.isFinished).max { $0.id < $1.id }
            ?? games.max { $0.id < $1.id }
    }

    var body: some View {
        ZStack {
            Theme.background
            VStack(spacing: 14) {
                AppHeader(title: "Scores", session: session)
                Picker("Page", selection: $page) {
                    ForEach(Page.allCases, id: \.self) { Text($0.rawValue) }
                }
                .pickerStyle(.segmented)
                ScrollView {
                    VStack(spacing: 14) {
                        if let problem {
                            Text(problem).font(.system(size: 15, weight: .semibold, design: .rounded)).foregroundStyle(Theme.warn)
                        }
                        switch page {
                        case .scorecard: scorecard
                        case .leaderboard: leaderboard
                        }
                    }
                }
                .refreshable { await load() }
            }
            .padding(.horizontal, 16)
        }
        // Reload when the page changes and whenever the server sends a scorecard or a game finishes.
        .task(id: Reload(page: page, revision: session.game.scoresRevision)) { await load() }
    }

    private struct Reload: Hashable {
        var page: Page
        var revision: Int
    }

    @ViewBuilder private var scorecard: some View {
        if let card {
            ScorecardTable(game: card).card(padding: 14)
        } else {
            empty("No games yet. Add players and start one.")
        }
    }

    @ViewBuilder private var leaderboard: some View {
        if let board, !(board.isEmpty && players.allSatisfy { $0.finishedRounds == 0 }) {
            LeaderboardView(board: board, players: players)
        } else {
            empty("No finished rounds yet. Finish a round to get on the board.")
        }
    }

    private func empty(_ text: String) -> some View {
        Text(text)
            .font(.system(size: 16, weight: .medium, design: .rounded))
            .foregroundStyle(Theme.muted)
            .frame(maxWidth: .infinity, minHeight: 120)
            .card()
    }

    private func load() async {
        guard let api = GameAPI.forSettings(session.settings) else {
            problem = GameAPI.APIError.noServer.errorDescription
            return
        }
        do {
            switch page {
            case .scorecard: recentGames = try await api.recentGames(limit: Self.recentLimit)
            case .leaderboard:
                async let board = api.leaderboard()
                async let players = api.players()
                (self.board, self.players) = try await (board, players)
            }
            problem = nil
        } catch is CancellationError {
        } catch {
            problem = error.localizedDescription
        }
    }
}
