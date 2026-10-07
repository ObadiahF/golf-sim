import SwiftUI

/// Putting mode (the sim says the player is putting): the distance and slope to the hole, the power meter
/// filling live as you stroke with the sim's read marked on it, aim, how the last putt finished, and a way
/// back to the club wheel to chip instead.
struct PuttingView: View {
    let session: SwingSession

    private var game: GameLink { session.game }
    private var state: GameProtocol.SimState? { game.state }
    private var meter: PuttMeter { session.meter }

    var body: some View {
        ScreenScaffold(title: state?.playTitle ?? "Practice", session: session) { fit in
            // The putting green is putts only: its made count stands where the Chip button would be. The banner
            // covers the card rather than pushing the meter and Address button down.
            PuttCard(state: state, onChip: state?.facility == .puttingGreen ? nil : { session.selectClub(Self.chipClub) })
                .overlay(alignment: .top) {
                    DisconnectedBanner(game: game).background(Theme.fairwayBottom, in: .rect(cornerRadius: 14))
                }
            InstructionText(session: session)
            HStack(alignment: .center, spacing: 18) {
                PowerMeter(value: meter.shown, peak: meter.peak, target: state?.puttPlaysAs, range: meter.range)
                    .frame(width: 110)
                AddressButton(stage: session.stage, size: fit.size(160, 124), action: session.address)
                    .frame(maxWidth: .infinity)
            }
            .frame(height: fit.size(240, 176))
            .waiting(game.shotWait)
            GameAim(game: game, compact: fit.tight)
            PuttResultStrip(result: meter.result, delivery: session.delivery)
        } bottom: {
            GameActions(game: game, holdsReplay: true)
            SimulateSwingButton(session: session)
        }
    }

    /// The wedge: picking it leaves putting mode (the sim sends a non-putting state).
    static var chipClub: Int { Club.index(named: "Pitching Wedge") ?? 0 }
}

/// Distance to the hole in metres and feet, the rise or fall, how long it plays and the green speed, and a
/// button to chip instead (on the putting green: putts made so far).
struct PuttCard: View {
    let state: GameProtocol.SimState?
    let onChip: (() -> Void)?

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack(alignment: .firstTextBaseline) {
                Text(Self.metres(state?.puttDistance))
                    .font(Theme.number(40))
                    .foregroundStyle(Theme.chalk)
                    .contentTransition(.numericText())
                Text(Self.feet(state?.puttDistance))
                    .font(Theme.number(20))
                    .foregroundStyle(Theme.muted)
                Spacer()
                if let onChip {
                    Button(action: onChip) {
                        Label("Chip", systemImage: "arrow.up.forward").capsuleTag()
                    }
                    .buttonStyle(PressDimStyle())
                    .accessibilityHint("Switches to the wedge and the club wheel")
                } else if let made = state?.madeSummary {
                    Label(made, systemImage: "flag.fill").capsuleTag()
                }
            }
            HStack {
                stat("Slope", Self.slope(state?.elevation))
                stat("Plays like", Self.metres(state?.puttPlaysAs))
                stat("Green", state?.stimp.map { String(format: "Stimp %.0f", $0) } ?? "–")
            }
        }
        .card(padding: 12)
    }

    private func stat(_ title: String, _ value: String) -> some View {
        VStack(alignment: .leading, spacing: 2) {
            Text(title).caption()
            Text(value)
                .font(.system(size: 17, weight: .bold, design: .rounded).monospacedDigit())
                .foregroundStyle(Theme.chalk)
                .lineLimit(1)
                .minimumScaleFactor(0.7)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }

    static func metres(_ m: Double?) -> String { m.map { String(format: "%.1f m", $0) } ?? "–" }

    static func feet(_ m: Double?) -> String { m.map { String(format: "%.0f ft", $0 / PuttModel.metersPerFoot) } ?? "" }

    /// "12 cm uphill", "8 cm downhill" or "Flat".
    static func slope(_ m: Double?) -> String {
        guard let m else { return "–" }
        let cm = Int((m * 100).rounded())
        return cm == 0 ? "Flat" : "\(abs(cm)) cm \(cm > 0 ? "uphill" : "downhill")"
    }
}

/// "Putted 4.2 m of 5.0 m" once the sim reports the last putt (or where the putt got to).
struct PuttResultStrip: View {
    let result: PuttMeter.Result?
    let delivery: Delivery?

    var body: some View {
        HStack {
            Text("Last putt").caption()
            Spacer()
            Text(text)
                .font(.system(size: 15, weight: .bold, design: .rounded).monospacedDigit())
                .foregroundStyle(result?.holed == true ? Theme.good : Theme.chalk)
                .lineLimit(1)
                .minimumScaleFactor(0.7)
        }
        .padding(.horizontal, 14)
        .frame(minHeight: 40)
        .background(Theme.card, in: .rect(cornerRadius: 14))
    }

    private var text: String {
        if let delivery, delivery != .received { return delivery.label }
        return result?.summary ?? "—"
    }
}

private extension View {
    /// The small dark capsule on the putt card (the Chip button, the putting green's made count).
    func capsuleTag() -> some View {
        font(.system(size: 13, weight: .bold, design: .rounded))
            .foregroundStyle(Theme.chalk)
            .padding(.horizontal, 10)
            .padding(.vertical, 6)
            .background(Color.black.opacity(0.25), in: .capsule)
    }
}
