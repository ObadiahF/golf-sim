import UIKit

/// The few haptics the remote uses; you feel them with the phone in your grip.
enum Haptics {
    static func addressSet() { UINotificationFeedbackGenerator().notificationOccurred(.success) }
    static func shotSent() { UIImpactFeedbackGenerator(style: .heavy).impactOccurred() }
    static func received() { UIImpactFeedbackGenerator(style: .light).impactOccurred() }
    static func problem() { UINotificationFeedbackGenerator().notificationOccurred(.warning) }
    /// A remote-control button press.
    static func press() { UIImpactFeedbackGenerator(style: .rigid).impactOccurred() }
    static func tick() { UISelectionFeedbackGenerator().selectionChanged() }
}
