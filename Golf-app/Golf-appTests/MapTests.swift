import Foundation
import Testing
@testable import Golf_app

/// The course map: the Map button's `map` message and the sim's `state.mapOpen`.
@MainActor
struct MapTests {
    private static func state(_ screen: String = "game", mapOpen: Bool?) -> GameProtocol.SimState {
        GameProtocol.SimState(screen: screen, mapOpen: mapOpen)
    }

    @Test func showMapEncodes() {
        #expect(jsonObject(GameProtocol.encode(GameProtocol.ShowMap(show: true))) as NSDictionary == ["type": "map", "show": true])
        #expect(jsonObject(GameProtocol.encode(GameProtocol.ShowMap(show: false))) as NSDictionary == ["type": "map", "show": false])
    }

    @Test func decodesMapOpenAndOlderSimsWithout() {
        guard case .state(let open) = GameProtocol.decode(Data(#"{"type":"state","screen":"game","mapOpen":true}"#.utf8)),
              case .state(let older) = GameProtocol.decode(Data(#"{"type":"state","screen":"game"}"#.utf8))
        else { Issue.record("not a state"); return }
        #expect(open.mapOpen == true && open.showsMap)
        #expect(older.mapOpen == nil && !older.showsMap)
        // Only the game screen shows the map (a stale flag on a scorecard doesn't light the button).
        #expect(!Self.state("holeComplete", mapOpen: true).showsMap)
    }

    @Test func buttonFollowsTheSimAndTogglesFromIt() throws {
        let link = GameLink(settings: try testSettings("MapTests.link"), deviceName: "test")
        link.handle(.hello(.init(simConnected: true, remotes: [], game: nil, state: Self.state(mapOpen: false))))
        #expect(!link.mapShown)
        #expect(link.mapToggle == GameProtocol.ShowMap(show: true))
        link.handle(.state(Self.state(mapOpen: true)))
        #expect(link.mapShown)
        #expect(link.mapToggle == GameProtocol.ShowMap(show: false))
        // The sim closes it when the ball is hit: the next state turns the button off.
        link.handle(.state(Self.state(mapOpen: false)))
        #expect(!link.mapShown)
        // No sim, no map.
        link.handle(.state(Self.state(mapOpen: true)))
        link.handle(.simStatus(connected: false))
        #expect(!link.mapShown)
    }
}
