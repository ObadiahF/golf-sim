import Foundation
import Testing
@testable import Golf_app

struct SwingRecorderTests {
    @Test func recordsOnlyWhileOnAndCountsShots() throws {
        let recorder = SwingRecorder()
        recorder.mark("fired early")
        #expect(recorder.isEmpty)
        recorder.isRecording = true
        recorder.mark("stage ready", time: 1)
        recorder.mark("fired(rate: 20, face: 1)", time: 2)
        #expect(recorder.swings == 1)
        let csv = try String(contentsOf: recorder.writeFile(), encoding: .utf8)
        let lines = csv.split(separator: "\n")
        #expect(lines.first.map(String.init) == SwingRecorder.header)
        #expect(lines.count == 3)
        #expect(lines.allSatisfy { $0.split(separator: ",", omittingEmptySubsequences: false).count == 20 })
    }

    @Test func turningOnAgainStartsOver() {
        let recorder = SwingRecorder()
        recorder.isRecording = true
        recorder.mark("fired")
        recorder.isRecording = false
        recorder.isRecording = true
        #expect(recorder.isEmpty)
        #expect(recorder.swings == 0)
    }
}
