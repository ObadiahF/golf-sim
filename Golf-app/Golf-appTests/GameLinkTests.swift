import Foundation
import Network
import Testing
@testable import Golf_app

/// GameLink against a fake game server: a WebSocket listener on loopback that says hello like
/// the real one, records what the remote sends and can push messages or drop the connection.
@MainActor
@Suite(.serialized)
struct GameLinkTests {
    final class FakeServer: @unchecked Sendable {
        private let listener: NWListener
        private let queue = DispatchQueue(label: "FakeGameServer")
        private let lock = NSLock()
        private var connections: [NWConnection] = []
        private var frames: [[String: Any]] = []
        private(set) var accepted = 0
        private(set) var ready = false
        let hello: String

        init(hello: String = #"{"type":"hello","role":"remote","simConnected":true,"remotes":["test"],"game":null,"state":null}"#) throws {
            self.hello = hello
            let params = NWParameters.tcp
            params.defaultProtocolStack.applicationProtocols.insert(NWProtocolWebSocket.Options(), at: 0)
            params.requiredLocalEndpoint = .hostPort(host: "127.0.0.1", port: .any)
            listener = try NWListener(using: params)
            listener.newConnectionHandler = { [weak self] in self?.accept($0) }
            listener.stateUpdateHandler = { [weak self] state in
                guard let self, case .ready = state else { return }
                lock.withLock { self.ready = true }
            }
            listener.start(queue: queue)
        }

        /// Once the listener is accepting connections.
        var port: Int? { lock.withLock { ready } ? listener.port.map { Int($0.rawValue) } : nil }
        var received: [[String: Any]] { lock.withLock { frames } }
        var types: [String] { received.compactMap { $0["type"] as? String } }

        func push(_ json: String) { lock.withLock { connections }.forEach { send(json, on: $0) } }

        /// Drops every connection, like a server restart.
        func dropAll() { lock.withLock { connections }.forEach { $0.cancel() } }

        func stop() {
            dropAll()
            listener.cancel()
        }

        private func accept(_ connection: NWConnection) {
            lock.withLock {
                connections.append(connection)
                accepted += 1
            }
            connection.stateUpdateHandler = { [weak self] state in
                if case .ready = state, let self { send(hello, on: connection) }
            }
            connection.start(queue: queue)
            receive(on: connection)
        }

        private func receive(on connection: NWConnection) {
            connection.receiveMessage { [weak self] data, _, _, error in
                guard let self, error == nil else { return }
                if let data, !data.isEmpty { lock.withLock { self.frames.append(jsonObject(data)) } }
                receive(on: connection)
            }
        }

        private func send(_ json: String, on connection: NWConnection) {
            let context = NWConnection.ContentContext(identifier: "text", metadata: [NWProtocolWebSocket.Metadata(opcode: .text)])
            connection.send(content: Data(json.utf8), contentContext: context, isComplete: true, completion: .idempotent)
        }
    }

    private func makeLink(_ server: FakeServer) async throws -> GameLink {
        await waitUntil { server.port != nil }
        let settings = try testSettings("GameLinkTests")
        settings.server = "127.0.0.1:\(try #require(server.port))"
        return GameLink(settings: settings, deviceName: "test")
    }

