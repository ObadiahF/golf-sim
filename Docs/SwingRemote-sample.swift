// SwingRemote: swing your iPhone like a club, send shots to the Unity sim over UDP.
// (Original sample from the user; reference for the real implementation.)
// Info.plist: add NSLocalNetworkUsageDescription ("Sends shots to the golf sim").
// Assumes good contact: speed and face angle come from the phone, the rest from the club table.

import SwiftUI
import CoreMotion
import Network

struct Club {
    let name: String
    let radius: Double    // m, swing radius from hands-pivot to clubhead
    let smash: Double     // ball speed / clubhead speed
    let launch: Double    // deg
    let backspin: Double  // rpm
    let startW: Double    // rad/s that counts as a swing starting
}

let clubs = [
    Club(name: "Driver", radius: 1.65, smash: 1.48, launch: 12, backspin: 2600, startW: 6),
    Club(name: "7 iron", radius: 1.45, smash: 1.33, launch: 17, backspin: 6500, startW: 6),
    Club(name: "Wedge",  radius: 1.35, smash: 1.20, launch: 28, backspin: 8500, startW: 5),
    Club(name: "Putter", radius: 0.90, smash: 1.60, launch: 1,  backspin: 0,    startW: 1.2),
]

final class SwingEngine: ObservableObject {
    @Published var host = "172.20.10.2"
    @Published var clubIndex = 0
    @Published var scale = 1.0          // raise for safe half swings
    @Published var status = "Enter your PC's IP, then tap Address"
    @Published var lastShot = ""

    let faceSign = -1.0                 // flip if fades and draws come out backwards
    private let motion = CMMotionManager()
    private var conn: NWConnection?
    private var address: CMAttitude?
    private var swinging = false
    private var samples: [(w: Double, angle: Double, face: Double)] = []

    func setAddress() {
        conn?.cancel()
        let c = NWConnection(host: NWEndpoint.Host(host), port: 4242, using: .udp)
        c.start(queue: .global())
        conn = c

        guard motion.isDeviceMotionAvailable else { status = "No motion sensors"; return }
        motion.stopDeviceMotionUpdates()
        address = nil
        swinging = false
        motion.deviceMotionUpdateInterval = 1.0 / 100.0
        motion.startDeviceMotionUpdates(using: .xArbitraryZVertical, to: .main) { [weak self] m, _ in
            guard let self, let m else { return }
            if self.address == nil {
                self.address = m.attitude.copy() as? CMAttitude
                self.status = "Address set. Swing!"
            }
            self.process(m)
        }
    }

    private func process(_ m: CMDeviceMotion) {
        guard let address else { return }
        let club = clubs[clubIndex]
        let r = m.rotationRate
        let w = sqrt(r.x * r.x + r.y * r.y + r.z * r.z)

        let rel = m.attitude.copy() as! CMAttitude
        rel.multiply(byInverseOf: address)
        let angle = 2 * acos(min(1, abs(rel.quaternion.w)))   // how far from address pose
        let face = rel.yaw * 180 / .pi

        if !swinging {
            if w > club.startW { swinging = true; samples = [] }
            else { return }
        }
        samples.append((w, angle, face))

        let peak = samples.map(\.w).max() ?? 0
        if peak > club.startW * 1.6 && w < 0.3 * peak {
            fire(club)
            swinging = false
        } else if samples.count > 300 {                       // 3 s with no real swing
            swinging = false
        }
    }

    private func fire(_ club: Club) {
        let peak = samples.map(\.w).max() ?? 0
        // Impact = the fast part of the swing where the phone is closest to its address pose.
        // Backswing samples are too slow to qualify, so this lands on the downswing.
        guard let hit = samples.filter({ $0.w >= 0.6 * peak }).min(by: { $0.angle < $1.angle }) else { return }

        let head = hit.w * club.radius * scale
        let face = max(-15, min(15, hit.face * faceSign))
        let shot: [String: Double] = [
            "speed": head * club.smash,
            "launch": club.launch,
            "azimuth": face * 0.75,
            "back": club.backspin,
            "side": club.backspin == 0 ? 0 : face * 180,
        ]
        if let data = try? JSONSerialization.data(withJSONObject: shot) {
            conn?.send(content: data, completion: .contentProcessed { _ in })
        }
        let mph = head * club.smash * 2.237
        lastShot = String(format: "%@  %.0f mph ball, face %+.1f°", club.name, mph, face)
        status = "Sent. Return to address and swing again"
    }
}

struct ContentView: View {
    @StateObject private var e = SwingEngine()

    var body: some View {
        VStack(spacing: 18) {
            TextField("PC IP address", text: $e.host)
                .textFieldStyle(.roundedBorder)
                .keyboardType(.decimalPad)
            Picker("Club", selection: $e.clubIndex) {
                ForEach(clubs.indices, id: \.self) { Text(clubs[$0].name) }
            }
            .pickerStyle(.segmented)
            VStack(alignment: .leading) {
                Text("Swing scale \(e.scale, specifier: "%.1f")x")
                Slider(value: $e.scale, in: 1...2.5, step: 0.1)
            }
            Button("Address ball") { e.setAddress() }
                .buttonStyle(.borderedProminent)
            Text(e.status)
            Text(e.lastShot).font(.caption.monospaced())
        }
        .padding()
    }
}

@main
struct SwingRemoteApp: App {
    var body: some Scene { WindowGroup { ContentView() } }
}
