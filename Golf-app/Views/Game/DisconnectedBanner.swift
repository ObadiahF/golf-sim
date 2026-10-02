import SwiftUI

/// Shown on the game screens while the server link is down: the controls that need it are greyed out until
/// the link reconnects (by itself) and the server's `hello` brings the sim's state back.
struct DisconnectedBanner: View {
    let game: GameLink

    var body: some View {
        if let reason = game.outage {
            HStack(spacing: 12) {
                ProgressView().tint(Theme.warn)
                VStack(alignment: .leading, spacing: 2) {
                    Text("Disconnected — reconnecting…")
                        .font(.system(size: 15, weight: .bold, design: .rounded))
                        .foregroundStyle(Theme.chalk)
                    Text("\(reason). Aim, Menu, Mulligan and Pick up come back when the server does.")
                        .font(.system(size: 13, weight: .medium, design: .rounded))
                        .foregroundStyle(Theme.muted)
                }
                Spacer(minLength: 0)
            }
            .padding(12)
            .background(Theme.warn.opacity(0.18), in: .rect(cornerRadius: 14))
            .overlay(RoundedRectangle(cornerRadius: 14).stroke(Theme.warn.opacity(0.6), lineWidth: 1))
            .accessibilityElement(children: .combine)
            .transition(.opacity)
        }
    }
}
