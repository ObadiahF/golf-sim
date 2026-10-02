import SwiftUI

/// A slice of a ring (annulus sector), for the D-pad wedges and the club wheel. Angles are
/// screen angles: 0° points right and they grow clockwise.
struct RingSegment: Shape {
    var start: Angle
    var end: Angle
    /// Inner radius as a fraction of the outer radius.
    var inner: CGFloat

    func path(in rect: CGRect) -> Path {
        let center = CGPoint(x: rect.midX, y: rect.midY)
        let outer = min(rect.width, rect.height) / 2
        var path = Path()
        path.addArc(center: center, radius: outer, startAngle: start, endAngle: end, clockwise: false)
        path.addArc(center: center, radius: outer * inner, startAngle: end, endAngle: start, clockwise: true)
        path.closeSubpath()
        return path
    }

    /// Offset from the centre to the middle of the slice, for its label.
    static func labelOffset(start: Angle, end: Angle, radius: CGFloat) -> CGSize {
        let mid = CGFloat((start.radians + end.radians) / 2)
        return CGSize(width: cos(mid) * radius, height: sin(mid) * radius)
    }
}

/// A round control that dims while pressed.
struct PressDimStyle: ButtonStyle {
    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .opacity(configuration.isPressed ? 0.6 : 1)
            .scaleEffect(configuration.isPressed ? 0.97 : 1)
            .animation(.snappy(duration: 0.12), value: configuration.isPressed)
    }
}

/// A small translucent pill button (Menu, Mulligan, Pick up, Back...).
struct PillButton: View {
    let title: String
    let systemImage: String
    var tint: Color = Theme.chalk
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            Label(title, systemImage: systemImage)
                .font(.system(size: 15, weight: .bold, design: .rounded))
                .foregroundStyle(tint)
                .frame(maxWidth: .infinity, minHeight: 44)
                .background(Theme.card, in: .capsule)
                .overlay(Capsule().stroke(Theme.cardStroke, lineWidth: 1))
        }
        .buttonStyle(PressDimStyle())
    }
}
