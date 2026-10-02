import Foundation
import Observation
import UIKit

/// The phone's WebSocket to the game server, as a `remote`: sends menu navigation, club, aim and
/// shots (relayed to the sim) and keeps the sim's latest state, scorecard and shot result.
/// Reconnects with backoff (1, 2, 5 s) and pings every 20 s; a connection counts as up once the
/// server's `hello` arrives.
@Observable
final class GameLink {
    enum Connection: Equatable {
        case idle
        case connecting
        case connected
        /// Lost or refused; retrying after a pause.
        case waiting(String)
    }

    static let pingInterval: Duration = .seconds(20)
    /// No frame (not even a pong) for this long means the connection is dead.
    static let silenceTimeout: TimeInterval = 50
    /// No `hello` this long after dialling means try again.
    static let connectTimeout: Duration = .seconds(5)
    /// Pause before each retry; the last one repeats.
    static let backoff: [Double] = [1, 2, 5]
    /// The server keeps device names to 40 characters.
    static let maxNameLength = 40

    private(set) var connection: Connection = .idle
    private(set) var simConnected = false
    /// Device names of the connected remotes (this phone included).
    private(set) var remotes: [String] = []
    /// What the sim shows; nil until it reports.
    private(set) var state: GameProtocol.SimState?
    /// The current or last game's scorecard.
    private(set) var game: GameView?
    private(set) var lastShotResult: GameProtocol.ShotResult?
    private(set) var lastTurn: GameProtocol.Turn?
    private(set) var lastError: String?
    /// Goes up with every scorecard change (`gameStarted`, `scorecard`, `gameFinished`), so score screens can reload.
    private(set) var scoresRevision = 0

    /// Every decoded message, after the link's own state is updated.
    @ObservationIgnored var onMessage: ((GameProtocol.Incoming) -> Void)?

    @ObservationIgnored private let settings: AppSettings
    @ObservationIgnored private let session: URLSession
    @ObservationIgnored private let deviceName: String
    @ObservationIgnored private var socket: URLSessionWebSocketTask?
    @ObservationIgnored private var loop: Task<Void, Never>?
    @ObservationIgnored private var lastReceived = Date()

    init(settings: AppSettings, session: URLSession = URLSession(configuration: .default), deviceName: String = UIDevice.current.name) {
        self.settings = settings
        self.session = session
        self.deviceName = String(deviceName.prefix(Self.maxNameLength))
    }

    var isConnected: Bool { connection == .connected }
    /// A sim is listening: commands reach it, and shots go over the WebSocket rather than UDP.
    var simReady: Bool { isConnected && simConnected }
    var screen: String { state?.screen ?? "menu" }
    /// Why the sim can't take a swing right now (between shots, screen still "game"); nil when it can, or no sim.
    var shotWait: String? { simReady ? state?.shotWait : nil }
    /// Between holes and after the last one, the sim shows the scorecard (Select continues).
    var showsScorecard: Bool { state?.showsScorecard == true }
    /// Why the server link is down, for the "Disconnected" banner; nil while connected.
    var outage: String? {
        switch connection {
        case .connected: nil
        case .waiting(let reason): reason
        case .idle, .connecting: "Reconnecting…"
        }
    }

    // MARK: Lifecycle

    func start() {
        guard loop == nil else { return }
        loop = Task { [weak self] in await self?.run() }
    }

    func stop() {
        loop?.cancel()
        loop = nil
        socket?.cancel(with: .goingAway, reason: nil)
        socket = nil
        connection = .idle
        simConnected = false
    }

    /// Drops the connection and dials again now (host or port changed).
    func reconnect() {
        stop()
        start()
    }

    private func run() async {
        var failures = 0
        while !Task.isCancelled {
            guard let url = socketURL() else {
                connection = .waiting("No server address")
                try? await Task.sleep(for: .seconds(2))
                continue
            }
            connection = .connecting
            let ws = session.webSocketTask(with: url)
            socket = ws
            lastReceived = Date()
            ws.resume()
            let watchdog = Task { [weak self] in
                try? await Task.sleep(for: Self.connectTimeout)
                guard !Task.isCancelled, let self, socket === ws, connection == .connecting else { return }
                ws.cancel(with: .goingAway, reason: nil) // receive() throws and we dial again
            }
            let pinger = Task { [weak self] in
                while !Task.isCancelled {
                    try? await Task.sleep(for: Self.pingInterval)
                    guard !Task.isCancelled else { return }
                    self?.ping(ws)
                }
            }
            let (reason, greeted) = await receive(on: ws)
            pinger.cancel()
            watchdog.cancel()
            ws.cancel(with: .goingAway, reason: nil)
            if socket === ws { socket = nil }
            simConnected = false
            guard !Task.isCancelled else { return }
            failures = greeted ? 0 : failures + 1
            connection = .waiting(reason)
            try? await Task.sleep(for: .seconds(Self.backoff[min(failures, Self.backoff.count - 1)]))
        }
    }

