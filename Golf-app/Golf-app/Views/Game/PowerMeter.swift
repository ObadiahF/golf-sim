import SwiftUI

/// The putting power meter: a vertical tube that fills (from the bottom) to how far the stroke would roll on a
/// flat green, with the target (the sim's read) marked across it, the stroke's peak as a tick, and metre ticks.
struct PowerMeter: View {
    /// Metres the meter fills to.
    let value: Double
    /// The stroke's strongest moment, metres.
    let peak: Double
    /// Metres the putt should roll (the sim's "plays like"); nil hides the mark.
    let target: Double?
    /// Metres at the top of the tube.
    let range: Double

    private let width: CGFloat = 46

    var body: some View {
        VStack(spacing: 6) {
            Text(String(format: "%.1f m", value))
                .font(Theme.number(18))
                .foregroundStyle(Theme.chalk)
                .contentTransition(.numericText())
            GeometryReader { geo in
                let h = geo.size.height
                ZStack(alignment: .bottom) {
                    Capsule().fill(Color.black.opacity(0.3))
                    Capsule().stroke(Theme.cardStroke, lineWidth: 1)
                    ticks(height: h)
                    Capsule()
                        .fill(LinearGradient(colors: [Theme.good, Theme.flag, fillTop], startPoint: .bottom, endPoint: .top))
                        .frame(height: max(width, h * fraction(value)))
                        .opacity(value > 0 ? 1 : 0)
                        .animation(.linear(duration: 0.05), value: value)
                    if peak > value + 0.05 { marker(at: peak, height: h, color: Theme.chalk.opacity(0.7), thickness: 2) }
                    if let target { targetMark(target, height: h) }
                }
                .frame(width: width)
                .frame(maxWidth: .infinity)
            }
            Text("Strength").caption()
        }
        .accessibilityElement(children: .ignore)
        .accessibilityLabel("Power meter")
        .accessibilityValue(String(format: "%.1f metres%@", value, target.map { String(format: ", target %.1f", $0) } ?? ""))
    }

    /// Past the target the top of the fill turns warm.
    private var fillTop: Color { target.map { value > $0 * 1.1 ? Theme.warn : Theme.flag } ?? Theme.flag }

    private func fraction(_ metres: Double) -> CGFloat { CGFloat(min(1, max(0, metres / range))) }

    private func marker(at metres: Double, height: CGFloat, color: Color, thickness: CGFloat) -> some View {
        Rectangle()
            .fill(color)
            .frame(width: width + 10, height: thickness)
            .offset(y: -height * fraction(metres) + thickness / 2)
    }

    private func targetMark(_ metres: Double, height: CGFloat) -> some View {
        marker(at: metres, height: height, color: .white, thickness: 3)
            .overlay(alignment: .trailing) {
                Text(String(format: "%.1f", metres))
                    .font(.system(size: 12, weight: .heavy, design: .rounded).monospacedDigit())
                    .foregroundStyle(Theme.fairwayBottom)
                    .padding(.horizontal, 5)
                    .padding(.vertical, 2)
                    .background(Theme.chalk, in: .capsule)
                    .offset(x: 40, y: -height * fraction(metres) + 1.5)
            }
    }

    /// A tick per metre (per 2 m on long meters).
    private func ticks(height: CGFloat) -> some View {
        let step = PuttMeter.tickStep(range)
        return ForEach(Array(stride(from: step, to: range, by: step)), id: \.self) { metres in
            Rectangle()
                .fill(Theme.chalk.opacity(0.18))
                .frame(width: width * 0.4, height: 1)
                .offset(y: -height * fraction(metres))
        }
    }
}

#Preview {
    ZStack {
        Theme.background
        PowerMeter(value: 4.2, peak: 4.6, target: 5.4, range: 9).frame(height: 280)
    }
}
