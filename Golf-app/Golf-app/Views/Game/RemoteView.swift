import SwiftUI

/// TV-remote mode: drives the sim's menus with the D-pad and Back, and shows the scorecard
/// when the sim does.
struct RemoteView: View {
    let session: SwingSession

    private var game: GameLink { session.game }

    var body: some View {
        ScreenScaffold(title: "Remote", session: session) { fit in
            statusCard
            if game.showsScorecard {
                if let card = game.game { ScorecardTable(game: card).card(padding: 12) }
                // OK (and Replay while the TV offers it) without scrolling down to the D-pad on a short phone.
                HStack(spacing: 8) {
                    if game.offersScorecardReplay { ReplayButton(game: game) }
                    PillButton(title: game.screen == "holeComplete" ? "Next hole" : "OK", systemImage: "checkmark", tint: Theme.flag) {
                        Haptics.press()
                        game.nav(.select)
                    }
                }
                .usable(game.simReady)
                .animation(.default, value: game.offersScorecardReplay)
            }
            Spacer(minLength: 0)
            // Smaller under the scorecard, so OK stays in reach.
            DPad(size: game.showsScorecard ? 200 : fit.size(300, 240), onPress: game.nav)
                .usable(game.simReady)
            Spacer(minLength: 0)
        } bottom: {
            PillButton(title: "Back", systemImage: "arrow.uturn.backward") {
                Haptics.press()
                game.nav(.back)
            }
            .disabled(!game.simReady)
        }
    }

    private var statusCard: some View {
        VStack(alignment: .leading, spacing: 6) {
            Text("On the TV").caption()
            Text(Self.screenTitle(game))
                .font(.system(size: 22, weight: .bold, design: .rounded))
                .foregroundStyle(Theme.chalk)
            Text(Self.hint(game))
                .font(.system(size: 15, weight: .medium, design: .rounded))
                .foregroundStyle(Theme.muted)
        }
        .card()
    }

    static func screenTitle(_ game: GameLink) -> String {
        guard game.isConnected else { return "Not connected" }
        guard game.simConnected else { return "Sim not running" }
        return switch game.screen {
        case "menu": "Main menu"
        case "courseSelect": "Choose a course"
        case "loading": "Loading…"
        case "game": "Playing"
        case "paused": "Paused"
        case "settings": "Sound"
        case "replay": "Instant replay"
        case "holeComplete", "scorecard": "Hole complete"
        case "results": "Final scores"
        default: game.screen.capitalized
        }
    }

    /// What the buttons do on the TV's screen (`state.screen`, see PROTOCOL.md), plus "▲ Replay last shot" on a
    /// scorecard that offers it. A screen the app doesn't know gets the D-pad's general use.
    static func hint(_ game: GameLink) -> String {
        guard game.isConnected else { return "Start the game server and check its address in Settings." }
        guard game.simConnected else { return GameLink.noSimHint }
        let hint = switch game.screen {
        case "menu": "Left/right to choose, OK to play. Add players and start a round in Players."
        case "courseSelect": "Arrows to choose, OK to pick, Back to go back."
        case "loading": "The next hole is loading."
        case "game": "The swing controls show here in a moment."
        case "paused": "Up/down to choose, OK to select, Back to resume."
        case "settings": "Up/Down choose · Left/Right change · Back close"
        case "replay": "Press OK to skip it."
        case "holeComplete", "scorecard": "Press OK for the next hole."
        case "results": "Press OK to go back to the menu."
        default: "Arrows to move, OK to select, Back to go back."
        }
        return game.offersScorecardReplay ? "\(hint)\n▲ Replay last shot" : hint
    }
}
