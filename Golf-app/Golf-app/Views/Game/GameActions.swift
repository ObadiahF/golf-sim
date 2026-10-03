import SwiftUI

/// Replay (while the TV offers one) and Menu / Mulligan / Pick up, shared by the gameplay and putting views
/// (Pick up asks first). Greyed out while the server is down, since they only work through it.
struct GameActions: View {
    let game: GameLink
    @State private var confirmPickUp = false

    var body: some View {
        VStack(spacing: 8) {
            if game.offersReplay {
                ReplayButton(game: game).transition(.opacity)
            }
            HStack(spacing: 8) {
                PillButton(title: "Menu", systemImage: "pause.fill") { game.nav(.back) }
                PillButton(title: "Mulligan", systemImage: "arrow.uturn.backward", action: game.mulligan)
                PillButton(title: "Pick up", systemImage: "flag.slash", tint: Theme.warn) { confirmPickUp = true }
            }
        }
        .animation(.default, value: game.offersReplay)
        .usable(game.isConnected)
        .confirmationDialog("Pick up on this hole?", isPresented: $confirmPickUp, titleVisibility: .visible) {
            Button("Pick up", role: .destructive, action: game.skip)
        } message: {
            Text("\(game.state?.player ?? "The player") scores the hole's maximum and play moves on.")
        }
    }
}

/// "Replay last shot": sends `nav up`, which replays the last shot while the TV offers "▲ Replay" (between
/// turns in gameplay, and on the scorecards).
struct ReplayButton: View {
    let game: GameLink

    var body: some View {
        PillButton(title: "Replay last shot", systemImage: "play.rectangle.fill", tint: Theme.flag) {
            Haptics.press()
            game.replay()
        }
    }
}

/// The sim's aim, turned from the phone, and the Map button beside it (aim from the TV's course map); shared by the
/// gameplay and putting views. Greyed out while the server is down, and disabled the moment the sim leaves the hole,
/// which stops a held aim button repeating while this screen fades out.
struct GameAim: View {
    let game: GameLink
    /// The screen is short of room (`ScreenFit.tight`): compact aim buttons.
    var compact = false

    var body: some View {
        HStack(spacing: 8) {
            AimControl(aim: game.state?.aim ?? 0, onTurn: game.aim(by:), onReset: game.aimReset, compact: compact)
            MapButton(game: game, height: compact ? 60 : 72)
        }
        .usable(game.isConnected && game.state?.isGame == true)
    }
}
