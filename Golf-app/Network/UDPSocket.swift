import Darwin
import Foundation

/// A small IPv4 UDP socket (BSD sockets). One socket is used for everything: discovery sweeps to
/// many hosts, shots, and the replies, which arrive on `onReceive` from a private queue.
///
/// BSD sockets rather than NWConnection because discovery sends to every host on the subnet from
/// one port and must hear replies from whichever one answers.
nonisolated final class UDPSocket: @unchecked Sendable {
    typealias Handler = @Sendable (_ data: Data, _ fromIP: String, _ fromPort: UInt16) -> Void

    private let fd: Int32
    private let source: DispatchSourceRead
    private let lock = NSLock()
    private var closed = false

    /// Binds to `port` on all interfaces (0 = any free port).
    init(port: UInt16 = 0, onReceive: @escaping Handler) throws {
        let fd = socket(AF_INET, SOCK_DGRAM, IPPROTO_UDP)
        guard fd >= 0 else { throw POSIXError(.init(rawValue: errno) ?? .EIO) }
        var local = Self.address(port: port)
        local.sin_addr.s_addr = INADDR_ANY
        let bound = withUnsafePointer(to: &local) {
            $0.withMemoryRebound(to: sockaddr.self, capacity: 1) { bind(fd, $0, socklen_t(MemoryLayout<sockaddr_in>.size)) }
        }
        guard bound == 0 else {
            let code = errno
            Darwin.close(fd)
            throw POSIXError(.init(rawValue: code) ?? .EIO)
        }
        _ = fcntl(fd, F_SETFL, fcntl(fd, F_GETFL) | O_NONBLOCK)
        self.fd = fd
        source = DispatchSource.makeReadSource(fileDescriptor: fd, queue: DispatchQueue(label: "SwingRemote.udp"))
        source.setEventHandler { Self.drain(fd, onReceive) }
        source.setCancelHandler { Darwin.close(fd) }
        source.resume()
    }

    deinit { close() }

    func close() {
        lock.lock()
        defer { lock.unlock() }
        guard !closed else { return }
        closed = true
        source.cancel()
    }

    /// Sends to a dotted IPv4 address. Returns false if the address is invalid or the send failed.
    @discardableResult
    func send(_ data: Data, toIP ip: String, port: UInt16) -> Bool {
        lock.lock()
        defer { lock.unlock() }
        guard !closed else { return false }
        var remote = Self.address(port: port)
        guard inet_pton(AF_INET, ip, &remote.sin_addr) == 1 else { return false }
        let sent = data.withUnsafeBytes { bytes in
            withUnsafePointer(to: &remote) {
                $0.withMemoryRebound(to: sockaddr.self, capacity: 1) {
                    sendto(fd, bytes.baseAddress, bytes.count, 0, $0, socklen_t(MemoryLayout<sockaddr_in>.size))
                }
            }
        }
        return sent == data.count
    }

    /// Resolves a host name or dotted address to a dotted IPv4 address. Blocking: call off the main thread.
    static func resolveIPv4(_ host: String) -> String? {
        var probe = in_addr()
        if inet_pton(AF_INET, host, &probe) == 1 { return host }
        var hints = addrinfo()
        hints.ai_family = AF_INET
        hints.ai_socktype = SOCK_DGRAM
        var result: UnsafeMutablePointer<addrinfo>?
        guard getaddrinfo(host, nil, &hints, &result) == 0, let info = result else { return nil }
        defer { freeaddrinfo(result) }
        guard let addr = info.pointee.ai_addr else { return nil }
        return addr.withMemoryRebound(to: sockaddr_in.self, capacity: 1) { ipString($0.pointee.sin_addr) }
    }

    static func ipString(_ addr: in_addr) -> String {
        var addr = addr
        var buffer = [CChar](repeating: 0, count: Int(INET_ADDRSTRLEN))
        inet_ntop(AF_INET, &addr, &buffer, socklen_t(INET_ADDRSTRLEN))
        return String(cString: buffer)
    }

    private static func address(port: UInt16) -> sockaddr_in {
        var addr = sockaddr_in()
        addr.sin_len = UInt8(MemoryLayout<sockaddr_in>.size)
        addr.sin_family = sa_family_t(AF_INET)
        addr.sin_port = port.bigEndian
        return addr
    }

    private static func drain(_ fd: Int32, _ handler: Handler) {
        var buffer = [UInt8](repeating: 0, count: 4096)
        while true {
            var from = sockaddr_in()
            var length = socklen_t(MemoryLayout<sockaddr_in>.size)
            let count = withUnsafeMutablePointer(to: &from) {
                $0.withMemoryRebound(to: sockaddr.self, capacity: 1) { recvfrom(fd, &buffer, buffer.count, 0, $0, &length) }
            }
            guard count > 0 else { return }
            handler(Data(buffer[0..<count]), ipString(from.sin_addr), UInt16(bigEndian: from.sin_port))
        }
    }
}
