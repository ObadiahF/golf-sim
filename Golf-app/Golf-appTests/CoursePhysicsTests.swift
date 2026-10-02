import Foundation
import Testing
@testable import Golf_app

/// The course physics screen's model: defaults, Stimp conversion and the /api/physics JSON.
struct CoursePhysicsTests {
    private static let decoder = GameProtocol.decoder

    @Test func defaultsMatchTheSimsAsset() {
        // Golf-sim BallPhysics.asset: green 1/0.4/0.06, fairway 0.9/0.45/0.11, rough 0.45/0.7/0.7, native 0.5/0.7/0.8, bunker 0.2/0.8/1.5.
        let byID = Dictionary(uniqueKeysWithValues: CoursePhysics.groups.map { ($0.id, $0) })
        #expect(byID["green"]?.defaults == .init(rolling: 0.06, restitution: 1, friction: 0.4))
        #expect(byID["fairway"]?.surfaces == ["fairway", "tee"])
        #expect(byID["native"]?.surfaces == ["native", "scrub", "woods"])
        #expect(byID["bunker"]?.defaults.rolling == 1.5)
        for group in CoursePhysics.groups {
            #expect(group.rollRange.contains(group.defaults.rolling), "\(group.id)")
            #expect(group.rollRange.lowerBound >= 0.02 && group.rollRange.upperBound <= 3, "server range, \(group.id)")
        }
    }

    @Test func greenRollingIsShownAsStimp() {
        #expect(abs(CoursePhysics.stimp(rolling: 0.06) - 9.33) < 0.01) // the sim's green, Stimp 9.3
        #expect(abs(CoursePhysics.stimp(rolling: 0.06) - PuttModel.defaultStimp) < 0.05)
        for stimp in [6.0, 9.3, 11.2, 14.0] {
            #expect(abs(CoursePhysics.stimp(rolling: CoursePhysics.rolling(stimp: stimp)) - stimp) < 1e-9)
        }
        // Stimp is the roll distance at the Stimpmeter's release speed.
        let rolled = CoursePhysics.rollDistance(speed: PuttModel.stimpReleaseSpeed, rolling: 0.08)
        #expect(abs(rolled / PuttModel.metersPerFoot - CoursePhysics.stimp(rolling: 0.08)) < 1e-9)
    }

    @Test func decodedProfileShowsOverridesOverDefaults() throws {
        let json = #"{"surfaces":[{"surface":"green","rolling":0.07},{"surface":"fairway"},{"surface":"tee","friction":0.9},{"surface":"rough","restitution":0.3,"friction":0.6}]}"#
        let profile = try Self.decoder.decode(CoursePhysics.Profile.self, from: Data(json.utf8))
        #expect(!profile.isDefault)
        let values = CoursePhysics.values(from: profile)
        #expect(values["green"] == .init(rolling: 0.07, restitution: 1, friction: 0.4))
        #expect(values["fairway"] == .init(rolling: 0.11, restitution: 0.9, friction: 0.45)) // a tee-only override isn't shown
        #expect(values["rough"] == .init(rolling: 0.7, restitution: 0.3, friction: 0.6))
        #expect(values["bunker"] == CoursePhysics.groups.last?.defaults)
        let empty = try Self.decoder.decode(CoursePhysics.Profile.self, from: Data(#"{"surfaces":[{"surface":"green"}]}"#.utf8))
        #expect(empty.isDefault)
    }

    @Test func updateSendsEverySurfaceWithNullsForDefaults() throws {
        var values = CoursePhysics.values(from: .init(surfaces: []))
        values["fairway"]?.friction = 0.6
        values["native"]?.rolling = 1.23456789
        values["green"]?.rolling = CoursePhysics.rolling(stimp: 10)
        let update = CoursePhysics.update(from: values)
        #expect(update.surfaces.map(\.surface) == ["green", "fairway", "tee", "rough", "native", "scrub", "woods", "bunker"])
        let json = try JSONSerialization.jsonObject(with: GameProtocol.encoder.encode(update)) as? [String: Any]
        let surfaces = try #require(json?["surfaces"] as? [[String: Any]])
        let tee = try #require(surfaces.first { $0["surface"] as? String == "tee" })
        #expect(tee["friction"] as? Double == 0.6) // tee follows fairway
        #expect(tee["rolling"] is NSNull && tee["restitution"] is NSNull) // explicit null: clear the override
        let woods = try #require(surfaces.first { $0["surface"] as? String == "woods" })
        #expect(woods["rolling"] as? Double == 1.2346) // follows native, rounded
        let green = try #require(surfaces.first { $0["surface"] as? String == "green" })
        #expect(abs((green["rolling"] as? Double ?? 0) - 0.056) < 1e-9) // Stimp 10, rounded to 4 places
        #expect(CoursePhysics.update(from: CoursePhysics.values(from: .init(surfaces: []))).isDefault)
    }
}

extension GameAPITests {
    @Test func physicsGetPutAndReset() async throws {
        Stub.reply = (200, #"{"surfaces":[{"surface":"green","rolling":0.07}]}"#)
        #expect(try await api.physics().surfaces.first?.rolling == 0.07)
        #expect(Stub.lastRequest?.httpMethod == "GET" && Stub.lastRequest?.url?.path == "/api/physics")

        let update = CoursePhysics.Profile(surfaces: [.init(surface: "rough", friction: 0.6)])
        Stub.reply = (200, #"{"surfaces":[{"surface":"rough","friction":0.6}]}"#)
        #expect(try await api.savePhysics(update).surfaces.first?.friction == 0.6)
        #expect(Stub.lastRequest?.httpMethod == "PUT")
        #expect(Stub.lastRequest?.value(forHTTPHeaderField: "Authorization") == "Bearer \(AppConfig.serverToken)")
        let sent = try #require((jsonObject(Stub.lastBody)["surfaces"] as? [[String: Any]])?.first)
        #expect(sent["surface"] as? String == "rough" && sent["friction"] as? Double == 0.6 && sent["rolling"] is NSNull)

        Stub.reply = (200, #"{"surfaces":[{"surface":"green"}]}"#)
        #expect(try await api.resetPhysics().isDefault)
        #expect(Stub.lastRequest?.httpMethod == "DELETE" && Stub.lastRequest?.url?.path == "/api/physics")

        Stub.reply = (400, #"{"status":400,"message":"Validation failed","fieldErrors":{"surfaces[0].rolling":"must be between 0.02 and 3"}}"#)
        await #expect(throws: GameAPI.APIError.server(status: 400, message: "Validation failed")) {
            try await api.savePhysics(update)
        }
    }
}
