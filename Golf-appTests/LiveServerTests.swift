import Foundation
import Testing
@testable import Golf_app

/// Read-only checks against a real game server (nothing is created or sent to the sim). Run with
/// `TEST_RUNNER_GOLF_LIVE_SERVER=127.0.0.1:8080 xcodebuild test ...`; skipped otherwise.
private let liveServer = ProcessInfo.processInfo.environment["GOLF_LIVE_SERVER"]

@MainActor
@Suite(.enabled(if: liveServer != nil, "set TEST_RUNNER_GOLF_LIVE_SERVER to run"))
struct LiveServerTests {
    private func settings() throws -> AppSettings {
        let settings = try testSettings("LiveServerTests")
        settings.server = try #require(liveServer)
        return settings
    }

    @Test func restEndpointsDecode() async throws {
        let api = try #require(GameAPI.forSettings(try settings()))
        let current = try await api.currentGame()
        let recent = try await api.recentGames(limit: 3)
        _ = try await api.players()
        _ = try await api.leaderboard()
        if let current { #expect(try await api.game(id: current.id).id == current.id) }
        #expect(recent.count <= 3)
    }

    @Test func remoteGetsHello() async throws {
        let link = GameLink(settings: try settings(), deviceName: "SwingRemote tests")
        link.start()
        defer { link.stop() }
        await waitUntil(seconds: 5) { link.isConnected }
        #expect(link.isConnected)
        #expect(link.remotes.contains("SwingRemote tests"))
    }
}
