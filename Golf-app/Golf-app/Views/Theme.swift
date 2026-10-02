import SwiftUI

/// Colors and shared styling: a dusk-on-the-fairway palette with a flag-yellow accent.
enum Theme {
    static let fairwayTop = Color(red: 0.05, green: 0.24, blue: 0.16)
    static let fairwayBottom = Color(red: 0.02, green: 0.12, blue: 0.08)
    static let flag = Color(red: 0.98, green: 0.82, blue: 0.25)
    static let chalk = Color(red: 0.96, green: 0.95, blue: 0.90)
    static let muted = chalk.opacity(0.6)
    static let card = Color.white.opacity(0.07)
    static let cardStroke = Color.white.opacity(0.12)
    static let good = Color(red: 0.45, green: 0.85, blue: 0.5)
    static let warn = Color(red: 1.0, green: 0.62, blue: 0.3)

    static var background: some View {
        LinearGradient(colors: [fairwayTop, fairwayBottom], startPoint: .top, endPoint: .bottom)
            .ignoresSafeArea()
    }

    static func number(_ size: CGFloat) -> Font { .system(size: size, weight: .bold, design: .rounded).monospacedDigit() }
    static let label = Font.system(size: 12, weight: .semibold, design: .rounded)
}

extension View {
    /// The rounded translucent panel used for every card on the main screen.
    func card(padding: CGFloat = 16) -> some View {
        self
            .padding(padding)
            .frame(maxWidth: .infinity, alignment: .leading)
            .background(Theme.card, in: .rect(cornerRadius: 20))
            .overlay(RoundedRectangle(cornerRadius: 20).stroke(Theme.cardStroke, lineWidth: 1))
    }

    /// Small uppercase caption.
    func caption() -> some View {
        font(Theme.label).textCase(.uppercase).tracking(1).foregroundStyle(Theme.muted)
    }
}

/// A colored dot and a short label, e.g. connection or delivery status.
struct StatusPill: View {
    let text: String
    let color: Color

    var body: some View {
        HStack(spacing: 6) {
            Circle().fill(color).frame(width: 8, height: 8)
            Text(text).font(.system(size: 13, weight: .semibold, design: .rounded)).lineLimit(1)
        }
        .foregroundStyle(Theme.chalk)
        .padding(.horizontal, 10)
        .padding(.vertical, 6)
        .background(Color.black.opacity(0.25), in: .capsule)
    }
}

extension SimLink.Connection {
    var label: String {
        switch self {
        case .idle: "Offline"
        case .searching: "Looking for PC…"
        case .connected(let name): name
        case .notResponding: "PC not responding"
        case .notFound: "No PC found"
        case .failed(let reason): reason
        }
    }

    var color: Color {
        switch self {
        case .connected: Theme.good
        case .searching: Theme.flag
        default: Theme.warn
        }
    }
}

extension Delivery {
    var label: String {
        switch self {
        case .sending: "Sending…"
        case .received: "Received"
        case .busy: "Sim busy, ball still moving"
        case .rejected(let reason): reason
        case .noReply: "No reply from PC"
        }
    }

    var color: Color {
        switch self {
        case .sending: Theme.flag
        case .received: Theme.good
        default: Theme.warn
        }
    }
}

extension GameLink.Connection {
    var label: String {
        switch self {
        case .idle: "Offline"
        case .connecting: "Connecting…"
        case .connected: "Connected"
        case .waiting(let reason): reason
        }
    }

    var color: Color {
        switch self {
        case .connected: Theme.good
        case .connecting: Theme.flag
        default: Theme.warn
        }
    }
}
