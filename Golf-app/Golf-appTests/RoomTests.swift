import Foundation
import Testing
@testable import Golf_app

/// Rooms: the room code setting, the WebSocket URL that carries it, and the room fields in hello and game.
@MainActor
struct RoomTests {
    // MARK: Setting

    @Test func codesAreTrimmedAndUpperCased() {
        #expect(AppConfig.normalizedRoom("  k7qf2 \n") == "K7QF2")
        #expect(AppConfig.normalizedRoom("") == "")
    }

    @Test func codesAreFourToEightLettersOrDigits() {
        for good in ["", "  ", "ABCD", "k7qf2", "ABCD1234", " abcd "] { #expect(AppConfig.roomProblem(good) == nil, "\(good)") }
        for bad in ["ABC", "ABCDEFGHI", "AB-CD", "AB CD", "ÄBCD", "ABC_1"] { #expect(AppConfig.roomProblem(bad) != nil, "\(bad)") }
    }

    @Test func roomIsSavedNormalizedAndBadCodesAreRefused() throws {
        let settings = try testSettings("RoomTests.save")
        #expect(settings.room == "") // the default room
        #expect(settings.setRoom(" k7qf2 ") == nil)
        #expect(settings.room == "K7QF2")
        #expect(settings.setRoom("no!") != nil)
        #expect(settings.room == "K7QF2") // unchanged
        let defaults = try #require(UserDefaults(suiteName: "RoomTests.save"))
        #expect(AppSettings(defaults: defaults).room == "K7QF2")
        #expect(settings.setRoom("") == nil)
        #expect(AppSettings(defaults: defaults).room == "")
    }

    @Test func aMalformedStoredCodeLoadsAsTheDefaultRoom() throws {
        let defaults = try #require(UserDefaults(suiteName: "RoomTests.stored"))
        defaults.removePersistentDomain(forName: "RoomTests.stored")
        defaults.set("bad code", forKey: "room")
        #expect(AppSettings(defaults: defaults).room == "")
        defaults.set("abcd", forKey: "room")
        #expect(AppSettings(defaults: defaults).room == "ABCD")
    }

    // MARK: WebSocket URL

    private let server = URL(string: "https://golf.example")!

    @Test func socketURLCarriesTheRoom() throws {
        let url = try #require(GameLink.socketURL(server: server, name: "Ann", room: "K7QF2"))
        #expect(url.query(percentEncoded: true) == "token=\(AppConfig.percentEncode(AppConfig.serverToken))&role=remote&name=Ann&room=K7QF2")
        let odd = try #require(GameLink.socketURL(server: server, name: "Ann", room: "A B+"))
        #expect(odd.query(percentEncoded: true)?.hasSuffix("&room=A%20B%2B") == true)
    }

    @Test func defaultRoomSendsNoRoomParameter() throws {
        let url = try #require(GameLink.socketURL(server: server, name: "Ann", room: ""))
        #expect(url.query(percentEncoded: true)?.contains("room") == false)
    }

    // MARK: Decoding

    @Test func helloAndGameDecodeTheirRoom() throws {
        let game = GameProtocolTests.gameJSON.replacingOccurrences(of: #""winners":[]"#, with: #""winners":[],"room":"K7QF2""#)
        guard case .hello(let hello)? = GameProtocol.decode(Data(#"{"type":"hello","role":"remote","simConnected":true,"remotes":[],"room":"K7QF2","game":\#(game),"state":null}"#.utf8))
        else { Issue.record("not a hello"); return }
        #expect(hello.room == "K7QF2")
        #expect(hello.game?.room == "K7QF2")
        guard case .hello(let lobby)? = GameProtocol.decode(Data(#"{"type":"hello","simConnected":false,"room":"","game":null}"#.utf8))
        else { Issue.record("not a hello"); return }
        #expect(lobby.room == "") // the default room
    }

    @Test func olderServersWithoutRoomStillDecode() throws {
        guard case .hello(let hello)? = GameProtocol.decode(Data(#"{"type":"hello","role":"remote","simConnected":true,"remotes":["iPhone"],"game":\#(GameProtocolTests.gameJSON),"state":null}"#.utf8))
        else { Issue.record("not a hello"); return }
        #expect(hello.room == nil)
        #expect(hello.game?.id == 12 && hello.game?.room == nil)
        guard case .gameStarted(let game)? = GameProtocol.decode(Data(#"{"type":"gameStarted","game":\#(GameProtocolTests.gameJSON)}"#.utf8))
        else { Issue.record("not gameStarted"); return }
        #expect(game.room == nil)
    }
}
