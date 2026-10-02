import SwiftUI

/// The live or last scorecard, and the all-time leaderboard, from the game server.
struct ScoresView: View {
    enum Page: String, CaseIterable { case scorecard = "Scorecard", leaderboard = "Leaderboard" }

    let session: SwingSession
    @State private var page = Page.scorecard
    @State private var lastGame: GameView?
    @State private var board: Stats.Leaderboard?
    @State private var players: [Stats.Player] = []
    @State private var problem: String?

    /// The live scorecard from the WebSocket wins over the last one fetched.
    private var card: GameView? { session.game.game ?? lastGame }

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
            case .scorecard: lastGame = try await api.recentGames(limit: 1).first
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
