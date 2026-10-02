import SwiftUI

/// The space a screen's content gets below its header, and the sizes that suit it: big controls on a tall
/// phone, smaller ones on a short one (iPhone SE).
struct ScreenFit {
    /// Below this the screen is short: big controls shrink and the spacing tightens.
    static let compactHeight: CGFloat = 620

    let height: CGFloat

    var compact: Bool { height < Self.compactHeight }
    var spacing: CGFloat { compact ? 6 : 10 }

    /// `regular` on a tall screen, `small` on a short one.
    func size(_ regular: CGFloat, _ small: CGFloat) -> CGFloat { compact ? small : regular }
}

/// A Play or Practice screen: the header (title, status, Settings) stays at the top and the content scrolls
/// when it doesn't fit, so nothing ends up above the screen or under the tab bar. On a phone where it fits it
/// doesn't scroll at all, and a `Spacer` in the content still fills the free space.
struct ScreenScaffold<Content: View>: View {
    let title: String
    let session: SwingSession
    @ViewBuilder let content: (ScreenFit) -> Content

    var body: some View {
        VStack(spacing: 6) {
            AppHeader(title: title, session: session)
                .padding(.horizontal, 16)
            GeometryReader { proxy in
                let fit = ScreenFit(height: proxy.size.height)
                ScrollView {
                    VStack(spacing: fit.spacing) { content(fit) }
                        .padding(.horizontal, 16)
                        .padding(.bottom, 8)
                        .frame(minHeight: proxy.size.height, alignment: .top)
                }
                .scrollBounceBehavior(.basedOnSize)
            }
        }
    }
}

extension View {
    /// Disabled and greyed out unless `usable` (e.g. game controls while the server is down).
    func usable(_ usable: Bool) -> some View {
        disabled(!usable).opacity(usable ? 1 : 0.4)
    }
}
