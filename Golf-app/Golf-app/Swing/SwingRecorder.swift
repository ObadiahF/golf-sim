import CoreMotion
import CoreTransferable
import Foundation
import Observation
import UniformTypeIdentifiers

/// Records the raw sensor stream and what the swing detector made of it, for tuning detection with real swings.
/// Turn it on in Settings, practice, then share the CSV. Keeps the last few minutes; off by default.
@Observable
final class SwingRecorder {
    /// Rows kept: five minutes at 100 Hz.
    static let maxRows = 30_000
    static let header = "t,kind,rx,ry,rz,qw,qx,qy,qz,gx,gy,gz,ax,ay,az,rate,angle,tilt,face,note"

    var isRecording = false {
        didSet {
            guard isRecording != oldValue, isRecording else { return }
            rows.removeAll(keepingCapacity: true)
            swings = 0
        }
    }

    /// Shots the detector fired while recording (shown in Settings).
    private(set) var swings = 0
    @ObservationIgnored private var rows: [String] = []

    var isEmpty: Bool { rows.isEmpty }

    /// One sensor reading and the sample the detector saw.
    func add(_ motion: CMDeviceMotion, _ sample: MotionSample) {
        guard isRecording else { return }
        let r = motion.rotationRate, q = motion.attitude.quaternion, g = motion.gravity, a = motion.userAcceleration
        let values = [r.x, r.y, r.z, q.w, q.x, q.y, q.z, g.x, g.y, g.z, a.x, a.y, a.z, sample.rate, sample.angle, sample.tilt ?? 0, sample.face]
        append(time: motion.timestamp, kind: "m", values: values, note: "")
    }

    /// Something the detector or session decided (stage changes, shots, aborts).
    func mark(_ note: String, time: Double = ProcessInfo.processInfo.systemUptime) {
        guard isRecording else { return }
        if note.hasPrefix("fired") { swings += 1 }
        append(time: time, kind: "e", values: Array(repeating: nil, count: 17), note: note)
    }

    private func append(time: Double, kind: String, values: [Double?], note: String) {
        let numbers = values.map { $0.map { String(format: "%.5f", $0) } ?? "" }
        rows.append(([String(format: "%.4f", time), kind] + numbers + [note.replacingOccurrences(of: ",", with: ";")]).joined(separator: ","))
        if rows.count > Self.maxRows { rows.removeFirst(rows.count - Self.maxRows) }
    }

    /// The recording as a CSV file in the temporary folder.
    func writeFile() throws -> URL {
        let stamp = ISO8601DateFormatter().string(from: .now).replacingOccurrences(of: ":", with: "-")
        let url = FileManager.default.temporaryDirectory.appendingPathComponent("swing-\(stamp).csv")
        try ([Self.header] + rows).joined(separator: "\n").write(to: url, atomically: true, encoding: .utf8)
        return url
    }
}

/// What Settings shares: the CSV, written when the share sheet asks for it.
struct SwingRecording: Transferable {
    let recorder: SwingRecorder

    static var transferRepresentation: some TransferRepresentation {
        FileRepresentation(exportedContentType: .commaSeparatedText) { recording in
            SentTransferredFile(try await MainActor.run { try recording.recorder.writeFile() })
        }
    }
}