    @Test func connectsRelaysAndReconnects() async throws {
        let server = try FakeServer()
        defer { server.stop() }
        let link = try await makeLink(server)
        #expect(!link.simReady)
        link.start()
        defer { link.stop() }

        await waitUntil(seconds: 4) { link.isConnected }
        #expect(link.connection == .connected, "accepted \(server.accepted)")
        #expect(link.simReady)
        #expect(link.remotes == ["test"])

        server.push(#"{"type":"state","screen":"game","gameId":1,"currentPlayer":"Ann","hole":1,"par":4,"strokes":0,"club":"Driver","aim":0,"distanceToPin":380,"lie":"tee"}"#)
        await waitUntil { link.state != nil }
        #expect(link.state?.player == "Ann")
        #expect(link.screen == "game")

        link.nav(.select)
        link.aim(by: 1)
        let shot = Shot.from(Impact(rate: 20, face: 0, angle: 0, time: 0), club: Club.bag[0], scale: 1, faceSign: -1)
        #expect(link.sendShot(shot, id: 5))
        await waitUntil(seconds: 4) { server.types.count >= 3 }
        try #require(server.types == ["nav", "aim", "shot"])
        #expect(server.received[0]["key"] as? String == "select")
        #expect(server.received[2]["id"] as? Int == 5)

        server.push(#"{"type":"simStatus","connected":false}"#)
        await waitUntil { !link.simConnected }
        #expect(link.state == nil)
        #expect(!link.sendShot(shot, id: 6)) // falls back to UDP

        server.dropAll()
        await waitUntil { link.connection != .connected }
        if case .waiting = link.connection {} else { Issue.record("expected waiting, got \(link.connection)") }
        await waitUntil(seconds: 8) { link.isConnected }
        #expect(link.isConnected)
        #expect(server.accepted >= 2)
    }

    @Test func aFailedSendRedialsAndResyncs() async throws {
        let server = try FakeServer()
        defer { server.stop() }
        let link = try await makeLink(server)
        link.start()
        defer { link.stop() }
        await waitUntil(seconds: 4) { link.isConnected }
        let dead = try #require(link.socket)
        link.sendFailed(on: dead, reason: "Socket is not connected")
        #expect(link.lastError == "Socket is not connected")
        await waitUntil(seconds: 6) { server.accepted >= 2 && link.isConnected }
        #expect(server.accepted == 2 && link.simReady && link.socket !== dead) // a fresh hello, a fresh socket

        link.sendFailed(on: dead, reason: "late") // the old socket's failure leaves the new one alone
        try await Task.sleep(for: .milliseconds(300))
        #expect(link.isConnected && server.accepted == 2)
    }

    @Test func aRedialKeepsTheNewConnectionsSim() async throws {
        let server = try FakeServer()
        defer { server.stop() }
        let link = try await makeLink(server)
        link.start()
        defer { link.stop() }
        await waitUntil(seconds: 4) { link.simReady }
        link.reconnect()
        await waitUntil(seconds: 4) { link.simReady }
        try await Task.sleep(for: .milliseconds(300)) // the old loop winding down mustn't clear it
        #expect(link.simReady)
    }

    @Test func noServerMeansWaiting() async throws {
        let server = try FakeServer()
        let link = try await makeLink(server)
        server.stop() // nothing listening on that port any more
        link.start()
        defer { link.stop() }
        await waitUntil(seconds: 8) { if case .waiting = link.connection { true } else { false } }
        if case .waiting = link.connection {} else { Issue.record("expected waiting, got \(link.connection)") }
        #expect(!link.send(GameProtocol.Bare.ping))
        link.stop()
        #expect(link.connection == .idle)
    }

    @Test func messagesUpdateTheLink() throws {
        let link = GameLink(settings: try testSettings("GameLinkTests.handle"), deviceName: "test")
        link.handle(.hello(.init(simConnected: false, remotes: ["a"], game: nil, state: nil)))
        #expect(link.isConnected && !link.simReady)
        link.handle(.simStatus(connected: true))
        #expect(link.simReady)
        link.handle(.state(.init(screen: "holeComplete")))
        #expect(link.screen == "holeComplete" && link.showsScorecard)
        link.handle(.shotResult(.init(player: "Ann", carry: 100, total: 110, lie: "fairway", holed: false, strokes: 1)))
        #expect(link.lastShotResult?.total == 110)
        link.handle(.turn(.init(player: "Bob", hole: 2, strokes: 0)))
        #expect(link.lastTurn?.player == "Bob")
        link.handle(.error("no sim connected"))
        #expect(link.lastError == "no sim connected")
    }
}
