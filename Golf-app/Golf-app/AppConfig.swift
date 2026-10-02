import Foundation

/// Fixed app-wide configuration. The token is shared with the game server (`GOLF_API_TOKEN`) and the sim.
nonisolated enum AppConfig {
    /// Shared secret for the game server: REST `Authorization: Bearer <token>`, WebSocket `?token=<token>`.
    static let serverToken = "golf-sim-dev-token"
    /// The hosted game server, used unless Settings names another (REST here, WebSocket at `wss://…/ws`).
    static let hostedServer = "https://golf-server.obadiahfusco.xyz"
    /// The game server's default port on a PC or LAN server (docker compose publishes 8080).
    static let defaultServerPort = 8080
    /// Round lengths offered on the Players screen; the first is the default.
    static let roundLengths = [9, 18]

    /// The server's base URL from an address: "192.168.1.20", "pc.local:9000" or "http://192.168.1.20:8080".
    /// Plain http on port 8080 unless the text says otherwise; nil when empty or malformed.
    static func serverURL(_ address: String) -> URL? {
        let text = address.trimmingCharacters(in: .whitespaces)
        guard !text.isEmpty,
              var parts = URLComponents(string: text.contains("://") ? text : "http://" + text),
              let scheme = parts.scheme?.lowercased(), ["http", "https"].contains(scheme),
              let host = parts.host, !host.isEmpty
        else { return nil }
        if parts.port == nil, scheme == "http" { parts.port = defaultServerPort }
        parts.path = ""
        parts.query = nil
        return parts.url
    }

    /// Why a typed server address can't be used, or nil when it can (empty means the hosted server).
    static func serverAddressProblem(_ address: String) -> String? {
        let text = address.trimmingCharacters(in: .whitespaces)
        guard !text.isEmpty, serverURL(text) == nil else { return nil }
        return "“\(text)” isn't a server address. Use a host or IP, with an optional port, e.g. 192.168.1.20:\(String(defaultServerPort)) or https://golf.example."
    }

    /// The server's WebSocket endpoint: http -> ws, https -> wss, at `/ws`.
    static func webSocketURL(server: URL) -> URL? {
        guard var parts = URLComponents(url: server, resolvingAgainstBaseURL: false) else { return nil }
        parts.scheme = parts.scheme == "https" ? "wss" : "ws"
        parts.path = "/ws"
        return parts.url
    }

    /// The WebSocket endpoint with this query. Values are fully percent-encoded: `URLQueryItem` leaves `+` literal,
    /// and the server decodes that as a space.
    static func webSocketURL(server: URL, query: KeyValuePairs<String, String>) -> URL? {
        guard let endpoint = webSocketURL(server: server),
              var parts = URLComponents(url: endpoint, resolvingAgainstBaseURL: false)
        else { return nil }
        parts.percentEncodedQueryItems = query.map { URLQueryItem(name: percentEncode($0.key), value: percentEncode($0.value)) }
        return parts.url
    }

    /// Characters a query name or value may keep as they are (RFC 3986 unreserved).
    private static let unreserved = CharacterSet(charactersIn: "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-._~")

    static func percentEncode(_ text: String) -> String {
        text.addingPercentEncoding(withAllowedCharacters: unreserved) ?? text
    }
}
