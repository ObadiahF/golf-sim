import SwiftUI

/// Last shot: ball speed big, then club speed, face and start line, then the sim's result.
struct ShotCard: View {
    let shot: Shot?
    let delivery: Delivery?
    let result: SimResult?

    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            HStack(alignment: .firstTextBaseline) {
                Text("Last shot").caption()
                Spacer()
                if let delivery { StatusPill(text: delivery.label, color: delivery.color) }
            }
            if let shot {
                HStack(alignment: .firstTextBaseline, spacing: 6) {
                    Text(shot.ballSpeedMph, format: .number.precision(.fractionLength(0)))
                        .font(Theme.number(64))
                        .foregroundStyle(Theme.chalk)
                        .contentTransition(.numericText())
                    Text("mph ball").font(.system(size: 16, weight: .semibold, design: .rounded)).foregroundStyle(Theme.muted)
                }
                HStack {
                    stat("Club", "\(Int(shot.clubSpeedMph.rounded())) mph")
                    stat("Face", Self.side(shot.face, open: "open", closed: "closed"))
                    stat("Start", Self.side(shot.azimuth, open: "R", closed: "L"))
                }
                Divider().overlay(Theme.cardStroke)
                HStack {
                    stat("Carry", result.map { "\(Int($0.carry.rounded())) yd" } ?? "—")
                    stat("Total", result.map { "\(Int($0.total.rounded())) yd" } ?? "—")
                    stat("Lie", result.map { $0.outcome == "Stopped" ? $0.surface.capitalized : $0.outcome } ?? "—")
                }
            } else {
                Text("Your shots show up here")
                    .font(.system(size: 17, weight: .medium, design: .rounded))
                    .foregroundStyle(Theme.muted)
                    .frame(maxWidth: .infinity, minHeight: 120)
            }
        }
        .card()
        .animation(.snappy, value: shot)
    }

    private func stat(_ title: String, _ value: String) -> some View {
        VStack(alignment: .leading, spacing: 4) {
            Text(title).caption()
            Text(value)
                .font(.system(size: 18, weight: .bold, design: .rounded).monospacedDigit())
                .foregroundStyle(Theme.chalk)
                .lineLimit(1)
                .minimumScaleFactor(0.7)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }

    /// "2.1° open" / "1.4° closed" / "Square".
    static func side(_ degrees: Double, open: String, closed: String) -> String {
        guard abs(degrees) >= 0.05 else { return "Square" }
        return String(format: "%.1f° %@", abs(degrees), degrees > 0 ? open : closed)
    }
}
