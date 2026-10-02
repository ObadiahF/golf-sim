import Foundation
import Testing
@testable import Golf_app

/// Drives the pure SwingDetector with synthetic 100 Hz streams.
struct SwingDetectorTests {
    private func run(_ samples: [MotionSample], _ thresholds: SwingThresholds = .fullSwing) -> [SwingDetector.Event] {
        var detector = SwingDetector(thresholds: thresholds)
        return samples.compactMap { detector.process($0) }
    }

    private func impacts(_ events: [SwingDetector.Event]) -> [Impact] {
        events.compactMap { if case .fired(let impact) = $0 { impact } else { nil } }
    }

    /// Samples along a signed swing angle p(t) in degrees; the rate is its derivative.
    private func path(seconds: Double, start: Double = 0, _ p: (Double) -> Double) -> [MotionSample] {
        let dt = 0.01
        return (0..<Int(seconds / dt)).map { i in
            let t = Double(i) * dt
            let rate = abs(p(t) - p(max(0, t - dt))) / dt * .pi / 180
            return MotionSample(time: start + t, rate: rate, angle: abs(p(t)), face: 0)
        }
    }

    @Test func fullSwingFiresOnceAtImpactSpeed() throws {
        let events = run(SyntheticSwing.samples(.full(impactRate: 22, face: 3)))
        let hits = impacts(events)
        try #require(hits.count == 1)
        #expect(abs(hits[0].rate - 22) / 22 < 0.1)
        #expect(abs(hits[0].face - 3) < 1)
        #expect(hits[0].angle < 20) // the fastest sample: within one 100 Hz sample (13 degrees at 22 rad/s) of address
        #expect(events.first == .started)
        #expect(events.last == .rearmed)
    }

    @Test func noisySwingStillFiresOnce() {
        for seed in 1...20 {
            let samples = SyntheticSwing.samples(.full(impactRate: 18, face: -2), noise: 0.4, seed: UInt64(seed))
            #expect(impacts(run(samples)).count == 1, "seed \(seed)")
        }
    }

    @Test func slowAndFastSwingsScaleSpeed() throws {
        let slow = try #require(impacts(run(SyntheticSwing.samples(.full(impactRate: 12)))).first)
        let fast = try #require(impacts(run(SyntheticSwing.samples(.full(impactRate: 30)))).first)
        #expect(fast.rate > slow.rate * 2)
    }

    @Test func waggleDoesNotFire() {
        // A brisk 20 degree waggle at 3 Hz crosses the start rate but never makes a backswing.
        let waggle = path(seconds: 1) { 20 * sin(2 * .pi * 3 * $0) }
        let events = run(waggle + SyntheticSwing.still(seconds: 1.5, start: 1))
        #expect(impacts(events).isEmpty)
        #expect(events.contains(.started))
        #expect(events.contains(.cancelled)) // never left address: armed again without a return
        #expect(!events.contains(.aborted))
    }

    @Test func backswingOnlyDoesNotFire() {
        // Quick takeaway to the top, pause, then walk slowly back to address.
        let top = path(seconds: 0.4) { t in let s = t / 0.4; return -150 * s * s * (3 - 2 * s) }
        let hold = path(seconds: 0.5, start: 0.4) { _ in -150 }
        let back = path(seconds: 2.5, start: 0.9) { t in -150 * (1 - min(1, t / 2)) }
        #expect(impacts(run(top + hold + back)).isEmpty)
    }

    @Test func singleSpikeIsDebounced() {
        var samples = SyntheticSwing.still(seconds: 1)
        samples[50].rate = 30
        #expect(run(samples).isEmpty)
    }

    @Test func noReTriggerUntilBackAtAddress() {
        var first = SyntheticSwing.Spec.full(impactRate: 20)
        first.returnTime = nil // stays at the finish
        var second = SyntheticSwing.Spec.full(impactRate: 20)
        second.addressHold = 0.2 // not long enough at address to re-arm
        let a = SyntheticSwing.samples(first)
        let b = SyntheticSwing.samples(second, start: a.last!.time + 0.01)
        #expect(impacts(run(a + b)).count == 1)
    }

