import SwiftUI

/// Settings > Club power: a percentage per club on top of the swing scale, to tame a club that flies too far
/// (or not far enough) with your swing. Stored on this phone only; the putter keeps its putt scale.
struct ClubPowerView: View {
    @Bindable var settings: AppSettings

    var body: some View {
        Form {
            Section {
                ForEach(AppSettings.poweredClubs) { club in
                    VStack(alignment: .leading) {
                        HStack {
                            Text(club.name)
                            Spacer()
                            Text(Self.percent(settings.power(for: club)))
                                .monospacedDigit()
                                .foregroundStyle(settings.power(for: club) == 1 ? .secondary : .primary)
                        }
                        Slider(value: binding(for: club), in: AppSettings.clubPowerRange, step: AppSettings.clubPowerStep)
                    }
                }
            } footer: {
                Text("Each club's swing speed is multiplied by its percentage, on top of the swing scale (\(settings.scale, specifier: "%.1f")×). Lower a club that flies too far, e.g. the wedge to 80%. The putter uses the putt scale instead.")
            }

            Section {
                Button("Reset all to 100%", systemImage: "arrow.counterclockwise") {
                    settings.resetClubPower()
                    Haptics.tick()
                }
                .disabled(AppSettings.poweredClubs.allSatisfy { settings.power(for: $0) == 1 })
            }
        }
        .navigationTitle("Club power")
        .navigationBarTitleDisplayMode(.inline)
    }

    private func binding(for club: Club) -> Binding<Double> {
        Binding(get: { settings.power(for: club) }, set: { settings.setPower($0, for: club) })
    }

    /// "85%".
    static func percent(_ power: Double) -> String { "\(Int((power * 100).rounded()))%" }
}
