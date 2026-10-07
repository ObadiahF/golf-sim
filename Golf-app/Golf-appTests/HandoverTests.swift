import Foundation
import Testing
@testable import Golf_app

/// One phone passed between players: when the sim puts someone else up, the phone stops listening for a swing (so
/// handing it over can't fire a stroke) until the next player taps Address; a player on their own keeps the re-arm.
@MainActor
struct HandoverTests {
    private func state(_ player: String) throws -> GameProtocol.SimState {
        let json = #"{"type":"state","screen":"game","canShoot":true,"waitReason":"","currentPlayer":"\#(player)","hole":1,"strokes":0,"club":"Driver"}"#
        guard case .state(let state)? = GameProtocol.decode(Data(json.utf8)) else { throw CancellationError() }
        return state
    }

    /// Ann is up and her phone is armed (addressed and held still).
    private func armed(_ suite: String) throws -> (SwingSession, ShotReadinessTests.ManualMotion) {
        let motion = ShotReadinessTests.ManualMotion()
        let session = SwingSession(settings: try testSettings(suite), motion: motion)
        session.game.handle(.hello(.init(simConnected: true, remotes: ["me"], game: nil, state: try state("Ann"))))
        session.game.handle(.turn(.init(player: "Ann", hole: 1, strokes: 0)))
        session.address()
        motion.feed(stride(from: 0.0, through: 2.0, by: 0.01).map { MotionSample(time: $0, rate: 0.02, angle: 0.1, face: 0) })
        #expect(session.stage == .ready)
        return (session, motion)
    }

    private func swing(_ motion: ShotReadinessTests.ManualMotion) {
        motion.feed(SyntheticSwing.samples(.full(impactRate: 20), start: 2.1))
    }

    @Test func anotherPlayerUpDisarmsThePhone() throws {
        let (session, motion) = try armed("HandoverTests.turn")
        session.game.handle(.turn(.init(player: "Sis", hole: 1, strokes: 0)))
        #expect(session.stage == .idle)
        #expect(session.instruction == "Sis is up: tap Address when ready")
        swing(motion) // waved about while it's handed over
        #expect(session.lastShot == nil && session.lastShotID == nil)
        session.address()
        #expect(session.handedTo == nil && session.instruction == "Take your grip… hold still")
    }

    @Test func aStateWithAnotherPlayerDisarmsToo() throws {
        let (session, motion) = try armed("HandoverTests.state")
        session.game.handle(.state(try state("Sis")))
        #expect(session.stage == .idle && session.handedTo == "Sis")
        swing(motion)
        #expect(session.lastShot == nil)
    }

    @Test func thePlayerOnTheirOwnStaysArmed() throws {
        let (session, motion) = try armed("HandoverTests.solo")
        session.game.handle(.turn(.init(player: "Ann", hole: 1, strokes: 1)))
        session.game.handle(.state(try state("Ann")))
        #expect(session.stage == .ready && session.handedTo == nil)
        swing(motion)
        #expect(session.lastShot != nil)
    }
}
