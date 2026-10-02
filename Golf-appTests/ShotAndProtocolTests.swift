import Foundation
import Testing
@testable import Golf_app

struct ShotTests {
    private let driver = Club.bag[0]

    @Test func clubheadSpeedIsRateTimesRadiusTimesScale() {
        let shot = Shot.from(Impact(rate: 20, face: 0, angle: 0, time: 0), club: driver, scale: 1.5, faceSign: -1)
        #expect(abs(shot.clubSpeed - 20 * 1.65 * 1.5) < 1e-9)
        #expect(abs(shot.ballSpeed - shot.clubSpeed * 1.48) < 1e-9)
        #expect(shot.launch == 12)
        #expect(shot.backspin == 2600)
    }

    @Test func faceSignAndSpin() {
        let shot = Shot.from(Impact(rate: 20, face: 4, angle: 0, time: 0), club: driver, scale: 1, faceSign: -1)
        #expect(shot.face == -4)
        #expect(shot.azimuth == -3)
        #expect(shot.sidespin == -720)
        let flipped = Shot.from(Impact(rate: 20, face: 4, angle: 0, time: 0), club: driver, scale: 1, faceSign: 1)
        #expect(flipped.sidespin == 720)
    }

    @Test func faceIsClamped() {
        let shot = Shot.from(Impact(rate: 20, face: 40, angle: 0, time: 0), club: driver, scale: 1, faceSign: 1)
        #expect(shot.face == Shot.maxFace)
    }

    @Test func putterHasNoSidespin() {
        let putter = Club.bag.last!
        let shot = Shot.from(Impact(rate: 1.5, face: 3, angle: 0, time: 0), club: putter, scale: 1, faceSign: 1)
        #expect(putter.isPutter)
        #expect(shot.sidespin == 0)
        #expect(shot.backspin == 0)
    }

    @Test func clubIndexIsClamped() {
        #expect(Club.at(-3) == Club.bag.first)
        #expect(Club.at(99) == Club.bag.last)
    }
}

struct ProtocolTests {
    private func json(_ data: Data) throws -> [String: Any] {
        try #require(JSONSerialization.jsonObject(with: data) as? [String: Any])
    }

    @Test func shotDatagramKeepsTheV1Fields() throws {
        let shot = Shot.from(Impact(rate: 20, face: 4, angle: 0, time: 0), club: Club.bag[0], scale: 1, faceSign: -1)
        let data = try #require(SimProtocol.encode(SimProtocol.ShotMessage(id: 42, shot: shot)))
        let object = try json(data)
        for key in ["speed", "launch", "azimuth", "back", "side"] { #expect(object[key] is Double, "\(key)") }
        #expect(object["type"] as? String == "shot")
        #expect(object["id"] as? Int == 42)
        #expect(object["v"] as? Int == 2)
        #expect(object["club"] as? String == "Driver")
        #expect(abs((object["speed"] as? Double ?? 0) - 48.84) < 0.01)
        #expect(object["side"] as? Double == -720)
    }

    @Test func discoverDatagram() throws {
        let object = try json(try #require(SimProtocol.encode(SimProtocol.DiscoverMessage())))
        #expect(object["type"] as? String == "discover")
    }

    @Test func decodesUnityReplies() throws {
        // Unity's JsonUtility writes every field, so acks carry zeroed result fields too.
        let hello = try #require(SimProtocol.decodeReply(Data(#"{"v":2,"type":"hello","id":0,"status":"ready","name":"GOLF-PC","message":"","carry":0.0,"total":0.0,"offline":0.0,"surface":"","outcome":""}"#.utf8)))
        #expect(hello.type == "hello")
        #expect(hello.name == "GOLF-PC")

        let busy = try #require(SimProtocol.decodeReply(Data(#"{"v":2,"type":"ack","id":7,"status":"busy","message":"Ball is still moving"}"#.utf8)))
        #expect(busy.id == 7)

        let reply = try #require(SimProtocol.decodeReply(Data(#"{"v":2,"type":"result","id":7,"status":"ok","carry":231.4,"total":252.9,"offline":-6.2,"surface":"fairway","outcome":"Stopped"}"#.utf8)))
        let result = try #require(SimResult(reply))
        #expect(result.id == 7)
        #expect(result.carry == 231.4)
        #expect(result.surface == "fairway")
        #expect(SimResult(hello) == nil)
    }

    @Test func garbageIsIgnored() {
        #expect(SimProtocol.decodeReply(Data("not json".utf8)) == nil)
        #expect(SimProtocol.decodeReply(Data(#"{"speed":3}"#.utf8)) == nil)
    }

    @MainActor @Test func ackStatusMapsToDelivery() {
        func ack(_ status: String) -> SimProtocol.Reply { SimProtocol.Reply(type: "ack", id: 1, status: status, message: "x") }
        #expect(SimLink.delivery(for: ack("ok")) == .received)
        #expect(SimLink.delivery(for: ack("busy")) == .busy)
        #expect(SimLink.delivery(for: ack("error")) == .rejected("x"))
    }
}

struct LocalSubnetTests {
    private func ip(_ a: UInt32, _ b: UInt32, _ c: UInt32, _ d: UInt32) -> UInt32 { a << 24 | b << 16 | c << 8 | d }

    @Test func homeWifiSlash24() {
        let hosts = LocalSubnet.hosts(address: ip(192, 168, 1, 37), netmask: 0xFFFF_FF00, cap: 1024)
        #expect(hosts.count == 254)
        #expect(LocalSubnet.dotted(hosts.first!) == "192.168.1.1")
        #expect(LocalSubnet.dotted(hosts.last!) == "192.168.1.254")
    }

    @Test func personalHotspotSlash28() {
        let hosts = LocalSubnet.hosts(address: ip(172, 20, 10, 1), netmask: 0xFFFF_FFF0, cap: 1024)
        #expect(hosts.count == 14)
        #expect(LocalSubnet.dotted(hosts.last!) == "172.20.10.14")
    }

    @Test func bigSubnetsAreNarrowedToSlash24() {
        let hosts = LocalSubnet.hosts(address: ip(10, 0, 5, 9), netmask: 0xFFFF_0000, cap: 1024)
        #expect(hosts.count == 254)
        #expect(LocalSubnet.dotted(hosts.first!) == "10.0.5.1")
    }

    @Test func pointToPointHasNoHosts() {
        #expect(LocalSubnet.hosts(address: ip(10, 0, 0, 1), netmask: 0xFFFF_FFFE, cap: 1024).isEmpty)
    }
}
