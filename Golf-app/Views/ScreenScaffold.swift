import SwiftUI

/// The space a screen's content gets between its header and its pinned controls, and the sizes that suit it:
/// big controls when there's room (a tall phone), smaller ones when there isn't (iPhone SE, a banner showing).
struct ScreenFit {
    /// Below this the screen is short: the spacing tightens.
    static let compactHeight: CGFloat = 620
    /// The sizes `ScreenScaffold` tries, biggest first (1 = `regular`, 0 = `small`).
    static let levels: [CGFloat] = [1, 0.75, 0.5, 0.25, 0]

    let height: CGFloat
    /// Where big controls sit between their small (0) and regular (1) sizes.
    var level: CGFloat = 0

    var compact: Bool { height < Self.compactHeight }
    var spacing: CGFloat { compact ? 6 : 10 }

    /// `regular` with room to spare, `small` without, or in between.
    func size(_ regular: CGFloat, _ small: CGFloat) -> CGFloat { (small + (regular - small) * level).rounded() }
}

/// A Play or Practice screen: the header (title, status, Settings) stays at the top, the `bottom` controls
/// (Menu, Mulligan, Back…) stay pinned just above the tab bar, and the content between them gets the biggest
/// control sizes that fit. When even the smallest don't (a banner showing, an 18-hole scorecard, a short phone)
/// it scrolls, so nothing ends up under the tab bar. A `Spacer` in the content fills any free space.
struct ScreenScaffold<Content: View, Bottom: View>: View {
    let title: String
    let session: SwingSession
    @ViewBuilder let content: (ScreenFit) -> Content
    @ViewBuilder let bottom: () -> Bottom

    var body: some View {
        VStack(spacing: 6) {
            AppHeader(title: title, session: session)
                .padding(.horizontal, 16)
            GeometryReader { proxy in
                let height = proxy.size.height
                ViewThatFits(in: .vertical) {
                    ForEach(ScreenFit.levels, id: \.self) { level in
                        column(ScreenFit(height: height, level: level)).frame(maxHeight: .infinity, alignment: .top)
                    }
                    ScrollView {
                        column(ScreenFit(height: height)).frame(minHeight: height, alignment: .top)
                    }
                    .scrollBounceBehavior(.basedOnSize)
                }
            }
            // Laid out after the content, so the content's height (and `fit`) is what is left above it.
            if Bottom.self != EmptyView.self {
                VStack(spacing: 8) { bottom() }
                    .padding(.horizontal, 16)
                    .padding(.bottom, 8)
            }
        }
    }

    private func column(_ fit: ScreenFit) -> some View {
        VStack(spacing: fit.spacing) { content(fit) }
            .padding(.horizontal, 16)
            .padding(.bottom, 8)
    }
}

extension ScreenScaffold where Bottom == EmptyView {
    /// A screen without pinned controls.
    init(title: String, session: SwingSession, @ViewBuilder content: @escaping (ScreenFit) -> Content) {
        self.init(title: title, session: session, content: content) { EmptyView() }
    }
}

extension View {
    /// Disabled and greyed out unless `usable` (e.g. game controls while the server is down).
    func usable(_ usable: Bool) -> some View {
        disabled(!usable).opacity(usable ? 1 : 0.4)
    }

    /// Dimmed (still usable, e.g. to pick the next club) while the sim can't take a swing; the instruction says why.
    func waiting(_ wait: String?) -> some View {
        opacity(wait == nil ? 1 : 0.45).animation(.default, value: wait)
    }
}
