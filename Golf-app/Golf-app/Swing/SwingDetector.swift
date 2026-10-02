import Foundation

/// One motion reading, relative to the address pose.
nonisolated struct MotionSample: Equatable, Sendable {
    /// Seconds, monotonic.
    var time: Double
    /// Rotation rate magnitude, rad/s.
    var rate: Double
    /// Total rotation away from the address pose, degrees.
    var angle: Double
    /// Yaw relative to address, degrees (the face angle at impact).
    var face: Double
    /// Degrees between gravity now and at address, straight from the accelerometer, so it never drifts the way
    /// `angle` can after a fast swing saturates the gyro. Only meaningful while the phone is still. Nil in
    /// synthetic streams, where `angle` stands in.
    var tilt: Double? = nil
}

/// Finds swings in a stream of motion samples. Pure value type: no CoreMotion, no clock,
/// so it can be driven by synthetic streams in tests and the simulator.
///
/// A swing must start fast (debounced), take the phone away from address (the backswing), come at least halfway
/// back before its fastest moment (the downswing), and slow down again (the finish). Impact is the fastest
/// sample. After a shot it ignores motion for a cooldown and re-arms once the phone is held still back near
/// address, judged by tilt: a full-speed swing can saturate the gyro, and then `angle` drifts.
nonisolated struct SwingDetector: Sendable {
    enum Phase: Equatable, Sendable {
        /// At address, waiting for a swing to start.
        case armed
        case swinging(since: Double)
        /// Just fired; ignoring everything until this time.
        case cooldown(until: Double)
        /// Waiting for the phone to come back to address and be held still.
        case waitingForReturn(stillSince: Double?)
    }

    enum Event: Equatable, Sendable {
        case started
        case fired(Impact)
        /// The motion was not a swing (waggle, half backswing, timeout).
        case aborted
        /// Back at address and ready for the next swing.
        case rearmed
    }

    var thresholds: SwingThresholds
    /// Consecutive fast samples needed to start a swing (debounce against single spikes).
    var startDebounce = 3
    /// Seconds of history kept from before the start, so a slow backswing still counts.
    var preRoll = 1.5
    /// A swing that hasn't finished after this many seconds is dropped.
    var maxSwing = 3.0
    /// Seconds after a shot during which all motion is ignored (the finish, the follow-through wobble).
    var cooldown = 0.8
    /// Seconds the phone must be held still near address to re-arm.
    var holdToRearm = 0.4
    /// A started swing that never went away from address ends after this long held still.
    var waggleTimeout = 0.35
    /// Finish detection: the rate must fall below this fraction of the swing's peak.
    var finishFraction = 0.3

    private(set) var phase: Phase = .armed
    private var history: [MotionSample] = []
    private var swing: [MotionSample] = []
    private var fastCount = 0
    private var stillSince: Double?

    init(thresholds: SwingThresholds) {
        self.thresholds = thresholds
    }

    /// Back to armed with no history (call when the address pose is (re)captured).
    mutating func reset() {
        phase = .armed
        history.removeAll(keepingCapacity: true)
        swing.removeAll(keepingCapacity: true)
        fastCount = 0
        stillSince = nil
    }

    /// Feeds one sample; returns an event when the state changes in a way the UI cares about.
    mutating func process(_ s: MotionSample) -> Event? {
        switch phase {
        case .armed:
            return processArmed(s)
        case .swinging(let since):
            return processSwinging(s, since: since)
        case .cooldown(let until):
            guard s.time >= until else { return nil }
            phase = .waitingForReturn(stillSince: nil)
            return processReturn(s, stillSince: nil)
        case .waitingForReturn(let since):
            return processReturn(s, stillSince: since)
        }
    }

    private mutating func processArmed(_ s: MotionSample) -> Event? {
        history.append(s)
        if let first = history.first, s.time - first.time > preRoll { history.removeFirst() }
        fastCount = s.rate > thresholds.startRate ? fastCount + 1 : 0
        guard fastCount >= startDebounce else { return nil }
        swing = history
        stillSince = nil
        phase = .swinging(since: s.time)
        return .started
    }

    private mutating func processSwinging(_ s: MotionSample, since: Double) -> Event? {
        swing.append(s)
        if let impact = finishedImpact(current: s) {
            phase = .cooldown(until: s.time + cooldown)
            return .fired(impact)
        }
        if s.time - since > maxSwing { return abort() }
        if !wentAway, s.rate < thresholds.stillRate {
            let start = stillSince ?? s.time
            stillSince = start
            if s.time - start >= waggleTimeout { return abort() }
        } else {
            stillSince = nil
        }
        return nil
    }

    private mutating func processReturn(_ s: MotionSample, stillSince since: Double?) -> Event? {
        let settled = (s.tilt ?? s.angle) <= thresholds.rearmAngle && s.rate <= thresholds.stillRate
        guard settled else {
            phase = .waitingForReturn(stillSince: nil)
            return nil
        }
        let start = since ?? s.time
        guard s.time - start >= holdToRearm else {
            phase = .waitingForReturn(stillSince: start)
            return nil
        }
        reset()
        return .rearmed
    }

    private mutating func abort() -> Event {
        swing.removeAll(keepingCapacity: true)
        history.removeAll(keepingCapacity: true)
        fastCount = 0
        phase = .waitingForReturn(stillSince: nil)
        return .aborted
    }

    private var wentAway: Bool { swing.contains { $0.angle >= thresholds.awayAngle } }

    /// The impact (the fastest sample), if the swing went away, came at least halfway back from the top before
    /// that fastest moment (so a quick takeaway isn't a swing), and is now finishing. No check on how close to
    /// address impact is: after the gyro saturates, `angle` near impact can't be trusted.
    private func finishedImpact(current s: MotionSample) -> Impact? {
        guard let away = swing.firstIndex(where: { $0.angle >= thresholds.awayAngle }),
              let peak = swing[away...].indices.max(by: { swing[$0].rate < swing[$1].rate })
        else { return nil }
        let hit = swing[peak]
        guard hit.rate >= thresholds.startRate * thresholds.peakFactor, s.rate < finishFraction * hit.rate,
              let top = swing[away...peak].indices.max(by: { swing[$0].angle < swing[$1].angle }),
              swing[top...peak].contains(where: { $0.angle <= swing[top].angle / 2 })
        else { return nil }
        return Impact(rate: hit.rate, face: hit.face, angle: hit.angle, time: hit.time)
    }
}
