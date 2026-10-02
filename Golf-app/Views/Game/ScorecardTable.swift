import SwiftUI

/// The scorecard, laid out like a paper card: a row per player, a column per hole. Names stay pinned on the
/// left and TOT and ± on the right while the holes scroll between them (scrolled to the hole being played).
/// An 18-hole card shows one nine at a time, front (OUT) or back (IN).
struct ScorecardTable: View {
    let game: GameView
    /// The nine picked; nil follows the hole being played.
    @State private var picked: Int?

    private static let cell: CGFloat = 28
    private static let headerHeight: CGFloat = 16
    private static let rowSpacing: CGFloat = 6
    private static let nameWidth: CGFloat = 72
    private static let sumWidth: CGFloat = 36

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            HStack {
                Text(title).caption()
                Spacer()
                if !game.winners.isEmpty {
                    Label(game.winners.joined(separator: " & "), systemImage: "trophy.fill")
                        .font(.system(size: 14, weight: .bold, design: .rounded))
                        .foregroundStyle(Theme.flag)
                        .lineLimit(1)
                }
            }
            if nines.count > 1 {
                Picker("Nine", selection: Binding { shownNine } set: { picked = $0 }) {
                    ForEach(nines.indices, id: \.self) { Text(Self.nineTitle($0, of: nines.count)).tag($0) }
                }
                .pickerStyle(.segmented)
            }
            HStack(alignment: .top, spacing: 6) {
                column("Hole", par: "Par", width: Self.nameWidth, alignment: .leading) {
                    Text($0.name).bold().lineLimit(1).truncationMode(.tail)
                }
                holesScroller
                column("TOT", par: text(Self.sum((0..<game.holesCount).map(par))), width: Self.sumWidth) {
                    Text("\($0.total)").bold()
                }
                column("±", par: "", width: Self.sumWidth) { Text(formatToPar($0.toPar)) }
            }
            .font(.system(size: 15, weight: .semibold, design: .rounded).monospacedDigit())
            .foregroundStyle(Theme.chalk)
        }
    }

    /// The shown nine's holes and its OUT/IN, scrolling sideways with a visible indicator.
    private var holesScroller: some View {
        let holes = nines[safe: shownNine] ?? []
        let subtotal = Self.subtotalLabel(shownNine, of: nines.count)
        return ScrollViewReader { reader in
            ScrollView(.horizontal) {
                HStack(spacing: 2) {
                    ForEach(holes, id: \.self) { hole in
                        column("\(hole + 1)", par: par(hole).map(String.init) ?? "–") { score(strokes($0, hole), par: par(hole)) }
                            .id(hole)
                    }
                    if let subtotal {
                        column(subtotal, par: text(Self.sum(holes.map(par))), width: Self.sumWidth) { player in
                            Text(text(Self.sum(holes.map { strokes(player, $0) }))).bold()
                        }
                    }
                }
                .padding(.bottom, 8) // room for the scroll indicator
            }
            .scrollIndicators(.visible)
            .scrollIndicatorsFlash(onAppear: true)
            .onAppear { reader.scrollTo(Self.focusHole(game), anchor: .center) }
            .onChange(of: Self.focusHole(game)) { _, hole in withAnimation { reader.scrollTo(hole, anchor: .center) } }
        }
    }

    /// A column: its title, the par row, then a cell per player, all on the shared row heights.
    private func column<Cell: View>(_ title: String, par: String, width: CGFloat = cell, alignment: Alignment = .center,
                                    @ViewBuilder cell: @escaping (GameView.PlayerCard) -> Cell) -> some View {
        VStack(spacing: Self.rowSpacing) {
            header(title, width: width, alignment: alignment)
            header(par, width: width, alignment: alignment)
            ForEach(game.players) { cell($0).frame(width: width, height: Self.cell, alignment: alignment) }
        }
    }

    /// Hole indices (0-based) in nines; a short last nine keeps its holes.
    private var nines: [[Int]] { Self.nines(holes: game.holesCount) }

    private var shownNine: Int { picked ?? Self.focusHole(game) / 9 }

    static func nines(holes: Int) -> [[Int]] {
        stride(from: 0, to: holes, by: 9).map { Array($0..<min($0 + 9, holes)) }
    }

    /// OUT and IN on an 18-hole card; no subtotal when there's only one nine.
    static func subtotalLabel(_ index: Int, of count: Int) -> String? {
        guard count > 1 else { return nil }
        return index == 0 ? "OUT" : "IN"
    }

    static func nineTitle(_ index: Int, of count: Int) -> String {
        count == 2 ? (index == 0 ? "Front 9" : "Back 9") : "Holes \(index * 9 + 1)–\(index * 9 + 9)"
    }

    /// The hole being played (0-based): the first one the furthest player hasn't finished, else the last.
    static func focusHole(_ game: GameView) -> Int {
        let played = game.players.map(\.holesPlayed).max() ?? 0
        return max(0, min(played, game.holesCount - 1))
    }

    /// Sum of the known values, nil when none is known yet.
    static func sum(_ values: [Int?]) -> Int? {
        let known = values.compactMap { $0 }
        return known.isEmpty ? nil : known.reduce(0, +)
    }

    private func par(_ hole: Int) -> Int? { game.pars[safe: hole] ?? nil }
    private func strokes(_ player: GameView.PlayerCard, _ hole: Int) -> Int? { player.strokes[safe: hole] ?? nil }
    private func text(_ value: Int?) -> String { value.map(String.init) ?? "" }

    private var title: String {
        switch game.status {
        case "FINISHED": "Final · Game #\(game.id)"
        case "ABANDONED": "Ended early · Game #\(game.id)"
        default: "Game #\(game.id) · \(game.holesCount) holes"
        }
    }

    private func header(_ text: String, width: CGFloat, alignment: Alignment) -> some View {
        Text(text)
            .font(Theme.label)
            .foregroundStyle(Theme.muted)
            .lineLimit(1)
            .frame(width: width, height: Self.headerHeight, alignment: alignment)
    }

    /// Strokes, circled under par and boxed over par, like a paper card.
    private func score(_ strokes: Int?, par: Int?) -> some View {
        let diff = (strokes ?? 0) - (par ?? strokes ?? 0)
        return Text(strokes.map(String.init) ?? "·")
            .frame(width: Self.cell, height: Self.cell)
            .background {
                if strokes != nil, diff < 0 { Circle().stroke(Theme.good, lineWidth: 1.5) }
                if strokes != nil, diff > 0 { RoundedRectangle(cornerRadius: 5).stroke(Theme.warn.opacity(0.8), lineWidth: 1.5) }
            }
    }
}

extension Array {
    subscript(safe index: Int) -> Element? { indices.contains(index) ? self[index] : nil }
}
