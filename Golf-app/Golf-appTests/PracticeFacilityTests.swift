import Foundation
import Testing
@testable import Golf_app

/// The practice facilities: `state.practice`, `attempts` and `made` from the sim, and the views they pick.
@MainActor
struct PracticeFacilityTests {
    private func state(_ json: String) throws -> GameProtocol.SimState {
        guard case .state(let s) = GameProtocol.decode(Data(json.utf8)) else { throw DecodeFailure() }
        return s
    }

    private struct DecodeFailure: Error {}

    @Test func drivingRangePlaysInTheGameplayView() throws {
        let range = try state(#"{"type":"state","screen":"game","canShoot":true,"currentPlayer":"","club":"7 Iron","distanceToPin":150.0,"lie":"tee","putting":false,"practice":"range","attempts":12,"made":0}"#)
        #expect(range.facility == .range)
        #expect(range.attempts == 12)
        #expect(PlayScreen.of(range) == .gameplay)
        #expect(range.playTitle == "Driving Range")
        #expect(range.madeSummary == nil)
        // Between shots the next ball is being teed up: still the gameplay view, with the sim's reason.
        let teeing = try state(#"{"type":"state","screen":"game","canShoot":false,"waitReason":"Teeing up the next ball","practice":"range","attempts":13}"#)
        #expect(PlayScreen.of(teeing) == .gameplay)
        #expect(teeing.shotWait == "Teeing up the next ball")
        #expect(!teeing.offersReplay)
    }

    @Test func puttingGreenPlaysInThePuttingViewWithItsMadeCount() throws {
        let green = try state(#"{"type":"state","screen":"game","canShoot":true,"club":"Putter","lie":"green","putting":true,"puttDistance":6.0,"stimp":9.3,"puttPlaysAs":6.4,"practice":"puttingGreen","attempts":7,"made":3}"#)
        #expect(green.facility == .puttingGreen)
        #expect(PlayScreen.of(green) == .putting)
        #expect(green.playTitle == "Putting Green")
        #expect(green.madeSummary == "Made 3 of 7")
        let fresh = try state(#"{"type":"state","screen":"game","putting":true,"practice":"puttingGreen","attempts":0,"made":0}"#)
        #expect(fresh.madeSummary == "No putts yet")
        // Paused on the green: the remote, as anywhere.
        #expect(PlayScreen.of(try state(#"{"type":"state","screen":"paused","putting":true,"practice":"puttingGreen"}"#)) == .remote)
    }

    @Test func roundsAndOlderSimsHaveNoFacility() throws {
        let round = try state(#"{"type":"state","screen":"game","currentPlayer":"Ann","hole":3,"par":4,"practice":"","attempts":0,"made":0}"#)
        #expect(round.facility == nil)
        #expect(round.playTitle == "Ann")
        #expect(round.madeSummary == nil)
        let older = try state(#"{"type":"state","screen":"game","currentPlayer":""}"#)
        #expect(older.practice == nil && older.facility == nil)
        #expect(older.playTitle == "Practice")
        // A facility this app doesn't know yet plays like the practice hole.
        #expect(try state(#"{"type":"state","screen":"game","practice":"chippingArea"}"#).facility == nil)
    }
}
