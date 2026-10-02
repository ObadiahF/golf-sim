import SwiftUI

/// Settings > Club power: per club, a power percentage on top of the swing scale (to tame a club that flies too
/// far) and a detection sensitivity (to notice smaller swings, e.g. short putts). Stored on this phone only; the
/// putter has no power (it keeps its putt scale) but does have a sensitivity.
struct ClubPowerView: View {
    @Bindable var settings: AppSettings

    var body: some View {
        Form {
            Section {
                ForEach(AppSettings.poweredClubs) { club in
                    row(club.name, value: powerBinding(for: club), range: AppSettings.clubPowerRange, step: AppSettings.clubPowerStep)
                }
            } header: {
                Text("Power")
            } footer: {
                Text("Each club's swing speed is multiplied by its percentage, on top of the swing scale (\(settings.scale, specifier: "%.1f")×). Lower a club that flies too far, e.g. the wedge to 80%. The putter uses the putt scale instead.")
            }

            Section {
                ForEach(Club.bag) { club in
                    row(club.name, value: sensitivityBinding(for: club), range: AppSettings.sensitivityRange, step: AppSettings.sensitivityStep)
                }
            } header: {
                Text("Sensitivity")
            } footer: {
                Text("How small a swing counts. Raise it if gentle swings (short putts, chips) aren't picked up; lower it if waggles set off shots. Applies from the next Start or Address.")
            }

            Section {
                Button("Reset all to 100%", systemImage: "arrow.counterclockwise") {
                    settings.resetClubPower()
                    settings.resetSensitivity()
                    Haptics.tick()
                }
                .disabled(AppSettings.poweredClubs.allSatisfy { settings.power(for: $0) == 1 }
                          && Club.bag.allSatisfy { settings.sensitivity(for: $0) == 1 })
            }
        }
        .navigationTitle("Club power")
        .navigationBarTitleDisplayMode(.inline)
    }

    /// A club's name and percentage over a slider.
    private func row(_ name: String, value: Binding<Double>, range: ClosedRange<Double>, step: Double) -> some View {
        VStack(alignment: .leading) {
            HStack {
                Text(name)
                Spacer()
                Text(Self.percent(value.wrappedValue))
                    .monospacedDigit()
                    .foregroundStyle(value.wrappedValue == 1 ? .secondary : .primary)
            }
            Slider(value: value, in: range, step: step)
        }
    }

    private func powerBinding(for club: Club) -> Binding<Double> {
        Binding(get: { settings.power(for: club) }, set: { settings.setPower($0, for: club) })
    }

    private func sensitivityBinding(for club: Club) -> Binding<Double> {
        Binding(get: { settings.sensitivity(for: club) }, set: { settings.setSensitivity($0, for: club) })
    }

    /// "85%".
    static func percent(_ power: Double) -> String { "\(Int((power * 100).rounded()))%" }
}
