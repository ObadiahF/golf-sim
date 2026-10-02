import Foundation
import Testing
@testable import Golf_app

/// Shots fly where the player aims unless face-angle shaping is turned on.
@MainActor
struct AimTests {
    /// The Windows log's drives: the face reading pinned at the 15 degree limit.
    private let wildImpact = Impact(rate: 30, face: -40, angle: 10, time: 0)

    @Test func straightAtTheAimByDefault() throws {
        let settings = try testSettings("AimTests.default")
        #expect(!settings.shapeShots)
        let shot = Shot.from(wildImpact, club: Club.bag[0], scale: 1, faceSign: settings.faceFactor)
        #expect(shot.azimuth == 0)
        #expect(shot.sidespin == 0)
        #expect(shot.face == 0)
        #expect(shot.ballSpeed > 0)
    }

    @Test func shapingUsesTheFaceWhenOn() throws {
        let settings = try testSettings("AimTests.shaping")
        settings.shapeShots = true
        let shot = Shot.from(wildImpact, club: Club.bag[0], scale: 1, faceSign: settings.faceFactor)
        #expect(shot.face == Shot.maxFace) // -40 x faceSign -1, clamped
        #expect(shot.azimuth > 0)
        #expect(shot.sidespin > 0)
        settings.flipFace = true
        #expect(Shot.from(wildImpact, club: Club.bag[0], scale: 1, faceSign: settings.faceFactor).face == -Shot.maxFace)
    }
}
