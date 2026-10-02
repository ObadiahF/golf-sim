import Foundation

/// Generates motion-sample streams that look like a real swing, for unit tests and the
/// simulator's "Simulate swing" button (CoreMotion isn't available in the simulator).
///
/// The club moves along one signed angle p(t): negative is the backswing side, positive the
/// follow-through. Backswing eases out to the top, the downswing accelerates into address so the
/// fastest moment is impact, the follow-through decelerates from that speed, then (optionally)
/// the phone is brought slowly back to address and held still.
nonisolated enum SyntheticSwing {
    struct Spec: Sendable {
        /// Rotation rate at impact, rad/s.
        var impactRate: Double
        /// Backswing length, degrees from address.
        var top: Double
        /// Face angle at impact, degrees.
        var face: Double = 0
        var addressHold = 0.3
        var backswing = 0.9
        var pause = 0.1
        var followThrough = 0.2
        var finishHold = 0.5
        /// Seconds to walk the phone back to address afterwards; nil stays at the finish.
        var returnTime: Double? = 1.2
        /// Seconds held still at address at the end.
        var settle = 0.8

        static func full(impactRate: Double = 22, face: Double = 0) -> Spec {
            Spec(impactRate: impactRate, top: 150, face: face)
        }

        static func putt(impactRate: Double = 1.4, face: Double = 0) -> Spec {
            Spec(impactRate: impactRate, top: 8, face: face, backswing: 0.6, pause: 0.05, followThrough: 0.25, finishHold: 0.3)
        }
    }

    /// Samples at `hz`, starting at `start` seconds. `noise` adds deterministic jitter (rad/s; a
    /// quarter of it in degrees on the angles).
    static func samples(_ spec: Spec, start: Double = 0, hz: Double = 100, noise: Double = 0, seed: UInt64 = 1) -> [MotionSample] {
        let profile = Profile(spec)
        let dt = 1 / hz
        let count = Int((profile.duration * hz).rounded())
        var rng = SeededRandom(seed: seed)
        var samples: [MotionSample] = []
        samples.reserveCapacity(count)
        var previous = profile.position(at: 0)
        for i in 0..<count {
            let t = Double(i) * dt
            let p = profile.position(at: t)
            let rate = abs(p - previous) / dt
            previous = p
            let reach = max(spec.top, profile.finish * 180 / .pi)
            let face = spec.face * (1 - min(1, abs(p) * 180 / .pi / reach))
            samples.append(MotionSample(
                time: start + t,
                rate: max(0, rate + rng.jitter(noise)),
                angle: max(0, abs(p) * 180 / .pi + rng.jitter(noise / 4)),
                face: face + rng.jitter(noise / 4)
            ))
        }
        return samples
    }

    /// Still at address for `seconds`.
    static func still(seconds: Double, start: Double = 0, hz: Double = 100) -> [MotionSample] {
        (0..<Int(seconds * hz)).map { MotionSample(time: start + Double($0) / hz, rate: 0, angle: 0, face: 0) }
    }

    /// The signed swing angle (radians) as a function of time.
    private struct Profile {
        let spec: Spec
        let top: Double
        let downswing: Double
        let finish: Double
        let duration: Double
        private let marks: [Double]

        init(_ spec: Spec) {
            self.spec = spec
            top = spec.top * .pi / 180
            // p = -top (1 - s^2) reaches address at speed 2 top / T: solve for T.
            downswing = 2 * top / spec.impactRate
            // p = finish sin(pi s / 2) leaves address at finish * pi / (2 T): match the impact speed.
            finish = spec.impactRate * spec.followThrough * 2 / .pi
            var t = 0.0
            var marks: [Double] = []
            let returning: Double = spec.returnTime ?? 0
            let settling: Double = spec.returnTime == nil ? 0 : spec.settle
            let lengths: [Double] = [spec.addressHold, spec.backswing, spec.pause, downswing, spec.followThrough, spec.finishHold, returning, settling]
            for length in lengths {
                t += length
                marks.append(t)
            }
            self.marks = marks
            duration = t
        }

        func position(at t: Double) -> Double {
            func s(_ i: Int) -> Double {
                let begin = i == 0 ? 0 : marks[i - 1]
                let length = marks[i] - begin
                return length > 0 ? min(1, max(0, (t - begin) / length)) : 1
            }
            func smooth(_ x: Double) -> Double { x * x * (3 - 2 * x) }
            switch t {
            case ..<marks[0]: return 0
            case ..<marks[1]: return -top * smooth(s(1))
            case ..<marks[2]: return -top
            case ..<marks[3]: let x = s(3); return -top * (1 - x * x)
            case ..<marks[4]: return finish * sin(.pi / 2 * s(4))
            case ..<marks[5]: return finish
            case ..<marks[6]: return finish * (1 - smooth(s(6)))
            default: return spec.returnTime == nil ? finish : 0
            }
        }
    }
}

/// Small deterministic PRNG (SplitMix64) so synthetic noise is reproducible.
nonisolated struct SeededRandom {
    private var state: UInt64

    init(seed: UInt64) { state = seed }

    mutating func next() -> Double {
        state &+= 0x9E37_79B9_7F4A_7C15
        var z = state
        z = (z ^ (z >> 30)) &* 0xBF58_476D_1CE4_E5B9
        z = (z ^ (z >> 27)) &* 0x94D0_49BB_1331_11EB
        z ^= z >> 31
        return Double(z >> 11) / Double(1 << 53)
    }

    /// Uniform in [-amplitude, amplitude].
    mutating func jitter(_ amplitude: Double) -> Double {
        amplitude == 0 ? 0 : (next() * 2 - 1) * amplitude
    }
}
