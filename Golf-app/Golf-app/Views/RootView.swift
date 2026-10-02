import SwiftUI

/// Tabs: Play (TV remote, or gameplay while the sim plays a hole), Players, Scores and Practice.
struct RootView: View {
    enum Tab: Hashable { case play, players, scores, practice }

    let session: SwingSession
    @State private var tab = Tab.play

    var body: some View {
        TabView(selection: $tab) {
            PlayView(session: session)
                .tabItem { Label("Play", systemImage: "appletvremote.gen4.fill") }
                .tag(Tab.play)
            PlayersView(session: session)
                .tabItem { Label("Players", systemImage: "person.3.fill") }
                .tag(Tab.players)
            ScoresView(session: session)
                .tabItem { Label("Scores", systemImage: "list.number") }
                .tag(Tab.scores)
            ContentView(session: session)
                .tabItem { Label("Practice", systemImage: "figure.golf") }
                .tag(Tab.practice)
        }
        .tint(Theme.flag)
        // Jump to Play when the sim starts a hole or shows the scorecard.
        .onChange(of: session.game.screen) {
            if session.game.state?.isGame == true || session.game.showsScorecard { tab = .play }
        }
        // Follow the server address (by default the PC found by discovery).
        .onChange(of: session.settings.serverURL) { session.game.reconnect() }
    }
}

/// The remote, gameplay controls while the sim's screen is "game", or the putting view while putting.
struct PlayView: View {
    let session: SwingSession

    private var screen: PlayScreen { PlayScreen.of(session.game.state) }

    var body: some View {
        ZStack {
            Theme.background
            switch screen {
            case .remote: RemoteView(session: session).transition(.opacity)
            case .gameplay: GameplayView(session: session).transition(.opacity)
            case .putting: PuttingView(session: session).transition(.opacity)
            }
        }
        .animation(.easeInOut(duration: 0.25), value: screen)
    }
}
