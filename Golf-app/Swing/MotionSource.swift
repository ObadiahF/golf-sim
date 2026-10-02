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

    var isAvailable: Bool { manager.isDeviceMotionAvailable }

    func start(_ handler: @escaping (MotionSample) -> Void) {
        stop()
        address = nil
        manager.deviceMotionUpdateInterval = 1 / Self.hz
        manager.startDeviceMotionUpdates(using: .xArbitraryZVertical, to: .main) { [weak self] motion, _ in
            guard let self, let motion else { return }
            if address == nil { address = motion.attitude.copy() as? CMAttitude }
            guard let address else { return }
            handler(Self.sample(motion, relativeTo: address))
        }
    }

    func stop() {
        manager.stopDeviceMotionUpdates()
    }

    func captureAddress() {
        address = nil
    }

    static func sample(_ motion: CMDeviceMotion, relativeTo address: CMAttitude) -> MotionSample {
        let r = motion.rotationRate
        let relative = motion.attitude.copy() as! CMAttitude
        relative.multiply(byInverseOf: address)
        let angle = 2 * acos(min(1, abs(relative.quaternion.w)))
        return MotionSample(
            time: motion.timestamp,
            rate: (r.x * r.x + r.y * r.y + r.z * r.z).squareRoot(),
            angle: angle * 180 / .pi,
            face: relative.yaw * 180 / .pi
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

    func play(_ spec: SyntheticSwing.Spec) {
        queue += SyntheticSwing.samples(spec, start: clock, noise: 0.05, seed: UInt64(clock * 1000))
    }

    private func tick() {
        clock += 1 / DeviceMotionSource.hz
        var sample = queue.isEmpty ? MotionSample(time: clock, rate: 0.02, angle: 0.1, face: 0) : queue.removeFirst()
        sample.time = clock
        handler?(sample)
    }
}
#endif
