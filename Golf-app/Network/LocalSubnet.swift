import Darwin
import Foundation

/// The phone's local IPv4 networks, and every host address on them, for the discovery sweep.
///
/// iOS needs a special Apple entitlement to send broadcast packets, so discovery instead sends a
/// small unicast "discover" datagram to each address on the Wi-Fi (or hotspot) subnet.
nonisolated enum LocalSubnet {
    struct Interface: Equatable {
        var name: String
        /// Host byte order.
        var address: UInt32
        var netmask: UInt32
    }

    /// Wi-Fi (en*) and Personal Hotspot (bridge*) IPv4 interfaces that are up.
    static func interfaces() -> [Interface] {
        var head: UnsafeMutablePointer<ifaddrs>?
        guard getifaddrs(&head) == 0, let first = head else { return [] }
        defer { freeifaddrs(head) }
        var found: [Interface] = []
        for pointer in sequence(first: first, next: { $0.pointee.ifa_next }) {
            let entry = pointer.pointee
            let name = String(cString: entry.ifa_name)
            guard name.hasPrefix("en") || name.hasPrefix("bridge"),
                  entry.ifa_flags & UInt32(IFF_UP) != 0,
                  let addr = entry.ifa_addr, addr.pointee.sa_family == sa_family_t(AF_INET),
                  let mask = entry.ifa_netmask
            else { continue }
            found.append(Interface(name: name, address: ipv4(addr), netmask: ipv4(mask)))
        }
        return found
    }

    /// Every host on the interfaces' subnets except our own addresses (dotted strings).
    static func sweepTargets(cap: Int = 1024) -> [String] {
        let all = interfaces()
        let own = Set(all.map(\.address))
        var seen = Set<UInt32>()
        return all
            .flatMap { hosts(address: $0.address, netmask: $0.netmask, cap: cap) }
            .filter { !own.contains($0) && seen.insert($0).inserted }
            .map(dotted)
    }

    /// Host addresses on the subnet (no network or broadcast address). Subnets bigger than `cap`
    /// hosts are narrowed to the /24 around `address`.
    static func hosts(address: UInt32, netmask: UInt32, cap: Int) -> [UInt32] {
        var mask = netmask
        if Int(~mask) + 1 > cap { mask = 0xFFFF_FF00 }
        let network = address & mask
        let broadcast = network | ~mask
        guard broadcast > network + 1 else { return [] }
        return Array((network + 1)..<broadcast)
    }

    static func dotted(_ ip: UInt32) -> String {
        "\(ip >> 24 & 0xFF).\(ip >> 16 & 0xFF).\(ip >> 8 & 0xFF).\(ip & 0xFF)"
    }

    private static func ipv4(_ addr: UnsafeMutablePointer<sockaddr>) -> UInt32 {
        addr.withMemoryRebound(to: sockaddr_in.self, capacity: 1) { UInt32(bigEndian: $0.pointee.sin_addr.s_addr) }
    }
}
