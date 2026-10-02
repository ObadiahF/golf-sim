import Foundation
import Testing
@testable import Golf_app

/// Polls until the condition holds or about `seconds` pass.
@MainActor
func waitUntil(seconds: Double = 2, _ condition: () -> Bool) async {
    for _ in 0..<Int(seconds * 50) where !condition() { try? await Task.sleep(for: .milliseconds(20)) }
}

/// Fresh settings in their own UserDefaults suite.
@MainActor
func testSettings(_ suite: String) throws -> AppSettings {
    let defaults = try #require(UserDefaults(suiteName: suite))
    defaults.removePersistentDomain(forName: suite)
    return AppSettings(defaults: defaults)
}

/// A JSON object from a message, for checking what goes on the wire.
func jsonObject(_ data: Data?) -> [String: Any] {
    data.flatMap { try? JSONSerialization.jsonObject(with: $0) as? [String: Any] } ?? [:]
}
