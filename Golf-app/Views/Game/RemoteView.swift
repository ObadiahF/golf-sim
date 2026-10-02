import SwiftUI

/// TV-remote mode: drives the sim's menus with the D-pad and Back, and shows the scorecard
/// when the sim does.
struct RemoteView: View {
    let session: SwingSession

    private var game: GameLink { session.game }

    var body: some View {
        ScreenScaffold(title: "Remote", session: session) { fit in
            statusCard
            if game.showsScorecard, let card = game.game {
                ScorecardTable(game: card).card(padding: 12)
                // OK without scrolling down to the D-pad on a short phone.
                PillButton(title: game.screen == "holeComplete" ? "Next hole" : "OK", systemImage: "checkmark", tint: Theme.flag) {
                    Haptics.press()
                    game.nav(.select)
                }
                .usable(game.simReady)
            }
            Spacer(minLength: 0)
            // Smaller under the scorecard, so OK stays in reach.
            DPad(size: game.showsScorecard ? 220 : fit.size(300, 240), onPress: game.nav)
                .usable(game.simReady)
            PillButton(title: "Back", systemImage: "arrow.uturn.backward") {
                Haptics.press()
                game.nav(.back)
            }
            .disabled(!game.simReady)
            Spacer(minLength: 0)
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
        case "loading": "Loading…"
        case "paused": "Paused"
        case "holeComplete": "Hole complete"
        case "results": "Final scores"
        case "game": "Playing"
        default: game.screen.capitalized
        }
    }

    static func hint(_ game: GameLink) -> String {
        guard game.isConnected else { return "Start the game server and check its address in Settings." }
        guard game.simConnected else { return "Start the golf sim on the PC." }
        return switch game.screen {
        case "holeComplete": "Press OK for the next hole."
        case "results": "Press OK to go back to the menu."
        case "paused": "Up/down to choose, OK to select, Back to resume."
        default: "Left/right to choose, OK to play. Add players and start a round in Players."
        }
    }
}
