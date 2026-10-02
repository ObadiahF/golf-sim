import SwiftUI

/// Gameplay mode (the sim is playing a hole): whose turn it is, the club wheel around the Address
/// button, aim, the last shot, and Menu / Mulligan / Pick up.
struct GameplayView: View {
    let session: SwingSession

    private var game: GameLink { session.game }
    private var state: GameProtocol.SimState? { game.state }

    var body: some View {
        ScreenScaffold(title: state?.player ?? "Practice", session: session) { fit in
            DisconnectedBanner(game: game)
            TurnCard(state: state, club: session.club)
            InstructionText(session: session)
            ClubWheel(selected: session.settings.clubIndex, size: fit.size(320, 220), onSelect: session.selectClub) {
                AddressButton(stage: session.stage, size: fit.size(168, 116), action: session.address)
            }
            .waiting(game.shotWait)
            GameAim(game: game)
            LastShotStrip(result: game.lastShotResult, delivery: session.delivery)
            GameActions(game: game)
            SimulateSwingButton(session: session)
        }
    }
}

/// Hole, par, strokes, distance and lie for the player up.
struct TurnCard: View {
    let state: GameProtocol.SimState?
    let club: Club

    var body: some View {
        HStack(alignment: .top) {
            stat("Hole", state?.hole.map { "\($0)" } ?? "–", detail: state?.par.map { "Par \($0)" })
            stat("Strokes", "\(state?.strokes ?? 0)", detail: club.name)
            stat("To pin", state?.distanceToPin.map { "\(Int($0.rounded())) yd" } ?? "–", detail: state?.lie?.capitalized)
        }
        .card(padding: 12)
    }

    private func stat(_ title: String, _ value: String, detail: String?) -> some View {
        VStack(alignment: .leading, spacing: 2) {
            Text(title).caption()
            Text(value)
                .font(Theme.number(26))
                .foregroundStyle(Theme.chalk)
                .lineLimit(1)
                .minimumScaleFactor(0.6)
                .contentTransition(.numericText())
            Text(detail ?? " ")
                .font(.system(size: 13, weight: .semibold, design: .rounded))
                .foregroundStyle(Theme.muted)
                .lineLimit(1)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }
}

/// One line about the last shot: the sim's result, or where the shot got to.
struct LastShotStrip: View {
    let result: GameProtocol.ShotResult?
    let delivery: Delivery?

    var body: some View {
        HStack {
            Text("Last shot").caption()
            Spacer()
            Text(text)
                .font(.system(size: 15, weight: .bold, design: .rounded).monospacedDigit())
                .foregroundStyle(Theme.chalk)
                .lineLimit(1)
                .minimumScaleFactor(0.7)
        }
        .padding(.horizontal, 14)
        .frame(minHeight: 40)
        .background(Theme.card, in: .rect(cornerRadius: 14))
    }

    private var text: String {
        if let delivery, delivery != .received { return delivery.label }
        guard let result else { return "—" }
        let carry = Int((result.carry ?? 0).rounded())
        let total = Int((result.total ?? 0).rounded())
        return "\(result.player): \(carry) carry · \(total) yd · \(result.lieText)"
    }
}
