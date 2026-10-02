import SwiftUI

/// Practice: connection, club, last shot, what to do next, and the big Address button.
struct ContentView: View {
    let session: SwingSession

    var body: some View {
        ZStack {
            Theme.background
            ScreenScaffold(title: "SwingRemote", session: session) { fit in
                ClubPicker(selected: session.settings.clubIndex, onSelect: session.selectClub)
                ShotCard(shot: session.lastShot, delivery: session.delivery, result: session.result)
                Spacer(minLength: 0)
                InstructionText(session: session)
                AddressButton(stage: session.stage, size: fit.size(176, 132), action: session.address)
                scaleSlider
                SimulateSwingButton(session: session)
            }
        }
    }

    /// The swing scale, or the putt scale with the putter in hand.
    private var scaleSlider: some View {
        let settings = Bindable(session.settings)
        let putter = session.club.isPutter
        return VStack(spacing: 2) {
            HStack {
                Text(putter ? "Putt scale" : "Swing scale").caption()
                Spacer()
                Text("\(session.settings.scale(for: session.club), specifier: "%.1f")×")
                    .font(.system(size: 15, weight: .bold, design: .rounded).monospacedDigit())
                    .foregroundStyle(Theme.chalk)
            }
            if putter {
                Slider(value: settings.puttScale, in: AppSettings.puttScaleRange, step: 0.05).tint(Theme.flag)
            } else {
                Slider(value: settings.scale, in: AppSettings.scaleRange, step: 0.1).tint(Theme.flag)
            }
        }
        .card(padding: 12)
    }
}

#Preview {
    ContentView(session: SwingSession())
}
