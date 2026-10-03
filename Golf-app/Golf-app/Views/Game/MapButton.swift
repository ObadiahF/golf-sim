import SwiftUI

extension GameProtocol.SimState {
    /// The course map is up on the TV.
    var showsMap: Bool { isGame && mapOpen == true }
}

extension GameLink {
    /// The TV shows its course map (what the Map button says); false without a sim.
    var mapShown: Bool { simReady && state?.showsMap == true }

    /// What the Map button sends: the opposite of what the TV shows now.
    var mapToggle: GameProtocol.ShowMap { GameProtocol.ShowMap(show: !mapShown) }

    /// Opens or closes the course map on the TV; the sim's next `state` says whether it is up (it only opens while
    /// a hole is being played, and closes itself when the ball is hit).
    func toggleMap() { send(mapToggle) }
}

/// Opens and closes the TV's course map (the hole from above with the aim line, so you can aim from it), lit while
/// it is up. Sits beside the aim control, the same height as its card.
struct MapButton: View {
    let game: GameLink
    var height: CGFloat = 72

    var body: some View {
        let on = game.mapShown
        Button {
            Haptics.press()
            game.toggleMap()
        } label: {
            VStack(spacing: 4) {
                Image(systemName: on ? "map.fill" : "map")
                    .font(.system(size: 22, weight: .bold))
                Text("Map")
                    .font(.system(size: 13, weight: .bold, design: .rounded))
            }
            .foregroundStyle(on ? Theme.fairwayBottom : Theme.chalk)
            .frame(width: 66, height: height)
            .background(on ? Theme.flag : Theme.card, in: .rect(cornerRadius: 20))
            .overlay(RoundedRectangle(cornerRadius: 20).stroke(Theme.cardStroke, lineWidth: 1))
        }
        .buttonStyle(PressDimStyle())
        .animation(.default, value: on)
        .accessibilityLabel("Course map")
        .accessibilityValue(on ? "Showing on the TV" : "Hidden")
        .accessibilityHint(on ? "Closes the map on the TV" : "Shows the whole hole from above on the TV")
    }
}