    @Test func reArmsAfterReturnAndFiresAgain() {
        let a = SyntheticSwing.samples(.full(impactRate: 20, face: 1))
        let b = SyntheticSwing.samples(.full(impactRate: 24, face: -1), start: a.last!.time + 0.01)
        let hits = impacts(run(a + b))
        #expect(hits.count == 2)
        #expect(hits.count == 2 && hits[1].rate > hits[0].rate)
    }

    /// A full-speed swing saturates the gyro: from the fast part of the downswing on, the fused angle is 70
    /// degrees off (never back near address), while the accelerometer's tilt stays true.
    private func saturated(_ spec: SyntheticSwing.Spec, start: Double = 0) -> [MotionSample] {
        var drifted = false
        return SyntheticSwing.samples(spec, start: start).map { sample in
            var s = sample
            s.tilt = sample.angle
            if s.rate > 30 { drifted = true }
            if drifted { s.angle += 70 }
            return s
        }
    }

    @Test func saturatedSwingStillFiresAndReArms() throws {
        let events = run(saturated(.full(impactRate: 34)))
        let hits = impacts(events)
        try #require(hits.count == 1)
        #expect(hits[0].rate > 30)
        #expect(events.last == .rearmed)
    }

    @Test func saturatedSwingsBackToBack() {
        let a = saturated(.full(impactRate: 34))
        let b = SyntheticSwing.samples(.full(impactRate: 25), start: a.last!.time + 0.01)
        #expect(impacts(run(a + b)).count == 2)
    }

    @Test func puttFiresWithPutterThresholds() throws {
        let samples = SyntheticSwing.samples(.putt(impactRate: 1.4, face: -1), noise: 0.03)
        let hits = impacts(run(samples, .putter))
        try #require(hits.count == 1)
        #expect(abs(hits[0].rate - 1.4) / 1.4 < 0.15)
    }

    @Test func shortPuttFires() throws {
        // A ~30 cm tap: about 0.4 rad/s at the phone, 4 degrees back.
        var spec = SyntheticSwing.Spec.putt(impactRate: 0.4)
        spec.top = 4
        let hits = impacts(run(SyntheticSwing.samples(spec, noise: 0.02), .putter))
        try #require(hits.count == 1)
        #expect(abs(hits[0].rate - 0.4) / 0.4 < 0.15)
    }

    @Test func sensitivityLetsSmallerSwingsFire() {
        var spec = SyntheticSwing.Spec.putt(impactRate: 0.28)
        spec.top = 3
        let samples = SyntheticSwing.samples(spec)
        #expect(impacts(run(samples, .putter)).isEmpty)
        #expect(impacts(run(samples, .putter.scaled(sensitivity: 1.5))).count == 1)
        #expect(impacts(run(SyntheticSwing.samples(.full(impactRate: 9)), .fullSwing.scaled(sensitivity: 0.5))).isEmpty)
    }

    @Test func puttIsTooGentleForFullSwingThresholds() {
        #expect(impacts(run(SyntheticSwing.samples(.putt()), .fullSwing)).isEmpty)
    }

    @Test func tremorAtAddressDoesNotFireThePutter() {
        var rng = SeededRandom(seed: 7)
        let tremor = (0..<500).map { i in
            MotionSample(time: Double(i) / 100, rate: 0.15 + rng.jitter(0.1), angle: 0.5 + rng.jitter(0.3), face: 0)
        }
        #expect(run(tremor, .putter).isEmpty)
    }

    @Test func abandonedSwingTimesOut() {
        // Fast start, then hold far from address for a long time: aborts, never fires.
        let away = path(seconds: 0.3) { t in -90 * min(1, t / 0.15) }
        let hold = path(seconds: 4, start: 0.3) { _ in -90 }
        let events = run(away + hold)
        #expect(impacts(events).isEmpty)
        #expect(events.contains(.aborted))
    }
}
