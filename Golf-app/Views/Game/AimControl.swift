import SwiftUI

/// Left/right buttons (hold to keep turning) that rotate the sim's aim, and a reset.
struct AimControl: View {
    static let step = 1.0

    /// Current aim from the sim, degrees, + right.
    let aim: Double
    let onTurn: (Double) -> Void
    let onReset: () -> Void

    var body: some View {
        HStack(spacing: 12) {
            nudge(-Self.step, icon: "arrow.counterclockwise", label: "Aim left")
            Button(action: onReset) {
                VStack(spacing: 2) {
                    Text("Aim").caption()
                    Text(Self.label(aim))
                        .font(.system(size: 17, weight: .bold, design: .rounded).monospacedDigit())
                        .foregroundStyle(Theme.chalk)
                        .contentTransition(.numericText())
                    Text("tap to reset").font(.system(size: 11, weight: .medium, design: .rounded)).foregroundStyle(Theme.muted)
                }
                .frame(maxWidth: .infinity, minHeight: 56)
            }
            .buttonStyle(PressDimStyle())
            .accessibilityLabel("Reset aim, now \(Self.label(aim))")
            nudge(Self.step, icon: "arrow.clockwise", label: "Aim right")
        }
        .card(padding: 8)
    }

    private func nudge(_ delta: Double, icon: String, label: String) -> some View {
        HoldRepeatButton(action: {
            Haptics.tick()
            onTurn(delta)
        }) {
            Image(systemName: icon)
                .font(.system(size: 24, weight: .heavy))
                .foregroundStyle(Theme.fairwayBottom)
                .frame(width: 72, height: 56)
                .background(Theme.flag, in: .rect(cornerRadius: 14))
        }
        .accessibilityLabel(label)
    }

    /// "At the pin", "3° right", "1.5° left".
    static func label(_ aim: Double) -> String {
        guard abs(aim) >= 0.05 else { return "At the pin" }
        let value = abs(aim).truncatingRemainder(dividingBy: 1) < 0.05 ? String(format: "%.0f", abs(aim)) : String(format: "%.1f", abs(aim))
        return "\(value)° \(aim > 0 ? "right" : "left")"
    }
}
