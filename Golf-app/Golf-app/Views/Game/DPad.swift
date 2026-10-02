import SwiftUI

/// A TV-remote D-pad: four arrow wedges around a big round select button.
struct DPad: View {
    var size: CGFloat = 300
    let onPress: (GameProtocol.NavKey) -> Void

    private static let inner: CGFloat = 0.44
    private static let wedges: [(key: GameProtocol.NavKey, icon: String, center: Double)] = [
        (.up, "chevron.up", -90), (.right, "chevron.right", 0), (.down, "chevron.down", 90), (.left, "chevron.left", 180),
    ]

    var body: some View {
        ZStack {
            ForEach(Self.wedges, id: \.key) { wedge in
                let start = Angle.degrees(wedge.center - 44)
                let end = Angle.degrees(wedge.center + 44)
                let shape = RingSegment(start: start, end: end, inner: Self.inner)
                Button { press(wedge.key) } label: {
                    ZStack {
                        shape.fill(Theme.card)
                        shape.stroke(Theme.cardStroke, lineWidth: 1)
                        Image(systemName: wedge.icon)
                            .font(.system(size: 30, weight: .heavy))
                            .foregroundStyle(Theme.chalk)
                            .offset(RingSegment.labelOffset(start: start, end: end, radius: size * (1 + Self.inner) / 4))
                    }
                    .contentShape(shape)
                }
                .buttonStyle(PressDimStyle())
                .accessibilityLabel(wedge.key.rawValue.capitalized)
            }
            Button { press(.select) } label: {
                Circle()
                    .fill(Theme.flag)
                    .overlay(Text("OK").font(.system(size: 30, weight: .heavy, design: .rounded)).foregroundStyle(Theme.fairwayBottom))
                    .shadow(color: .black.opacity(0.35), radius: 10, y: 6)
                    .frame(width: size * Self.inner - 16, height: size * Self.inner - 16)
            }
            .buttonStyle(PressDimStyle())
            .accessibilityLabel("Select")
        }
        .frame(width: size, height: size)
    }

    private func press(_ key: GameProtocol.NavKey) {
        Haptics.press()
        onPress(key)
    }
}
