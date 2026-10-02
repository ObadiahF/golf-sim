import SwiftUI

/// The scorecard, laid out like a paper card: a row per player, a column per hole. Names stay pinned on the
/// left and TOT and ± on the right, and the hole columns narrow so a whole nine fits between them (names get
/// any room left over). Only when a nine can't fit (an 18-hole card's OUT/IN, a narrow phone) do the holes
/// scroll, to the hole being played. An 18-hole card shows one nine at a time, front (OUT) or back (IN).
struct ScorecardTable: View {
    let game: GameView
    /// The nine picked; nil follows the hole being played.
    @State private var picked: Int?
    /// The table's width, for `Columns`; 0 until measured.
    @State private var width: CGFloat = 0

    private static let rowHeight: CGFloat = 28
    private static let headerHeight: CGFloat = 16
    private static let rowSpacing: CGFloat = 6
    private static let sumWidth: CGFloat = 32
    private static let columnSpacing: CGFloat = 4
    private static let holeSpacing: CGFloat = 2

    /// Hole and name column widths for a table `width` wide showing `holes` holes (and an OUT/IN column).
    struct Columns: Equatable {
        static let cellRange: ClosedRange<CGFloat> = 20...28
        static let minName: CGFloat = 60
        static let unmeasured = Columns(cell: cellRange.upperBound, name: 72)

        var cell: CGFloat
        var name: CGFloat

        /// The widest hole cell (up to 28) that fits every hole beside a 60-point name, never below 20; the
        /// name gets the rest. 0 (not measured yet) gives the old fixed sizes.
        static func fit(width: CGFloat, holes: Int, subtotal: Bool) -> Columns {
            guard width > 0, holes > 0 else { return .unmeasured }
            let room = width - ScorecardTable.fixedWidth - minName - holesWidth(cell: 0, holes: holes, subtotal: subtotal)
            let cell = min(cellRange.upperBound, max(cellRange.lowerBound, (room / CGFloat(holes)).rounded(.down)))
            let holesWidth = holesWidth(cell: cell, holes: holes, subtotal: subtotal)
            return Columns(cell: cell, name: max(minName, width - ScorecardTable.fixedWidth - holesWidth))
        }

        /// The holes and the OUT/IN column side by side.
        static func holesWidth(cell: CGFloat, holes: Int, subtotal: Bool) -> CGFloat {
            let columns = holes + (subtotal ? 1 : 0)
            return cell * CGFloat(holes) + (subtotal ? ScorecardTable.sumWidth : 0) + ScorecardTable.holeSpacing * CGFloat(columns - 1)
        }
    }

    /// TOT and ± and the gaps between the four parts of a row.
    static var fixedWidth: CGFloat { sumWidth * 2 + columnSpacing * 3 }

    private var columns: Columns {
        let holes = nines[safe: shownNine]?.count ?? 0
        return Columns.fit(width: width, holes: holes, subtotal: Self.subtotalLabel(shownNine, of: nines.count) != nil)
    }

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
            HStack(alignment: .top, spacing: Self.columnSpacing) {
                column("Hole", par: "Par", width: columns.name, alignment: .leading) {
                    Text($0.name).bold().lineLimit(1).minimumScaleFactor(0.8).truncationMode(.tail)
                }
                holesScroller
                column("TOT", par: text(Self.sum((0..<game.holesCount).map(par))), width: Self.sumWidth) {
                    Text("\($0.total)").bold()
                }
                column("±", par: "", width: Self.sumWidth) { Text(formatToPar($0.toPar)).lineLimit(1).minimumScaleFactor(0.8) }
            }
            .font(.system(size: 15, weight: .semibold, design: .rounded).monospacedDigit())
            .foregroundStyle(Theme.chalk)
        }
        .onGeometryChange(for: CGFloat.self) { $0.size.width } action: { width = $0 }
    }

    /// The shown nine's holes and its OUT/IN, scrolling sideways with a visible indicator.
    private var holesScroller: some View {
        let holes = nines[safe: shownNine] ?? []
        let subtotal = Self.subtotalLabel(shownNine, of: nines.count)
        let cell = columns.cell
        return ScrollViewReader { reader in
            ScrollView(.horizontal) {
                HStack(spacing: Self.holeSpacing) {
                    ForEach(holes, id: \.self) { hole in
                        column("\(hole + 1)", par: par(hole).map(String.init) ?? "–", width: cell) {
                            score(strokes($0, hole), par: par(hole), width: cell)
                        }
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
    private func column<Cell: View>(_ title: String, par: String, width: CGFloat, alignment: Alignment = .center,
                                    @ViewBuilder cell: @escaping (GameView.PlayerCard) -> Cell) -> some View {
        VStack(spacing: Self.rowSpacing) {
            header(title, width: width, alignment: alignment)
            header(par, width: width, alignment: alignment)
            ForEach(game.players) { cell($0).frame(width: width, height: Self.rowHeight, alignment: alignment) }
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
    private func score(_ strokes: Int?, par: Int?, width: CGFloat) -> some View {
        let diff = (strokes ?? 0) - (par ?? strokes ?? 0)
        return Text(strokes.map(String.init) ?? "·")
            .frame(width: width, height: Self.rowHeight)
            .background {
                if strokes != nil, diff < 0 { Circle().stroke(Theme.good, lineWidth: 1.5) }
                if strokes != nil, diff > 0 { RoundedRectangle(cornerRadius: 5).stroke(Theme.warn.opacity(0.8), lineWidth: 1.5) }
            }
    }
}

extension Array {
    subscript(safe index: Int) -> Element? { indices.contains(index) ? self[index] : nil }
}
