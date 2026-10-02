import SwiftUI

/// All-time stats: every player ranked by handicap (then average), and the server's top-10 lists.
struct LeaderboardView: View {
    let board: Stats.Leaderboard
    let players: [Stats.Player]

    var body: some View {
        VStack(spacing: 14) {
            if !players.isEmpty {
                let ranked = Self.ranked(players)
                PlayerTable(title: "Players · average to par per 18 holes", players: ranked, columns: PlayerTable.form).card(padding: 14)
                PlayerTable(title: "Rounds · 9 and 18 holes", players: ranked, columns: PlayerTable.rounds).card(padding: 14)
            }
            if let handicaps = board.lowestHandicap, !handicaps.isEmpty {
                LeaderSection(title: "Lowest handicap", rows: handicaps.map {
                    ($0.player, String(format: "%.1f", $0.handicap), "\($0.finishedRounds) rounds")
                })
            }
            if let birdies = board.mostBirdies, !birdies.isEmpty {
                LeaderSection(title: "Most birdies or better", rows: birdies.map {
                    ($0.player, "\($0.birdiesOrBetter)", "in \($0.holesPlayed) holes")
                })
            }
            ForEach(Self.bestRoundLengths(board.bestRounds), id: \.self) { holes in
                LeaderSection(title: "Best \(holes)-hole rounds", rows: Self.bestRounds(board.bestRounds, holes: holes).map {
                    ($0.player, "\($0.total) (\(formatToPar($0.toPar)))", "game #\($0.gameId)")
                })
            }
            LeaderSection(title: "Most wins", rows: board.mostWins.map {
                ($0.player, "\($0.wins)", "of \($0.finishedRounds) rounds")
            })
        }
    }

    /// The round lengths to list best rounds for: 9 and 18 holes, each only once someone has such a round (just
    /// the 9-hole list while nobody has any, for its "—").
    static func bestRoundLengths(_ rounds: [Stats.Round]) -> [Int] {
        let lengths = AppConfig.roundLengths.filter { holes in rounds.contains { $0.holesCount == holes } }
        return lengths.isEmpty ? Array(AppConfig.roundLengths.prefix(1)) : lengths
    }

    /// The server's best rounds of one length, so a 9-hole total never sits in a list with an 18-hole one. The
    /// server ranks all lengths together by score to par per 18 holes; within one length that is simply the
    /// round's score to par, so its order is kept.
    static func bestRounds(_ rounds: [Stats.Round], holes: Int) -> [Stats.Round] {
        rounds.filter { $0.holesCount == holes }
    }

    /// Handicap first (players without one go last), then average to par, then name.
    static func ranked(_ players: [Stats.Player]) -> [Stats.Player] {
        players.sorted {
            let key0 = ($0.handicap ?? .infinity, $0.averageToPar ?? .infinity)
            let key1 = ($1.handicap ?? .infinity, $1.averageToPar ?? .infinity)
            return key0 != key1 ? key0 < key1 : $0.name.localizedCaseInsensitiveCompare($1.name) == .orderedAscending
        }
    }
}

/// One row per player, one fixed-width column per stat; the name takes what's left, truncated, so a long name
/// can't push the stats off screen.
private struct PlayerTable: View {
    struct Column {
        var title: String
        var width: CGFloat
        var value: (Stats.Player) -> String
    }

    let title: String
    let players: [Stats.Player]
    let columns: [Column]

    /// Handicap, average to par per 18, wins, birdies, aces.
    static let form: [Column] = [
        Column(title: "HCP", width: 40) { $0.handicap.map { String(format: "%.1f", $0) } ?? "–" },
        Column(title: "Avg", width: 42) { $0.averageToPar.map(formatToPar) ?? "–" },
        Column(title: "Wins", width: 34) { "\($0.wins)" },
        Column(title: "Bird", width: 34) { $0.holes.map { "\($0.birdies)" } ?? "–" },
        Column(title: "Aces", width: 34) { $0.holes.map { "\($0.holesInOne)" } ?? "–" },
    ]

    /// Best and average totals of complete 9- and 18-hole rounds.
    static let rounds: [Column] = [
        Column(title: "Best 9", width: 44) { $0.best9.map(String.init) ?? "–" },
        Column(title: "Avg 9", width: 44) { $0.avg9.map { String(format: "%.1f", $0) } ?? "–" },
        Column(title: "Best 18", width: 50) { $0.best18.map(String.init) ?? "–" },
        Column(title: "Avg 18", width: 50) { $0.avg18.map { String(format: "%.1f", $0) } ?? "–" },
    ]

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            Text(title).caption()
            Grid(horizontalSpacing: 6, verticalSpacing: 8) {
                GridRow {
                    Text("").gridColumnAlignment(.leading)
                    ForEach(columns, id: \.title) { stat(Text($0.title).font(Theme.label).foregroundStyle(Theme.muted), width: $0.width) }
                }
                ForEach(Array(players.enumerated()), id: \.element.id) { index, player in
                    GridRow {
                        Text("\(index + 1). \(player.name)")
                            .bold()
                            .lineLimit(1)
                            .truncationMode(.tail)
                            .frame(maxWidth: .infinity, alignment: .leading)
                        ForEach(columns, id: \.title) { stat(Text($0.value(player)), width: $0.width) }
                    }
                }
            }
            .font(.system(size: 15, weight: .semibold, design: .rounded).monospacedDigit())
            .foregroundStyle(Theme.chalk)
        }
    }

    private func stat(_ text: Text, width: CGFloat) -> some View {
        text.lineLimit(1).minimumScaleFactor(0.7).frame(width: width)
    }
}

/// A titled list of (name, value, detail) rows.
struct LeaderSection: View {
    let title: String
    let rows: [(name: String, value: String, detail: String)]

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            Text(title).caption()
            ForEach(Array(rows.enumerated()), id: \.offset) { index, row in
                HStack {
                    Text("\(index + 1).").foregroundStyle(Theme.muted).frame(width: 28, alignment: .leading)
                    Text(row.name).bold().lineLimit(1).truncationMode(.tail)
                    Spacer(minLength: 8)
                    VStack(alignment: .trailing, spacing: 0) {
                        Text(row.value).bold()
                        Text(row.detail).font(.system(size: 12, weight: .medium, design: .rounded)).foregroundStyle(Theme.muted)
                    }
                    .fixedSize()
                }
                .font(.system(size: 16, weight: .semibold, design: .rounded).monospacedDigit())
                .foregroundStyle(Theme.chalk)
            }
            if rows.isEmpty { Text("—").foregroundStyle(Theme.muted) }
        }
        .card()
    }
}
