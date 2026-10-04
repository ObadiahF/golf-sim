import SwiftUI

/// Host (auto-discovered or typed), face direction, swing scale, club power and course physics, the swing
/// recorder, and a short how-to.
struct SettingsView: View {
    @Bindable var settings: AppSettings
    let link: SimLink
    let game: GameLink
    @Bindable var recorder: SwingRecorder
    @State private var hostDraft = ""
    @State private var serverDraft = ""
    /// Why the typed server address was refused.
    @State private var serverProblem: String?
    @State private var roomDraft = ""
    /// Why the typed room code was refused.
    @State private var roomProblem: String?
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
                    problemLabel(serverProblem)
                    TextField("Room code on the TV, e.g. K7QF2", text: $roomDraft)
                        .textInputAutocapitalization(.characters)
                        .autocorrectionDisabled()
                        .submitLabel(.go)
                        .onSubmit { applyRoom() }
                        .onChange(of: roomDraft) { roomProblem = nil }
                    problemLabel(roomProblem)
                    Button("Reconnect", systemImage: "arrow.clockwise") {
                        let serverApplied = applyServer()
                        if applyRoom() && serverApplied { game.reconnect() }
                    }
                } header: {
                    Text("Game server")
                } footer: {
                    Text(verbatim: "Rounds and scores go through the game server, \(settings.serverURL?.absoluteString ?? AppConfig.hostedServer). Leave the address empty to use the hosted server. For a server on your PC or LAN, type its address, e.g. \(Self.exampleServer), and allow TCP \(AppConfig.defaultServerPort) in that PC's firewall. Type the room code the TV shows (main or pause menu) so this phone controls that sim; leave it empty for an older sim without one.")
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
                    Toggle("Curve shots by face angle", isOn: $settings.shapeShots)
                    if settings.shapeShots {
                        Toggle("Flip face direction", isOn: $settings.flipFace)
                    }
                } header: {
                    Text("Swing")
                } footer: {
                    Text("Raise the scale to make half swings count as full ones. The putt scale does the same for putts: raise it if a normal stroke comes up short, lower it if putts race past. Shots fly straight where you aim unless \"Curve shots by face angle\" is on; the phone's face reading is rough, so expect wild curves with it. Flip the face direction if fades come out as draws.")
                }

                Section {
                    NavigationLink {
                        ClubPowerView(settings: settings)
                    } label: {
                        Label("Club power & sensitivity", systemImage: "dial.medium")
                    }
                    NavigationLink {
                        CoursePhysicsView(api: GameAPI.forSettings(settings), simConnected: game.simConnected)
                    } label: {
                        Label("Course physics", systemImage: "circle.bottomhalf.filled")
                    }
                } header: {
                    Text("Tuning")
                } footer: {
                    Text("Club power & sensitivity: per club, how far a swing sends the ball and how small a swing counts (this phone only). Course physics: how the ball bounces and rolls on each surface, saved on the game server for the sim.")
                }

                Section {
                    Toggle("Record swings", isOn: $recorder.isRecording)
                    if !recorder.isEmpty {
                        Text("\(recorder.swings) swings counted so far").foregroundStyle(.secondary)
                        ShareLink(item: SwingRecording(recorder: recorder), preview: SharePreview("Swing recording"))
                    }
                } header: {
                    Text("Swing recording")
                } footer: {
                    Text("Turn on, practice a few swings (including any that don't count), then share the file (AirDrop it to the Mac). It keeps the last five minutes of sensor data. Turning it on again starts over.")
                }

                Section("How to swing") {
                    Label("Hold the phone like a club grip, screen facing you.", systemImage: "hand.raised")
                    Label("Tap Start (or Address), take your stance and hold still until it buzzes.", systemImage: "figure.golf")
                    Label("Swing. After each shot, return to address and hold still; the next buzz means swing again.", systemImage: "arrow.uturn.backward")
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
                roomDraft = settings.room
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

    /// Empty means the default room; anything else must be a 4-8 character code. False (with the reason shown) when it isn't.
    @discardableResult
    private func applyRoom() -> Bool {
        roomProblem = settings.setRoom(roomDraft)
        guard roomProblem == nil else {
            Haptics.problem()
            return false
        }
        roomDraft = settings.room // shows the normalized code
        return true
    }

    /// The reason a typed value was refused, under its field.
    @ViewBuilder
    private func problemLabel(_ problem: String?) -> some View {
        if let problem {
            Label(problem, systemImage: "exclamationmark.triangle.fill")
                .font(.footnote)
                .foregroundStyle(Theme.warn)
        }
    }
}
