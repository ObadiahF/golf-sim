import SwiftUI

/// Practice, kept still on purpose: pick a club, tap Start, wait for the buzz, swing. After each swing, return
/// to address and hold still; the next buzz means swing again. Nothing moves, pulses or resizes.
struct ContentView: View {
    let session: SwingSession

    var body: some View {
        ZStack {
            Theme.background
            VStack(spacing: 14) {
                AppHeader(title: "Practice", session: session)
                ClubPicker(selected: session.settings.clubIndex, onSelect: session.selectClub)
                ShotCard(shot: session.lastShot, result: session.result)
                Spacer(minLength: 0)
                PracticeButton(session: session)
                SimulateSwingButton(session: session)
            }
            .padding(.horizontal, 16)
            .padding(.bottom, 12)
        }
        .onDisappear(perform: session.stop) // no buzzing from a tab you can't see
    }
}

/// One big button: Start, then a single word for what to do (Hold still, Swing); tap again to stop.
struct PracticeButton: View {
    let session: SwingSession

    var body: some View {
        Button(action: toggle) {
            VStack(spacing: 6) {
                Text(word)
                    .font(.system(size: 40, weight: .heavy, design: .rounded))
                Text(hint)
                    .font(.system(size: 15, weight: .semibold, design: .rounded))
                    .opacity(0.75)
            }
            .lineLimit(1)
            .minimumScaleFactor(0.6)
            .foregroundStyle(isLit ? Theme.fairwayBottom : Theme.chalk)
            .frame(maxWidth: .infinity, minHeight: 190)
            .background(color, in: .rect(cornerRadius: 28))
        }
        .buttonStyle(.plain)
        .transaction { $0.animation = nil }
    }

    private func toggle() {
        session.isActive ? session.stop() : session.address()
    }

    private var word: String {
        switch session.stage {
        case .idle: "Start"
        case .settling, .returning: "Hold still"
        case .ready, .swinging: "Swing"
        case .unavailable: "Unavailable"
        }
    }

    private var hint: String {
        switch session.stage {
        case .idle: "Tap, grip the phone, wait for the buzz"
        case .settling, .returning: "Buzz means swing · tap to stop"
        case .ready, .swinging: "Tap to stop"
        case .unavailable(let reason): reason
        }
    }

    /// Yellow to start, green to swing, plain while you hold still.
    private var isLit: Bool { color != Theme.card }

    private var color: Color {
        switch session.stage {
        case .idle: Theme.flag
        case .ready, .swinging: Theme.good
        default: Theme.card
        }
    }
}

#Preview {
    ContentView(session: SwingSession())
}
