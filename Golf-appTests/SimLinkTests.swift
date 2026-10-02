import Foundation
import Testing
@testable import Golf_app

/// End to end over loopback: the real SimLink against a fake sim that answers like the Unity receiver.
@MainActor
struct SimLinkTests {
    /// A fake sim: hello to discover, ack to shots (busy for the second), then a result for the first.
    final class FakeSim: @unchecked Sendable {
        let port: UInt16 = 42_421
        private var socket: UDPSocket?
        private var shots = 0

        init() throws {
            socket = try UDPSocket(port: port) { [weak self] data, ip, port in self?.answer(data, ip, port) }
        }

        func close() { socket?.close() }

        private func answer(_ data: Data, _ ip: String, _ port: UInt16) {
            guard let object = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else { return }
            let id = object["id"] as? Int ?? 0
            switch object["type"] as? String {
            case "discover":
                reply(#"{"v":2,"type":"hello","name":"Fake Sim","status":"ready"}"#, ip, port)
            case "shot":
                shots += 1
                if shots == 1 {
                    reply(#"{"v":2,"type":"ack","id":\#(id),"status":"ok"}"#, ip, port)
                    reply(#"{"v":2,"type":"result","id":\#(id),"carry":201.5,"total":220.2,"offline":-3.1,"surface":"fairway","outcome":"Stopped"}"#, ip, port)
                } else {
                    reply(#"{"v":2,"type":"ack","id":\#(id),"status":"busy","message":"Ball is still moving"}"#, ip, port)
                }
            default: break
            }
        }

        private func reply(_ json: String, _ ip: String, _ port: UInt16) {
            socket?.send(Data(json.utf8), toIP: ip, port: port)
        }
    }

    @Test func connectsSendsShotsAndGetsResults() async throws {
        let sim = try FakeSim()
        defer { sim.close() }
        let settings = try testSettings("SimLinkTests")
        settings.host = "127.0.0.1"
        let link = SimLink(settings: settings, port: sim.port)
        var deliveries: [Delivery] = []
        var results: [SimResult] = []
        link.onDelivery = { _, delivery in deliveries.append(delivery) }
        link.onResult = { results.append($0) }

        link.start()
        defer { link.stop() }
        await waitUntil { link.connection == .connected(name: "Fake Sim") }
        #expect(link.connection == .connected(name: "Fake Sim"))

        let shot = Shot.from(Impact(rate: 20, face: 2, angle: 0, time: 0), club: Club.bag[3], scale: 1, faceSign: -1)
        let first = link.send(shot)
        await waitUntil { deliveries.contains(.received) && !results.isEmpty }
        #expect(deliveries == [.sending, .received])
        #expect(results.first?.id == first)
        #expect(results.first?.carry == 201.5)

        link.send(shot)
        await waitUntil { deliveries.contains(.busy) }
        #expect(deliveries.last == .busy)
    }
}
