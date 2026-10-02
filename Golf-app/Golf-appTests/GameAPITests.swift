import Foundation
import Testing
@testable import Golf_app

/// The REST client against a URLProtocol stub (no network).
@Suite(.serialized)
struct GameAPITests {
    /// Answers every request with the canned reply and remembers the request.
    final class Stub: URLProtocol {
        nonisolated(unsafe) static var reply: (status: Int, body: String) = (200, "{}")
        nonisolated(unsafe) static var lastRequest: URLRequest?
        nonisolated(unsafe) static var lastBody: Data?

        override class func canInit(with request: URLRequest) -> Bool { true }
        override class func canonicalRequest(for request: URLRequest) -> URLRequest { request }

        override func startLoading() {
            Self.lastRequest = request
            Self.lastBody = request.httpBody ?? request.httpBodyStream.map(Self.read)
            let response = HTTPURLResponse(url: request.url!, statusCode: Self.reply.status, httpVersion: nil, headerFields: nil)!
            client?.urlProtocol(self, didReceive: response, cacheStoragePolicy: .notAllowed)
            client?.urlProtocol(self, didLoad: Data(Self.reply.body.utf8))
            client?.urlProtocolDidFinishLoading(self)
        }

        override func stopLoading() {}

        private static func read(_ stream: InputStream) -> Data {
            stream.open()
            defer { stream.close() }
            var data = Data()
            var buffer = [UInt8](repeating: 0, count: 4096)
            while stream.hasBytesAvailable {
                let count = stream.read(&buffer, maxLength: buffer.count)
                if count <= 0 { break }
                data.append(buffer, count: count)
            }
            return data
        }
    }

    let api: GameAPI = {
        let config = URLSessionConfiguration.ephemeral
        config.protocolClasses = [Stub.self]
        return GameAPI(baseURL: URL(string: "http://10.0.0.5:8080")!, session: URLSession(configuration: config))
    }()

    @Test func startGamePostsPlayersWithToken() async throws {
        Stub.reply = (201, GameProtocolTests.gameJSON)
        let game = try await api.startGame(players: ["Ann", "Bob"], holes: 9)
        #expect(game.id == 12)
        let request = try #require(Stub.lastRequest)
        #expect(request.httpMethod == "POST")
        #expect(request.url?.absoluteString == "http://10.0.0.5:8080/api/games")
        #expect(request.value(forHTTPHeaderField: "Authorization") == "Bearer \(AppConfig.serverToken)")
        let body = jsonObject(Stub.lastBody)
        #expect(body["players"] as? [String] == ["Ann", "Bob"])
        #expect(body["holes"] as? Int == 9)
        _ = try await api.startGame(players: ["Ann"], holes: 18)
        #expect(jsonObject(Stub.lastBody)["holes"] as? Int == 18)
    }

    @Test func currentGameIsNilOnNoContent() async throws {
        Stub.reply = (204, "")
        #expect(try await api.currentGame() == nil)
        #expect(Stub.lastRequest?.url?.path == "/api/games/current")
        Stub.reply = (200, GameProtocolTests.gameJSON)
        #expect(try await api.currentGame()?.players.count == 2)
    }

    @Test func serverErrorsCarryTheMessage() async {
        Stub.reply = (400, #"{"status":400,"error":"Bad Request","message":"Duplicate player name: ann"}"#)
        await #expect(throws: GameAPI.APIError.server(status: 400, message: "Duplicate player name: ann")) {
            try await api.startGame(players: ["Ann", "ann"], holes: 18)
        }
    }

    @Test func leaderboardAndPlayers() async throws {
        Stub.reply = (200, #"{"bestRounds":[{"player":"Ann","gameId":3,"status":"FINISHED","courseName":null,"holesCount":9,"playedAt":"2026-10-01T10:00:00Z","holesPlayed":9,"total":38,"toPar":2,"won":true}],"mostWins":[{"player":"Ann","wins":1,"finishedRounds":1}],"bestAverageToPar":[{"player":"Ann","finishedRounds":1,"averageToPar":2.0}]}"#)
        let board = try await api.leaderboard()
        #expect(board.bestRounds.first?.total == 38)
        #expect(board.mostWins.first?.wins == 1)
        #expect(board.lowestHandicap == nil && board.mostBirdies == nil) // older server
        // Older server: no handicap, hole tallies or 9/18-hole bests (its mixed-length bestTotal is ignored).
        Stub.reply = (200, #"[{"name":"Ann","gamesPlayed":2,"finishedRounds":1,"wins":1,"bestTotal":38,"averageTotal":38.0,"averageToPar":2.0,"lastPlayedAt":null}]"#)
        let old = try #require(try await api.players().first)
        #expect(old.finishedRounds == 1 && old.best9 == nil && old.best18 == nil && old.avg9 == nil && old.avg18 == nil)
        // Newer server: handicap, recent average and hole tallies.
        Stub.reply = (200, #"[{"name":"Ann","gamesPlayed":4,"finishedRounds":3,"wins":2,"best9":36,"best18":null,"avg9":37.0,"avg18":null,"averageToPar":2.0,"recentAverageToPar":1.4,"handicap":4.2,"holes":{"played":27,"holesInOne":1,"eagles":0,"birdies":3,"pars":14,"bogeys":7,"doubleBogeysOrWorse":2},"lastPlayedAt":"2026-10-02T07:43:47.309802Z"}]"#)
        let ann = try #require(try await api.players().first)
        #expect(ann.handicap == 4.2 && ann.holes?.birdies == 3 && ann.holes?.holesInOne == 1)
        #expect(ann.best9 == 36 && ann.best18 == nil && ann.avg9 == 37.0 && ann.avg18 == nil)
        #expect(ann.averageToPar == 2.0 && ann.recentAverageToPar == 1.4)
        // Players with only 18-hole rounds, or only short games (no scores at all).
        Stub.reply = (200, #"[{"name":"Bo","gamesPlayed":1,"finishedRounds":1,"wins":1,"best9":null,"best18":74,"avg9":null,"avg18":74.0,"averageToPar":2.0,"recentAverageToPar":2.0,"handicap":null,"lastPlayedAt":null},{"name":"Cy","gamesPlayed":2,"finishedRounds":2,"wins":2,"best9":null,"best18":null,"avg9":null,"avg18":null,"averageToPar":null,"recentAverageToPar":null,"handicap":null,"lastPlayedAt":null}]"#)
        let both = try await api.players()
        #expect(both[0].best18 == 74 && both[0].avg18 == 74.0 && both[0].best9 == nil)
        #expect(both[1].finishedRounds == 2 && both[1].averageToPar == nil && both[1].best18 == nil)
        Stub.reply = (200, #"{"bestRounds":[],"mostWins":[],"bestAverageToPar":[],"lowestHandicap":[{"player":"Ann","finishedRounds":3,"handicap":4.2}],"mostBirdies":[{"player":"Ann","birdiesOrBetter":4,"holesPlayed":27}]}"#)
        let newer = try await api.leaderboard()
        #expect(newer.lowestHandicap?.first?.handicap == 4.2 && newer.mostBirdies?.first?.birdiesOrBetter == 4)
        Stub.reply = (200, "[\(GameProtocolTests.gameJSON)]")
        #expect(try await api.recentGames(limit: 1).first?.id == 12)
        #expect(Stub.lastRequest?.url?.query == "limit=1")
    }
}
