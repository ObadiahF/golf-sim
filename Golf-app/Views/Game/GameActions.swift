import SwiftUI

/// Menu / Mulligan / Pick up, shared by the gameplay and putting views (Pick up asks first). Greyed out while
/// the server is down, since they only work through it.
struct GameActions: View {
    let game: GameLink
    @State private var confirmPickUp = false

    var body: some View {
        HStack(spacing: 8) {
            PillButton(title: "Menu", systemImage: "pause.fill") { game.nav(.back) }
            PillButton(title: "Mulligan", systemImage: "arrow.uturn.backward", action: game.mulligan)
            PillButton(title: "Pick up", systemImage: "flag.slash", tint: Theme.warn) { confirmPickUp = true }
        }
        .usable(game.isConnected)
        .confirmationDialog("Pick up on this hole?", isPresented: $confirmPickUp, titleVisibility: .visible) {
            Button("Pick up", role: .destructive, action: game.skip)
        } message: {
            Text("\(game.state?.player ?? "The player") scores the hole's maximum and play moves on.")
        }
    }
}

/// The sim's aim, turned from the phone; shared by the gameplay and putting views. Greyed out while the server
/// is down, and disabled the moment the sim leaves the hole, which stops a held aim button repeating while
/// this screen fades out.
struct GameAim: View {
    let game: GameLink

    var body: some View {
        AimControl(aim: game.state?.aim ?? 0, onTurn: game.aim(by:), onReset: game.aimReset)
            .usable(game.isConnected && game.state?.isGame == true)
    }
}
