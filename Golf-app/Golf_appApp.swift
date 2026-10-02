import SwiftUI
import UIKit

@main
struct Golf_appApp: App {
    @State private var session = SwingSession()
    @Environment(\.scenePhase) private var scenePhase

    var body: some Scene {
        WindowGroup {
            RootView(session: session)
                .preferredColorScheme(.dark)
                .onChange(of: scenePhase, initial: true) { _, phase in
                    let active = phase == .active
                    // Keep the screen awake while the remote is in use.
                    UIApplication.shared.isIdleTimerDisabled = active
                    if active {
                        session.connect()
                    } else if phase == .background {
                        session.disconnect()
                    }
                }
        }
    }
}
