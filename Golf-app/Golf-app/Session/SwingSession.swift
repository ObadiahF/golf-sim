import Foundation
import Observation

/// Ties it together: motion samples -> swing detector -> shot -> sim link, plus the address
/// routine and what the UI shows.
@Observable
final class SwingSession {
    enum Stage: Equatable {
        case idle
        /// Address tapped: waiting a moment, then for the phone to be held still.
        case settling
        case ready
        case swinging
        /// Shot sent (or swing aborted): waiting for the phone to come back to address.
        case returning
        case unavailable(String)
    }

    /// Seconds after tapping Address before the pose can be captured (time to take your grip).
    static let settleDelay = 1.2
    /// Seconds the phone must be held still to capture the address pose.
    static let settleHold = 0.5
    static let settleRate = 0.5

    let settings: AppSettings
    let link: SimLink
    /// The game server; shots go through it when a sim is connected there, else over UDP.
    let game: GameLink

    private(set) var stage: Stage = .idle {
        didSet { if stage != oldValue { recorder.mark("stage \(stage)") } }
    }
    private(set) var lastShot: Shot?
    private(set) var lastShotID: Int?
    private(set) var delivery: Delivery?
    private(set) var result: SimResult?
    /// The putting power meter (filled live while putting).
    private(set) var meter = PuttMeter()
    /// Raw swing data for tuning detection (Settings > Record swings).
    let recorder = SwingRecorder()

    @ObservationIgnored private let motion: MotionSource
    @ObservationIgnored private var detector: SwingDetector
    @ObservationIgnored private var settleStart: Double?
    @ObservationIgnored private var stillSince: Double?
    /// The club the sim last reported, so a repeat of the same state doesn't undo the player's pick.
    @ObservationIgnored private var mirroredClub: String?

    var club: Club { settings.club }

    init(settings: AppSettings = AppSettings(), motion: MotionSource? = nil) {
        self.settings = settings
        link = SimLink(settings: settings)
        game = GameLink(settings: settings)
        self.motion = motion ?? Self.defaultMotionSource()
        detector = SwingDetector(thresholds: settings.detection(for: settings.club))
        if let device = self.motion as? DeviceMotionSource {
            device.onRaw = { [recorder] motion, sample in recorder.add(motion, sample) }
        }
        link.onDelivery = { [weak self] id, delivery in self?.update(id: id, delivery: delivery) }
        link.onResult = { [weak self] result in
            guard let self, result.id == lastShotID else { return }
            self.result = result
        }
        game.onMessage = { [weak self] message in self?.apply(message) }
    }

    private static func defaultMotionSource() -> MotionSource {
        let device = DeviceMotionSource()
        #if DEBUG
        if !device.isAvailable { return SimulatedMotionSource() }
        #endif
        return device
    }

    // MARK: Controls

    var isActive: Bool { stage != .idle && !isUnavailable }
    private var isUnavailable: Bool { if case .unavailable = stage { true } else { false } }

    /// Starts (or restarts) the address routine: take your grip, hold still, then swing.
    func address() {
        guard motion.isAvailable else {
            stage = .unavailable("This device has no motion sensors")
            return
        }
        if stage == .idle || isUnavailable { motion.start { [weak self] in self?.handle($0) } }
        meter.reset() // a new putt starts from empty
        detector.thresholds = settings.detection(for: club) // picks up a sensitivity changed in Settings
        settleStart = nil
        stillSince = nil
        stage = .settling
        Haptics.tick()
    }

    func stop() {
        motion.stop()
        stage = .idle
    }

    /// The player picked a club: use it here and tell the sim.
    func selectClub(_ index: Int) {
        guard index != settings.clubIndex else { return }
        useClub(index)
        game.club(club.name)
        Haptics.tick()
    }

    private func useClub(_ index: Int) {
        settings.clubIndex = index
        detector.thresholds = settings.detection(for: settings.club)
    }

    /// Starts the swing services (UDP discovery and the game server).
    func connect() {
        link.start()
        game.start()
    }

    func disconnect() {
        stop()
        link.stop()
        game.stop()
    }

    #if DEBUG
    var canSimulate: Bool { motion is SimulatedMotionSource }

    /// Feeds a synthetic swing for the current club through the real pipeline (simulator only). Not addressed
    /// yet: addresses first and holds still long enough for the pose to be set, so one tap swings.
    func simulateSwing() {
        guard let simulated = motion as? SimulatedMotionSource else { return }
        let addressing = stage == .idle || stage == .settling || isUnavailable
        if stage != .settling && addressing { address() }
        let face = Double.random(in: -6...6)
        let spec: SyntheticSwing.Spec = club.isPutter ? .putt(impactRate: .random(in: 0.8...2.4), face: face) : .full(impactRate: .random(in: 16...26), face: face)
        simulated.play(spec, after: addressing ? Self.addressTime : 0)
    }

    /// Seconds from tapping Address until the pose is set when the phone is held still, with a little to spare.
    static var addressTime: Double { settleDelay + settleHold + 0.3 }
    #endif

