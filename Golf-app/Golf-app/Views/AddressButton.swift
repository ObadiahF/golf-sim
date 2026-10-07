import SwiftUI

/// The big golf-ball button: tap to (re)capture the address pose. A ring pulses while armed.
struct AddressButton: View {
    let stage: SwingSession.Stage
    var size: CGFloat = 176
    let action: () -> Void

    @State private var pulse = false

    /// The label shrinks with a smaller ball (the compact club wheel on short phones).
    private var textScale: CGFloat { min(1, size / 168) }

    private var title: String { stage == .idle ? "ADDRESS" : "RE-ADDRESS" }
    private var ringColor: Color {
        switch stage {
        case .ready: Theme.good
        case .swinging, .settling: Theme.flag
        case .returning: Theme.warn
        default: .clear
        }
    }

    var body: some View {
        Button(action: action) {
            ZStack {
                Circle()
                    .stroke(ringColor, lineWidth: 4)
                    .scaleEffect(pulse ? 1.12 : 1)
                    .opacity(pulse ? 0.2 : 0.9)
                Circle()
                    .fill(RadialGradient(colors: [.white, Theme.chalk, Color(white: 0.78)], center: .init(x: 0.35, y: 0.3), startRadius: 4, endRadius: 110))
                    .overlay(Dimples().opacity(0.12))
                    .shadow(color: .black.opacity(0.35), radius: 12, y: 8)
                    .padding(10)
                VStack(spacing: 4) {
                    Image(systemName: "figure.golf").font(.system(size: 30 * textScale, weight: .semibold))
                    Text(title)
                        .font(.system(size: 17 * textScale, weight: .heavy, design: .rounded))
                        .tracking(1.5 * textScale)
                        .lineLimit(1)
                }
                .foregroundStyle(Theme.fairwayBottom)
            }
            .frame(width: size, height: size)
        }
        .buttonStyle(.plain)
        .onAppear {
            withAnimation(.easeInOut(duration: 1.1).repeatForever(autoreverses: true)) { pulse = true }
        }
    }
}

/// What to do next, e.g. "Ready. Swing away". Always two lines tall, so a longer or shorter line (a wait reason
/// between shots) doesn't resize the screen around it.
struct InstructionText: View {
    let session: SwingSession

    var body: some View {
        Text(session.instruction)
            .font(.system(size: 17, weight: .semibold, design: .rounded))
            .foregroundStyle(Theme.chalk)
            .multilineTextAlignment(.center)
            .lineLimit(2, reservesSpace: true)
            .minimumScaleFactor(0.8)
            .animation(.default, value: session.instruction)
    }
}

/// Debug builds in the simulator: plays a synthetic swing through the real pipeline.
struct SimulateSwingButton: View {
    let session: SwingSession

    var body: some View {
        #if DEBUG
        if session.canSimulate {
            Button("Simulate swing", systemImage: "wand.and.stars", action: session.simulateSwing)
                .font(.system(size: 15, weight: .semibold, design: .rounded))
                .tint(Theme.flag)
        }
        #endif
    }
}

/// A sparse dimple pattern so the button reads as a golf ball.
private struct Dimples: View {
    var body: some View {
        Canvas { context, size in
            let step: CGFloat = 16
            var row = 0
            for y in stride(from: step / 2, to: size.height, by: step * 0.87) {
                let offset = row.isMultiple(of: 2) ? 0 : step / 2
                for x in stride(from: step / 2 + offset, to: size.width, by: step) {
                    context.fill(Path(ellipseIn: CGRect(x: x - 3, y: y - 3, width: 6, height: 6)), with: .color(.black))
                }
                row += 1
            }
        }
        .clipShape(Circle())
    }
}
