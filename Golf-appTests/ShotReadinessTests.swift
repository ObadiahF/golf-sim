import Foundation
import Testing
@testable import Golf_app

/// R-6: the sim says when it can't take a swing (`canShoot` / `waitReason` in its state, `shotRejected` after a
/// shot it couldn't hit), and the app shows why instead of sending swings into the void.
@MainActor
struct ShotReadinessTests {
    /// Hands samples to the session straight away, so a test can play a whole address and swing synchronously.
    final class ManualMotion: MotionSource {
        private var handler: ((MotionSample) -> Void)?
        var isAvailable: Bool { true }
        func start(_ handler: @escaping (MotionSample) -> Void) { self.handler = handler }
        func stop() { handler = nil }
        func captureAddress() {}
        func feed(_ samples: [MotionSample]) { samples.forEach { handler?($0) } }
    }

    private func state(_ json: String) throws -> GameProtocol.SimState {
        guard case .state(let state)? = GameProtocol.decode(Data(json.utf8)) else { throw CancellationError() }
        return state
    }

    /// A session whose game link has a sim connected (no real socket: shots over it fail and fall back to UDP).
    private func session(_ suite: String) throws -> (SwingSession, ManualMotion) {
        let motion = ManualMotion()
        let session = SwingSession(settings: try testSettings(suite), motion: motion)
        session.game.handle(.hello(.init(simConnected: true, remotes: ["test"], game: nil, state: nil)))
        return (session, motion)
    }

    /// Address (held still past the settle time), then a full swing.
    private func swing(_ session: SwingSession, _ motion: ManualMotion) {
        session.address()
        let still = stride(from: 0.0, through: 2.0, by: 0.01).map { MotionSample(time: $0, rate: 0.02, angle: 0.1, face: 0) }
        motion.feed(still)
        motion.feed(SyntheticSwing.samples(.full(impactRate: 20), start: 2.1))
    }

    @Test func decodesCanShootAndWaitReason() throws {
        let waiting = try state(#"{"type":"state","screen":"game","canShoot":false,"waitReason":"Wait for the next turn"}"#)
        #expect(waiting.canShoot == false && !waiting.acceptsShots)
        #expect(waiting.shotWait == "Wait for the next turn")
        #expect(PlayScreen.of(waiting) == .gameplay) // the gameplay view stays up between shots

        let ready = try state(#"{"type":"state","screen":"game","canShoot":true,"waitReason":""}"#)
        #expect(ready.acceptsShots && ready.shotWait == nil)
        let older = try state(#"{"type":"state","screen":"game"}"#) // older sims: always ready
        #expect(older.canShoot == nil && older.acceptsShots && older.shotWait == nil)
        #expect(try state(#"{"type":"state","screen":"game","canShoot":false,"waitReason":""}"#).shotWait == "Wait for the next shot")
        #expect(!(try state(#"{"type":"state","screen":"menu","canShoot":false}"#)).acceptsShots)
        #expect(try state(#"{"type":"state","screen":"menu","canShoot":false}"#).shotWait == nil)
    }

    @Test func decodesShotRejected() {
        func decode(_ json: String) -> GameProtocol.Incoming? { GameProtocol.decode(Data(json.utf8)) }
        #expect(decode(#"{"type":"shotRejected","reason":"Wait for the next turn","id":7}"#)
            == .shotRejected(.init(reason: "Wait for the next turn", id: 7)))
        #expect(decode(#"{"type":"shotRejected","reason":"Instant replay"}"#) == .shotRejected(.init(reason: "Instant replay", id: nil)))
        #expect(decode(#"{"type":"shotRejected"}"#) == nil) // reason is required
    }

    @Test func replayScreenShowsTheRemote() throws {
        let replay = try state(#"{"type":"state","screen":"replay","canShoot":false,"waitReason":"Instant replay"}"#)
        #expect(PlayScreen.of(replay) == .remote)
        #expect(PlayScreen.of(try state(#"{"type":"state","screen":"somethingNew"}"#)) == .remote)
        let link = GameLink(settings: try testSettings("ShotReadinessTests.replay"), deviceName: "test")
        link.handle(.hello(.init(simConnected: true, remotes: [], game: nil, state: replay)))
        #expect(RemoteView.screenTitle(link) == "Instant replay")
        #expect(RemoteView.hint(link) == "Press OK to skip it.")
        link.handle(.state(try state(#"{"type":"state","screen":"somethingNew"}"#)))
        #expect(RemoteView.screenTitle(link) == "Somethingnew") // capitalized, still readable
    }

    @Test func linkReportsTheWaitOnlyWithASim() throws {
        let link = GameLink(settings: try testSettings("ShotReadinessTests.link"), deviceName: "test")
        link.handle(.hello(.init(simConnected: true, remotes: [], game: nil, state: nil)))
        link.handle(.state(try state(#"{"type":"state","screen":"game","canShoot":false,"waitReason":"Ball in flight"}"#)))
        #expect(link.shotWait == "Ball in flight")
        link.handle(.simStatus(connected: false))
        #expect(link.shotWait == nil)
    }

    @Test func swingWhileTheSimCantShootSendsNothing() throws {
        let (session, motion) = try session("ShotReadinessTests.blocked")
        session.game.handle(.state(try state(#"{"type":"state","screen":"game","canShoot":false,"waitReason":"Wait for the next turn"}"#)))
        #expect(session.instruction == "Wait for the next turn")
        swing(session, motion)
        #expect(session.lastShot == nil && session.lastShotID == nil) // nothing over the WebSocket or UDP
        #expect(session.delivery == .rejected("Wait for the next turn"))
    }

    @Test func shotRejectedShowsTheReasonAndStopsWaiting() throws {
        let (session, motion) = try session("ShotReadinessTests.rejected")
        session.game.handle(.state(try state(#"{"type":"state","screen":"game","canShoot":true,"waitReason":""}"#)))
        swing(session, motion)
        let id = try #require(session.lastShotID)
        #expect(session.lastShot != nil)
        session.game.handle(.shotRejected(.init(reason: "Someone else's", id: id + 100))) // another phone's shot
        #expect(session.delivery != .rejected("Someone else's"))
        session.game.handle(.shotRejected(.init(reason: "Wait for the next turn", id: id)))
        #expect(session.delivery == .rejected("Wait for the next turn"))
        #expect(session.result == nil)
    }

    @Test func rejectedPuttClearsTheMeter() {
        var meter = PuttMeter()
        meter.strike(distance: 3, toHole: 5)
        meter.cancel()
        #expect(meter.struck == nil)
        meter.finish(GameProtocol.ShotResult(player: "Obi", total: 3, lie: "green"))
        #expect(meter.result == nil) // no putt waiting any more
    }
}