    // MARK: Pipeline

    private func handle(_ sample: MotionSample) {
        if stage == .settling { return settle(sample) }
        let event = detector.process(sample)
        if club.isPutter { trackPutt(sample, event: event) }
        guard let event else { return }
        recorder.mark("\(event) club=\(club.short)", time: sample.time)
        switch event {
        case .started: stage = .swinging
        case .fired(let impact): fire(impact)
        case .aborted: stage = .returning
        case .cancelled:
            meter.reset()
            stage = .ready // still at address: no buzz, it's already the pose the last one set
        case .rearmed:
            motion.captureAddress() // a fresh pose: a fast swing can leave the old one drifted
            stage = .ready
            Haptics.addressSet() // the buzz that says "swing again"
        }
    }

    /// Captures the address pose once the delay has passed and the phone has been held still.
    private func settle(_ sample: MotionSample) {
        let start = settleStart ?? sample.time
        settleStart = start
        guard sample.time - start >= Self.settleDelay, sample.rate < Self.settleRate else {
            stillSince = nil
            return
        }
        let since = stillSince ?? sample.time
        stillSince = since
        guard sample.time - since >= Self.settleHold else { return }
        motion.captureAddress()
        detector.reset()
        stage = .ready
        Haptics.addressSet()
    }

    /// Fills the power meter from the stroke in progress (the backswing counts too: it's what the meter feels).
    private func trackPutt(_ sample: MotionSample, event: SwingDetector.Event?) {
        if event == .started { meter.begin() }
        guard event == .started || stage == .swinging else { return }
        meter.track(distance: PuttModel.rollDistance(rate: sample.rate, scale: settings.scale(for: club), stimp: stimp, club: club))
    }

    private var stimp: Double { PuttModel.stimp(of: game.state) }

    private func fire(_ impact: Impact) {
        stage = .returning
        if let wait = game.shotWait { // the sim can't take a swing now: send nothing, say why
            meter.cancel()
            delivery = .rejected(wait)
            Haptics.problem()
            return
        }
        let shot = Shot.from(impact, club: club, scale: settings.scale(for: club), faceSign: settings.faceFactor)
        if club.isPutter {
            meter.strike(distance: PuttModel.rollDistance(ballSpeed: shot.ballSpeed, stimp: stimp), toHole: game.state.flatMap { $0.isPutting ? $0.puttDistance : nil })
        }
        lastShot = shot
        result = nil
        Haptics.shotSent()
        let id = link.makeShotID()
        if game.sendShot(shot, id: id) {
            lastShotID = id
            update(id: id, delivery: .received)
        } else {
            lastShotID = link.send(shot)
        }
    }

    /// Follows the sim: its suggested club, and the result of our last WebSocket shot.
    private func apply(_ message: GameProtocol.Incoming) {
        switch message {
        case .state(let state): follow(state)
        case .hello(let hello): follow(hello.state)
        case .turn: meter.reset()
        case .shotResult(let shot):
            meter.finish(shot)
            guard let id = lastShotID, lastShot != nil, result == nil else { return }
            result = SimResult(id: id, shot)
        case .shotRejected(let rejection):
            reject(rejection)
        default:
            break
        }
    }

    /// The sim didn't hit our last shot: say why and stop waiting for its result. Without an id it is ours only
    /// while we are waiting for a result (another phone may have swung).
    private func reject(_ rejection: GameProtocol.ShotRejected) {
        guard let id = lastShotID, lastShot != nil, rejection.id.map({ $0 == id }) ?? (result == nil) else { return }
        meter.cancel()
        update(id: id, delivery: .rejected(rejection.reason))
    }

    private func update(id: Int, delivery: Delivery) {
        // Ids only grow; a late reply for an older shot is stale. (`send` reports .sending before it returns the id.)
        guard id >= (lastShotID ?? .min) else { return }
        lastShotID = id
        self.delivery = delivery
        switch delivery {
        case .received: Haptics.received()
        case .busy, .rejected, .noReply: Haptics.problem()
        case .sending: break
        }
    }

    private func follow(_ state: GameProtocol.SimState?) {
        meter.follow(state)
        state.map(mirrorClub)
    }

    private func mirrorClub(of state: GameProtocol.SimState) {
        guard let name = state.club, name != mirroredClub else { return }
        mirroredClub = name
        if let index = Club.bag.firstIndex(where: { $0.name == name }), index != settings.clubIndex { useClub(index) }
    }

    // MARK: Display

    /// One line telling the golfer what to do next (or why the sim can't take a swing yet).
    var instruction: String {
        if let wait = game.shotWait, stage != .settling, !isUnavailable { return wait }
        return switch stage {
        case .idle: "Tap Address, take your grip and hold still"
        case .settling: "Take your grip… hold still"
        case .ready: club.isPutter ? "Ready. Make your stroke" : "Ready. Swing away"
        case .swinging: "Swinging…"
        case .returning: "Return to address and hold still"
        case .unavailable(let reason): reason
        }
    }
}
