import CoreMotion
import Foundation

/// Produces motion samples relative to an address pose. Samples are delivered on the main queue.
protocol MotionSource: AnyObject {
    var isAvailable: Bool { get }
    func start(_ handler: @escaping (MotionSample) -> Void)
    func stop()
    /// The next reading becomes the address pose; later samples are measured from it.
    func captureAddress()
}

/// The real thing: CoreMotion device motion at 100 Hz.
final class DeviceMotionSource: MotionSource {
    static let hz = 100.0

    private let manager = CMMotionManager()
    private var address: CMAttitude?
    /// The accelerometer's reading at address (gravity, as the phone was held still).
    private var addressDown: CMAcceleration?
    /// Gets every raw reading with its sample (the swing recorder).
    var onRaw: ((CMDeviceMotion, MotionSample) -> Void)?

    var isAvailable: Bool { manager.isDeviceMotionAvailable }

    func start(_ handler: @escaping (MotionSample) -> Void) {
        stop()
        address = nil
        manager.deviceMotionUpdateInterval = 1 / Self.hz
        manager.startDeviceMotionUpdates(using: .xArbitraryZVertical, to: .main) { [weak self] motion, _ in
            guard let self, let motion else { return }
            if address == nil {
                address = motion.attitude.copy() as? CMAttitude
                addressDown = Self.down(motion)
            }
            guard let address, let addressDown else { return }
            let sample = Self.sample(motion, relativeTo: address, down: addressDown)
            onRaw?(motion, sample)
            handler(sample)
        }
    }

    func stop() {
        manager.stopDeviceMotionUpdates()
    }

    func captureAddress() {
        address = nil
    }

    /// What the accelerometer reads: gravity plus the user's acceleration (the raw, drift-free signal).
    static func down(_ motion: CMDeviceMotion) -> CMAcceleration {
        let g = motion.gravity, a = motion.userAcceleration
        return CMAcceleration(x: g.x + a.x, y: g.y + a.y, z: g.z + a.z)
    }

    /// Degrees between two accelerometer readings.
    static func degrees(between a: CMAcceleration, _ b: CMAcceleration) -> Double {
        let dot = a.x * b.x + a.y * b.y + a.z * b.z
        let lengths = (a.x * a.x + a.y * a.y + a.z * a.z).squareRoot() * (b.x * b.x + b.y * b.y + b.z * b.z).squareRoot()
        guard lengths > 0 else { return 0 }
        return acos(max(-1, min(1, dot / lengths))) * 180 / .pi
    }

    static func sample(_ motion: CMDeviceMotion, relativeTo address: CMAttitude, down addressDown: CMAcceleration) -> MotionSample {
        let r = motion.rotationRate
        let relative = motion.attitude.copy() as! CMAttitude
        relative.multiply(byInverseOf: address)
        let angle = 2 * acos(min(1, abs(relative.quaternion.w)))
        return MotionSample(
            time: motion.timestamp,
            rate: (r.x * r.x + r.y * r.y + r.z * r.z).squareRoot(),
            angle: angle * 180 / .pi,
            face: relative.yaw * 180 / .pi,
            tilt: degrees(between: down(motion), addressDown)
        )
    }
}

#if DEBUG
/// Simulator stand-in: streams "still at address" at 100 Hz and plays queued synthetic swings
/// through the same pipeline as the real sensors.
final class SimulatedMotionSource: MotionSource {
    private var timer: Timer?
    private var queue: [MotionSample] = []
    private var clock = 0.0
    private var handler: ((MotionSample) -> Void)?

    var isAvailable: Bool { true }

    func start(_ handler: @escaping (MotionSample) -> Void) {
        stop()
        self.handler = handler
        timer = Timer.scheduledTimer(withTimeInterval: 1 / DeviceMotionSource.hz, repeats: true) { [weak self] _ in
            MainActor.assumeIsolated { self?.tick() }
        }
    }

    func stop() {
        timer?.invalidate()
        timer = nil
        queue.removeAll()
    }

    func captureAddress() {}

    /// Queues a swing, after holding still at address for `lead` seconds (time for the address pose to be set).
    func play(_ spec: SyntheticSwing.Spec, after lead: Double = 0) {
        queue += SyntheticSwing.still(seconds: lead, start: clock)
        queue += SyntheticSwing.samples(spec, start: clock + lead, noise: 0.05, seed: UInt64(clock * 1000))
    }

    private func tick() {
        clock += 1 / DeviceMotionSource.hz
        var sample = queue.isEmpty ? MotionSample(time: clock, rate: 0.02, angle: 0.1, face: 0) : queue.removeFirst()
        sample.time = clock
        handler?(sample)
    }
}
#endif
