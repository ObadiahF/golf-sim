import Foundation
import Observation

/// The phone's connection to the golf sim: finds the PC, keeps a heartbeat, sends shots with
/// retries until acknowledged, and reports acks and results.
@Observable
final class SimLink {
    enum Connection: Equatable {
        case idle
        case searching
        case connected(name: String)
        /// We know the host but it stopped answering.
        case notResponding
        /// Discovery found nobody.
        case notFound
        case failed(String)
    }

    static let discoveryTimeout: Duration = .seconds(2.5)
    static let heartbeatInterval: TimeInterval = 2
    static let heartbeatTimeout: TimeInterval = 6
    static let retryInterval: Duration = .milliseconds(350)
    static let maxAttempts = 3

    private(set) var connection: Connection = .idle
    /// Dotted IPv4 of the sim, once resolved or discovered.
    private(set) var hostIP: String?

    @ObservationIgnored var onDelivery: ((_ id: Int, Delivery) -> Void)?
    @ObservationIgnored var onResult: ((SimResult) -> Void)?

    @ObservationIgnored private let settings: AppSettings
    @ObservationIgnored private let port: UInt16
    @ObservationIgnored private var socket: UDPSocket?
    @ObservationIgnored private var heartbeat: Timer?
    @ObservationIgnored private var lastHello: Date?
    @ObservationIgnored private var nextID = Int.random(in: 1...9_000) * 1_000
    @ObservationIgnored private var pendingID: Int?
    @ObservationIgnored private var retryTask: Task<Void, Never>?
    @ObservationIgnored private var discoveryTask: Task<Void, Never>?

    init(settings: AppSettings, port: UInt16 = SimProtocol.port) {
        self.settings = settings
        self.port = port
    }

    /// Opens the socket and connects to the remembered host, falling back to discovery.
    func start() {
        guard socket == nil else { return }
        do {
            socket = try UDPSocket { [weak self] data, ip, _ in
                guard let self else { return }
                Task { @MainActor in self.receive(data, from: ip) }
            }
        } catch {
            connection = .failed("Network unavailable: \(error.localizedDescription)")
            return
        }
        heartbeat = Timer.scheduledTimer(withTimeInterval: Self.heartbeatInterval, repeats: true) { [weak self] _ in
            MainActor.assumeIsolated { self?.beat() }
        }
        if settings.host.isEmpty { discover() } else { connect(to: settings.host, discoverIfSilent: true) }
    }

    func stop() {
        heartbeat?.invalidate()
        heartbeat = nil
        retryTask?.cancel()
        discoveryTask?.cancel()
        socket?.close()
        socket = nil
        connection = .idle
    }

    /// Uses this host (IP or name) and remembers it. With `discoverIfSilent`, falls back to a
    /// subnet sweep when it doesn't answer.
    func connect(to host: String, discoverIfSilent: Bool = false) {
        let host = host.trimmingCharacters(in: .whitespaces)
        guard !host.isEmpty else { return discover() }
        settings.host = host
        hostIP = nil
        lastHello = nil
        connection = .searching
        discoveryTask?.cancel()
        discoveryTask = Task { [weak self] in
            let ip = await Task.detached { UDPSocket.resolveIPv4(host) }.value
            guard let self, !Task.isCancelled else { return }
            guard let ip else {
                connection = .failed("Can't find \(host)")
                return
            }
            hostIP = ip
            sendDiscover(to: [ip])
            try? await Task.sleep(for: Self.discoveryTimeout)
            guard !Task.isCancelled, connection == .searching else { return }
            if discoverIfSilent { discover() } else { connection = .notResponding }
        }
    }

    /// Finds the sim by sending "discover" to every host on the local subnet; the first to
    /// answer wins and is remembered.
    func discover() {
        connection = .searching
        discoveryTask?.cancel()
        discoveryTask = Task { [weak self] in
            guard let self else { return }
            var targets = LocalSubnet.sweepTargets()
            #if targetEnvironment(simulator)
            targets.insert("127.0.0.1", at: 0) // the simulator shares the Mac's network stack
            #endif
            if let known = hostIP { targets.insert(known, at: 0) }
            // Pace the sweep so the socket buffer doesn't overflow on big subnets.
            for chunk in stride(from: 0, to: targets.count, by: 64) {
                guard !Task.isCancelled, connection == .searching else { return }
                sendDiscover(to: Array(targets[chunk..<min(chunk + 64, targets.count)]))
                try? await Task.sleep(for: .milliseconds(15))
            }
            try? await Task.sleep(for: Self.discoveryTimeout)
            guard !Task.isCancelled, connection == .searching else { return }
            connection = .notFound
        }
    }

    /// A new shot id; ids only grow, for UDP and WebSocket shots alike, so the sim can spot retries.
    func makeShotID() -> Int {
        nextID += 1
        return nextID
    }

    /// Sends a shot and retries until the sim acks it. Returns the shot id.
    @discardableResult
    func send(_ shot: Shot) -> Int {
        let id = makeShotID()
        guard let ip = hostIP, let data = SimProtocol.encode(SimProtocol.ShotMessage(id: id, shot: shot)) else {
            onDelivery?(id, .rejected("No PC connected"))
            return id
        }
        pendingID = id
        onDelivery?(id, .sending)
        retryTask?.cancel()
        retryTask = Task { [weak self] in
            for _ in 0..<Self.maxAttempts {
                guard let self, pendingID == id else { return }
                socket?.send(data, toIP: ip, port: port)
                try? await Task.sleep(for: Self.retryInterval)
                if Task.isCancelled { return }
            }
            guard let self, pendingID == id else { return }
            pendingID = nil
            onDelivery?(id, .noReply)
        }
        return id
    }

    private func sendDiscover(to ips: [String]) {
        guard let socket, let data = SimProtocol.encode(SimProtocol.DiscoverMessage()) else { return }
        for ip in ips { socket.send(data, toIP: ip, port: port) }
    }

    private func beat() {
        guard let ip = hostIP else { return }
        sendDiscover(to: [ip])
        if case .connected = connection, let lastHello, Date().timeIntervalSince(lastHello) > Self.heartbeatTimeout {
            connection = .notResponding
        }
    }

    private func receive(_ data: Data, from ip: String) {
        guard let reply = SimProtocol.decodeReply(data) else { return }
        switch reply.type {
        case "hello":
            // While searching, the first sim to answer becomes the host; otherwise only ours counts.
            if connection == .searching, hostIP != ip {
                hostIP = ip
                settings.host = ip
            }
            guard ip == hostIP else { return }
            discoveryTask?.cancel()
            lastHello = Date()
            connection = .connected(name: reply.name ?? ip)
        case "ack":
            guard let id = reply.id, id == pendingID else { return }
            pendingID = nil
            retryTask?.cancel()
            onDelivery?(id, Self.delivery(for: reply))
        case "result":
            if let result = SimResult(reply) { onResult?(result) }
        default:
            break
        }
    }

    static func delivery(for ack: SimProtocol.Reply) -> Delivery {
        switch ack.status {
        case "ok": .received
        case "busy": .busy
        default: .rejected(ack.message ?? ack.status ?? "Rejected")
        }
    }
}