    /// Reads frames until the socket fails; returns why, and whether the server said hello.
    private func receive(on ws: URLSessionWebSocketTask) async -> (String, Bool) {
        var greeted = false
        while !Task.isCancelled {
            do {
                let data: Data = switch try await ws.receive() {
                case .string(let text): Data(text.utf8)
                case .data(let data): data
                @unknown default: Data()
                }
                guard socket === ws, let message = GameProtocol.decode(data) else { continue }
                if case .hello = message { greeted = true }
                handle(message)
            } catch {
                return (Self.describe(error), greeted)
            }
        }
        return ("Stopped", greeted)
    }

    private func socketURL() -> URL? {
        settings.serverURL.flatMap { Self.socketURL(server: $0, name: deviceName) }
    }

    /// `ws(s)://<server>/ws?token=…&role=remote&name=<device>`, every value percent-encoded.
    static func socketURL(server: URL, name: String) -> URL? {
        AppConfig.webSocketURL(server: server, query: ["token": AppConfig.serverToken, "role": "remote", "name": name])
    }

    private func ping(_ ws: URLSessionWebSocketTask) {
        guard socket === ws else { return }
        if Date().timeIntervalSince(lastReceived) > Self.silenceTimeout {
            ws.cancel(with: .goingAway, reason: nil) // receive() throws and the loop reconnects
            return
        }
        send(GameProtocol.Bare.ping)
    }

    private static func describe(_ error: Error) -> String {
        if let url = error as? URLError {
            switch url.code {
            case .cannotConnectToHost, .cannotFindHost, .timedOut, .networkConnectionLost, .notConnectedToInternet:
                return "Server not reachable"
            case .badServerResponse: return "Server refused the connection (token?)"
            case .cancelled: return "No answer from the server"
            default: break
            }
        }
        return "Disconnected"
    }

    // MARK: Messages in

    /// Applies one server message (internal so tests can drive the state machine directly).
    func handle(_ message: GameProtocol.Incoming) {
        lastReceived = Date()
        switch message {
        case .hello(let hello):
            connection = .connected
            simConnected = hello.simConnected
            remotes = hello.remotes ?? []
            state = hello.state
            if let current = hello.game { game = current }
        case .simStatus(let connected):
            simConnected = connected
            if !connected { state = nil }
        case .state(let state):
            self.state = state
        case .shotResult(let result):
            lastShotResult = result
        case .turn(let turn):
            lastTurn = turn
        case .gameStarted(let game), .scorecard(let game), .gameFinished(let game):
            self.game = game
            scoresRevision += 1
        case .error(let message):
            lastError = message
        case .shotRejected, .pong, .other: // the swing session handles rejected shots
            break
        }
        onMessage?(message)
    }

    // MARK: Messages out

    /// Sends one message; false when not connected.
    @discardableResult
    func send<T: Encodable>(_ message: T) -> Bool {
        guard isConnected, let socket, let data = GameProtocol.encode(message) else { return false }
        socket.send(.string(String(decoding: data, as: UTF8.self))) { [weak self] error in
            guard let error else { return }
            let reason = error.localizedDescription
            Task { @MainActor [weak self] in self?.lastError = reason }
        }
        return true
    }

    func nav(_ key: GameProtocol.NavKey) { send(GameProtocol.Nav(key: key)) }
    func club(_ name: String) { send(GameProtocol.ClubChoice(club: name)) }
    func aim(by degrees: Double) { send(GameProtocol.Aim(delta: degrees)) }
    func aimReset() { send(GameProtocol.Bare.aimReset) }
    func mulligan() { send(GameProtocol.Bare.mulligan) }
    func skip() { send(GameProtocol.Bare.skip) }

    /// Sends a swing's shot to the sim; false (nothing sent) when no sim is listening.
    func sendShot(_ shot: Shot, id: Int) -> Bool {
        simReady && send(SimProtocol.ShotMessage(id: id, shot: shot))
    }
}
