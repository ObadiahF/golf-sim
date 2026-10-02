import SwiftUI

/// Screen title, the connection pill and the Settings gear (which opens Settings).
struct AppHeader: View {
    let title: String
    let session: SwingSession
    @State private var showSettings = false

    var body: some View {
        HStack {
            Text(title)
                .font(.system(size: 26, weight: .heavy, design: .rounded))
                .foregroundStyle(Theme.chalk)
                .lineLimit(1)
                .minimumScaleFactor(0.7)
            Spacer()
            Button { showSettings = true } label: {
                HStack(spacing: 8) {
                    StatusPill(text: session.statusText, color: session.statusColor)
                    Image(systemName: "gearshape.fill").foregroundStyle(Theme.chalk)
                }
            }
            .buttonStyle(.plain)
            .accessibilityLabel("Settings")
        }
        .padding(.top, 8)
        .sheet(isPresented: $showSettings) {
            SettingsView(settings: session.settings, link: session.link, game: session.game)
        }
    }
}

extension SwingSession {
    /// One status for the header: the game server and sim when the server is up, else the UDP link.
    var statusText: String {
        if game.simReady { return "Sim connected" }
        if game.isConnected { return "Sim not running" }
        return link.connection.label
    }

    var statusColor: Color {
        if game.simReady { return Theme.good }
        if game.isConnected { return Theme.warn }
        return link.connection.color
    }
}
