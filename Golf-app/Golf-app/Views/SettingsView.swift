import SwiftUI

/// Host (auto-discovered or typed), face direction, swing scale, and a short how-to.
struct SettingsView: View {
    @Bindable var settings: AppSettings
    let link: SimLink
    let game: GameLink
    @State private var hostDraft = ""
    @State private var serverDraft = ""
    /// Why the typed server address was refused.
    @State private var serverProblem: String?
    @Environment(\.dismiss) private var dismiss

    var body: some View {
        NavigationStack {
            Form {
                Section {
                    HStack {
                        Text("Status")
                        Spacer()
                        StatusPill(text: link.connection.label, color: link.connection.color)
                    }
                    Button("Find PC automatically", systemImage: "dot.radiowaves.left.and.right") { link.discover() }
                    TextField("PC address, e.g. 192.168.1.20", text: $hostDraft)
                        .keyboardType(.numbersAndPunctuation)
                        .textInputAutocapitalization(.never)
                        .autocorrectionDisabled()
                        .submitLabel(.go)
                        .onSubmit(connect)
                    Button("Connect to this address", systemImage: "arrow.right.circle", action: connect)
                        .disabled(hostDraft.trimmingCharacters(in: .whitespaces).isEmpty)
                } header: {
                    Text("Golf sim PC")
                } footer: {
                    Text("The phone and PC must be on the same Wi-Fi (or the PC on the phone's hotspot) and the sim must be running. Allow UDP port 4242 in the PC's firewall.")
                }

                Section {
                    HStack {
                        Text("Server")
                        Spacer()
                        StatusPill(text: game.connection.label, color: game.connection.color)
                    }
                    HStack {
                        Text("Sim")
                        Spacer()
                        StatusPill(text: game.simConnected ? "Connected" : "Not connected", color: game.simConnected ? Theme.good : Theme.warn)
                    }
                    TextField("Local server, e.g. \(Self.exampleServer)", text: $serverDraft)
                        .keyboardType(.URL)
                        .textInputAutocapitalization(.never)
                        .autocorrectionDisabled()
                        .submitLabel(.go)
                        .onSubmit { applyServer() }
                        .onChange(of: serverDraft) { serverProblem = nil }
                    if let serverProblem {
                        Label(serverProblem, systemImage: "exclamationmark.triangle.fill")
                            .font(.footnote)
                            .foregroundStyle(Theme.warn)
                    }
                    Button("Reconnect", systemImage: "arrow.clockwise") {
                        if applyServer() { game.reconnect() }
                    }
                } header: {
                    Text("Game server")
                } footer: {
                    Text(verbatim: "Rounds and scores go through the game server, \(settings.serverURL?.absoluteString ?? AppConfig.hostedServer). Leave the address empty to use the hosted server. For a server on your PC or LAN, type its address, e.g. \(Self.exampleServer), and allow TCP \(AppConfig.defaultServerPort) in that PC's firewall.")
                }

                Section {
                    VStack(alignment: .leading) {
                        Text("Swing scale \(settings.scale, specifier: "%.1f")×")
                        Slider(value: $settings.scale, in: AppSettings.scaleRange, step: 0.1)
                    }
                    VStack(alignment: .leading) {
                        Text("Putt scale \(settings.puttScale, specifier: "%.1f")×")
                        Slider(value: $settings.puttScale, in: AppSettings.puttScaleRange, step: 0.05)
                    }
                    Toggle("Flip face direction", isOn: $settings.flipFace)
                } header: {
                    Text("Swing")
                } footer: {
                    Text("Raise the scale to make half swings count as full ones. The putt scale does the same for putts: raise it if a normal stroke comes up short, lower it if putts race past. Flip the face direction if fades come out as draws (it depends on which way the screen faces in your grip).")
                }

                Section("How to swing") {
                    Label("Hold the phone like a club grip, screen facing you.", systemImage: "hand.raised")
                    Label("Tap Address, take your stance and hold still until it buzzes.", systemImage: "figure.golf")
                    Label("Swing. After each shot, return to address and hold still to re-arm.", systemImage: "arrow.uturn.backward")
                    Label("Keep a firm grip and room around you. Use a wrist strap.", systemImage: "exclamationmark.triangle")
                }
            }
            .navigationTitle("Settings")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .confirmationAction) { Button("Done") { dismiss() } }
            }
            .onAppear {
                hostDraft = settings.host
                serverDraft = settings.server
            }
        }
    }

    private func connect() {
        link.connect(to: hostDraft)
    }

    /// "192.168.1.20:8080", written without digit grouping.
    private static let exampleServer = "192.168.1.20:\(String(AppConfig.defaultServerPort))"

    /// Empty means the hosted server; anything else must parse as an address. False (with the reason shown) when it doesn't.
    @discardableResult
    private func applyServer() -> Bool {
        let draft = serverDraft.trimmingCharacters(in: .whitespaces)
        if let problem = AppConfig.serverAddressProblem(draft) {
            serverProblem = problem
            Haptics.problem()
            return false
        }
        serverProblem = nil
        if draft != settings.server { settings.server = draft }
        return true
    }
}
