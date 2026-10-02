import SwiftUI

/// Last shot as three fixed numbers: ball speed, carry and total. The layout never changes size; empty
/// values show "—" until there is a shot.
struct ShotCard: View {
    let shot: Shot?
    let result: SimResult?

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            Text("Last shot").caption()
            HStack {
                stat("Ball", shot.map { "\(Int($0.ballSpeedMph.rounded()))" }, unit: "mph")
                stat("Carry", result.map { "\(Int($0.carry.rounded()))" }, unit: "yd")
                stat("Total", result.map { "\(Int($0.total.rounded()))" }, unit: "yd")
            }
        }
        .card()
        .transaction { $0.animation = nil }
    }

    private func stat(_ title: String, _ value: String?, unit: String) -> some View {
        VStack(alignment: .leading, spacing: 2) {
            Text(title).caption()
            HStack(alignment: .firstTextBaseline, spacing: 4) {
                Text(value ?? "—").font(Theme.number(34)).foregroundStyle(Theme.chalk)
                Text(unit).font(.system(size: 14, weight: .semibold, design: .rounded)).foregroundStyle(Theme.muted)
            }
            .lineLimit(1)
            .minimumScaleFactor(0.6)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }
}
