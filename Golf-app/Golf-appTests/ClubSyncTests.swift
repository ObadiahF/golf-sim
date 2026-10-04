import Foundation
import Testing
@testable import Golf_app

/// The phone's club against the sim's states: picks survive stale echoes, and a refused pick shows the sim's club.
@MainActor
struct ClubSyncTests {
    private func state(club: String, putting: Bool = false) throws -> GameProtocol.SimState {
        let json = #"{"type":"state","screen":"game","canShoot":true,"club":"\#(club)","putting":\#(putting)}"#
        guard case .state(let state)? = GameProtocol.decode(Data(json.utf8)) else { throw CancellationError() }
        return state
    }

    /// A session following a connected sim that has `club`.
    private func session(_ suite: String, club: String, putting: Bool = false) throws -> SwingSession {
        let session = SwingSession(settings: try testSettings(suite), motion: ShotReadinessTests.ManualMotion())
        session.game.handle(.hello(.init(simConnected: true, remotes: ["me"], game: nil, state: try state(club: club, putting: putting))))
        #expect(session.club.name == club)
        return session
    }

    private func pick(_ name: String, on session: SwingSession) throws {
        session.selectClub(try #require(Club.index(named: name)))
    }

    @Test func quickTapsWithDelayedEchoesEndOnTheLastPick() throws {
        let session = try session("ClubSyncTests.taps", club: "Driver")
        for name in ["3 Wood", "5 Iron", "7 Iron"] { try pick(name, on: session) }
        var seen: [String] = []
        for echo in ["3 Wood", "5 Iron", "7 Iron", "7 Iron"] { // the sim's states for each tap, arriving late
            session.game.handle(.state(try state(club: echo)))
            seen.append(session.club.name)
        }
        #expect(seen == ["7 Iron", "7 Iron", "7 Iron", "7 Iron"]) // never replays the earlier taps
    }

    @Test func aConfirmedPickFollowsTheSimAgain() throws {
        let session = try session("ClubSyncTests.follow", club: "Driver")
        try pick("5 Iron", on: session)
        session.game.handle(.state(try state(club: "5 Iron"))) // confirmed
        session.game.handle(.state(try state(club: "Putter", putting: true))) // the sim moved on (onto the green)
        #expect(session.club.name == "Putter")
    }

    @Test func aRefusedPickResyncsToTheSimsClub() async throws {
        let session = try session("ClubSyncTests.refused", club: "Putter", putting: true)
        session.clubEchoWindow = .milliseconds(50)
        try pick("Driver", on: session)
        session.game.handle(.state(try state(club: "Putter", putting: true))) // the green keeps the putter
        #expect(session.club.name == "Driver") // could still be a stale echo
        try await Task.sleep(for: .milliseconds(300))
        #expect(session.club.name == "Putter") // same club as last mirrored, still shown
    }

    @Test func aRefusedPickWithNoFurtherStatesStillResyncs() async throws {
        let session = try session("ClubSyncTests.silent", club: "Putter", putting: true)
        session.clubEchoWindow = .milliseconds(50)
        try pick("Driver", on: session) // the sim says nothing back
        try await Task.sleep(for: .milliseconds(300))
        #expect(session.club.name == "Putter")
    }

    @Test func aStateAfterTheWindowIsFollowed() async throws {
        let session = try session("ClubSyncTests.late", club: "Driver")
        session.clubEchoWindow = .milliseconds(50)
        try pick("5 Iron", on: session)
        try await Task.sleep(for: .milliseconds(300))
        session.game.handle(.state(try state(club: "9 Iron"))) // another phone picked
        #expect(session.club.name == "9 Iron")
    }

    @Test func withoutASimPicksStay() throws {
        let session = SwingSession(settings: try testSettings("ClubSyncTests.nosim"), motion: ShotReadinessTests.ManualMotion())
        session.game.handle(.hello(.init(simConnected: false, remotes: ["me"], game: nil, state: nil)))
        try pick("7 Iron", on: session)
        #expect(session.club.name == "7 Iron")
    }
}
