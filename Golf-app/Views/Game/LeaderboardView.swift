import SwiftUI

/// All-time stats: every player ranked by handicap (then average), and the server's top-10 lists.
struct LeaderboardView: View {
    let board: Stats.Leaderboard
    let players: [Stats.Player]

    var body: some View {
        VStack(spacing: 14) {
            if !players.isEmpty { PlayerTable(players: Self.ranked(players)).card(padding: 14) }
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
            LeaderSection(title: "Best rounds", rows: board.bestRounds.map {
                ($0.player, "\($0.total) (\(formatToPar($0.toPar)))", "\($0.holesCount) holes · game #\($0.gameId)")
            })
            LeaderSection(title: "Most wins", rows: board.mostWins.map {
                ($0.player, "\($0.wins)", "of \($0.finishedRounds) rounds")
            })
        }
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

/// One row per player: handicap, average, best, wins, birdies, aces. The stat columns have fixed widths and
/// the name takes what's left, truncated, so a long name can't push the stats off screen.
private struct PlayerTable: View {
    let players: [Stats.Player]

    private static let columns: [(title: String, width: CGFloat)] = [
        ("HCP", 40), ("Avg", 42), ("Best", 32), ("Wins", 34), ("Bird", 30), ("Aces", 34),
    ]

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            Text("Players").caption()
            Grid(horizontalSpacing: 6, verticalSpacing: 8) {
                GridRow {
                    Text("").gridColumnAlignment(.leading)
                    ForEach(Self.columns, id: \.title) { stat(Text($0.title).font(Theme.label).foregroundStyle(Theme.muted), width: $0.width) }
                }
                ForEach(Array(players.enumerated()), id: \.element.id) { index, player in
                    GridRow {
                        Text("\(index + 1). \(player.name)")
                            .bold()
                            .lineLimit(1)
                            .truncationMode(.tail)
                            .frame(maxWidth: .infinity, alignment: .leading)
                        ForEach(Array(Self.values(player).enumerated()), id: \.offset) { column, value in
                            stat(Text(value), width: Self.columns[column].width)
                        }
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

    /// The row's values, in `columns` order.
    private static func values(_ player: Stats.Player) -> [String] {
        [
            player.handicap.map { String(format: "%.1f", $0) } ?? "–",
            player.averageToPar.map(formatToPar) ?? "–",
            player.bestTotal.map(String.init) ?? "–",
            "\(player.wins)",
            player.holes.map { "\($0.birdies)" } ?? "–",
            player.holes.map { "\($0.holesInOne)" } ?? "–",
        ]
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
