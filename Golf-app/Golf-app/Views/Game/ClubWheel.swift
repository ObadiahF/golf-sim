import SwiftUI

/// Weapon-wheel club picker: the bag around a ring, with any view (the Address button) in the
/// middle. Tap a club, or touch the ring and drag round to it; it's picked when you let go.
struct ClubWheel<Center: View>: View {
    let selected: Int
    var size: CGFloat = 320
    let onSelect: (Int) -> Void
    @ViewBuilder let center: () -> Center

    @State private var preview: Int?

    static var inner: CGFloat { 0.58 }
    private static var slice: Double { 360 / Double(Club.bag.count) }

    var body: some View {
        ZStack {
            ring
                .contentShape(RingSegment(start: .zero, end: .degrees(360), inner: Self.inner), eoFill: true)
                .gesture(
                    DragGesture(minimumDistance: 0)
                        .onChanged { drag in
                            let index = Self.index(at: drag.location, size: size)
                            if index != preview { Haptics.tick() }
                            preview = index
                        }
                        .onEnded { drag in
                            preview = nil
                            onSelect(Self.index(at: drag.location, size: size))
                        }
                )
            center()
        }
        .frame(width: size, height: size)
    }

    private var ring: some View {
        ZStack {
            ForEach(Club.bag.indices, id: \.self) { index in
                let (start, end) = Self.span(of: index)
                let shape = RingSegment(start: start, end: end, inner: Self.inner)
                let lit = (preview ?? selected) == index
                shape.fill(lit ? Theme.flag : Theme.card)
                shape.stroke(lit ? Theme.flag : Theme.cardStroke, lineWidth: 1)
                Text(Club.bag[index].short)
                    .font(.system(size: size < 260 ? 15 : 18, weight: .heavy, design: .rounded)) // 14 slices
                    .foregroundStyle(lit ? Theme.fairwayBottom : Theme.chalk)
                    .offset(RingSegment.labelOffset(start: start, end: end, radius: size * (1 + Self.inner) / 4))
                    .accessibilityLabel(Club.bag[index].name)
                    .accessibilityAddTraits(index == selected ? [.isButton, .isSelected] : .isButton)
            }
        }
        .frame(width: size, height: size)
        .scaleEffect(preview == nil ? 1 : 1.03)
        .animation(.snappy(duration: 0.15), value: preview)
    }

    /// Club 0 sits at the top; the rest follow clockwise, with a small gap between slices.
    private static func span(of index: Int) -> (Angle, Angle) {
        let center = -90 + Double(index) * slice
        return (.degrees(center - slice / 2 + 1.5), .degrees(center + slice / 2 - 1.5))
    }

    /// The club whose slice contains this point's direction from the centre.
    static func index(at point: CGPoint, size: CGFloat) -> Int {
        let degrees = atan2(point.y - size / 2, point.x - size / 2) * 180 / .pi + 90
        let count = Club.bag.count
        return (Int((degrees / slice).rounded()) % count + count) % count
    }
}
