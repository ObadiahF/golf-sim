import SwiftUI

/// A button that fires once on touch-down and then repeats while held (aim nudges). The repeat runs in a task
/// keyed on the press, so it stops on release, when the view goes away (the Play screen changes mid-press),
/// when the button is disabled, and when the app leaves the foreground.
struct HoldRepeatButton<Label: View>: View {
    var interval: Duration = HoldRepeat.interval
    /// Pause before repeating starts.
    var delay: Duration = HoldRepeat.delay
    let action: () -> Void
    @ViewBuilder let label: () -> Label

    /// Resets by itself when the touch ends or the gesture is cancelled.
    @GestureState private var pressed = false
    /// The touch-down action has fired for this press (a quick tap can begin and end within one frame).
    @State private var fired = false
    @Environment(\.isEnabled) private var isEnabled
    @Environment(\.scenePhase) private var scenePhase

    private var repeating: Bool { HoldRepeat.shouldRun(pressed: pressed, enabled: isEnabled, phase: scenePhase) }

    var body: some View {
        label()
            .opacity(repeating ? 0.6 : 1)
            .contentShape(.rect)
            .gesture(
                DragGesture(minimumDistance: 0)
                    .updating($pressed) { _, pressed, _ in pressed = true }
                    .onChanged { _ in
                        guard !fired else { return }
                        fired = true
                        action()
                    }
                    .onEnded { _ in fired = false }
            )
            // A cancelled gesture skips onEnded but still resets `pressed`.
            .onChange(of: pressed) { _, pressed in if !pressed { fired = false } }
            // Cancelled when `repeating` changes and when the view disappears.
            .task(id: repeating) {
                guard repeating else { return }
                await HoldRepeat.repeating(delay: delay, interval: interval, action: action)
            }
            .accessibilityAddTraits(.isButton)
            .accessibilityAction { action() }
    }
}

/// The hold-to-repeat timing, apart from the view so tests can drive it.
enum HoldRepeat {
    nonisolated static let interval: Duration = .milliseconds(120)
    nonisolated static let delay: Duration = .milliseconds(350)

    /// Repeat only while pressed, enabled and in the foreground.
    static func shouldRun(pressed: Bool, enabled: Bool, phase: ScenePhase) -> Bool {
        pressed && enabled && phase == .active
    }

    /// After `delay`, fires every `interval` until the task is cancelled (the touch-down action is the button's).
    static func repeating(delay: Duration = Self.delay, interval: Duration = Self.interval, action: () -> Void) async {
        try? await Task.sleep(for: delay)
        while !Task.isCancelled {
            action()
            try? await Task.sleep(for: interval)
        }
    }
}
