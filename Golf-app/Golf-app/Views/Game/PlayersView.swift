import SwiftUI

/// Who's playing: add names, drag to set the turn order, pick 9 or 18 holes, then Start Game.
struct PlayersView: View {
    let session: SwingSession
    @State private var draft = ""
    @State private var problem: String?
    @State private var starting = false
    /// The in-progress game End game would abandon, while its confirmation shows.
    @State private var confirmEnd: GameView?
    @FocusState private var typing: Bool

    private var settings: AppSettings { session.settings }

    var body: some View {
        ZStack {
            Theme.background
            VStack(spacing: 14) {
                AppHeader(title: "Players", session: session)
                addField
                list
                if let current = session.game.game, current.isInProgress {
                    HStack {
                        Text("Game #\(current.id) is in progress. Starting a new one ends it.")
                            .font(.system(size: 13, weight: .medium, design: .rounded))
                            .foregroundStyle(Theme.muted)
                        Button("End game") { confirmEnd = current }
                            .font(.system(size: 14, weight: .bold, design: .rounded))
                            .tint(Theme.warn)
                    }
                    .confirmationDialog("End game #\(current.id)?", isPresented: Binding { confirmEnd != nil } set: { if !$0 { confirmEnd = nil } },
                                        titleVisibility: .visible, presenting: confirmEnd) { game in
                        Button("End game", role: .destructive) { call { _ = try await $0.endGame(id: game.id) } }
                    } message: { game in
                        Text("The \(game.holesCount)-hole round is abandoned and its scores won't count. The sim goes back to the menu.")
                    }
                }
                if let problem {
                    Text(problem).font(.system(size: 15, weight: .semibold, design: .rounded)).foregroundStyle(Theme.warn)
                }
                Picker("Holes", selection: Bindable(settings).holes) {
                    ForEach(AppConfig.roundLengths, id: \.self) { Text("\($0) holes").tag($0) }
                }
                .pickerStyle(.segmented)
                startButton
            }
            .padding(.horizontal, 16)
            .padding(.bottom, 10)
        }
    }

    private var addField: some View {
        HStack(spacing: 10) {
            TextField("Player name", text: $draft)
                .textInputAutocapitalization(.words)
                .autocorrectionDisabled()
                .submitLabel(.done)
                .focused($typing)
                .onSubmit(add)
                .font(.system(size: 18, weight: .semibold, design: .rounded))
                .foregroundStyle(Theme.chalk)
            Button(action: add) {
                Image(systemName: "plus.circle.fill").font(.system(size: 30)).foregroundStyle(Theme.flag)
            }
            .disabled(draft.trimmingCharacters(in: .whitespaces).isEmpty)
            .accessibilityLabel("Add player")
        }
        .card(padding: 14)
    }

    private var list: some View {
        List {
            ForEach(Array(settings.players.enumerated()), id: \.element) { index, name in
                HStack(spacing: 12) {
                    Text("\(index + 1)")
                        .font(Theme.number(18))
                        .foregroundStyle(Theme.fairwayBottom)
                        .frame(width: 30, height: 30)
                        .background(Theme.flag, in: .circle)
                    Text(name).font(.system(size: 18, weight: .semibold, design: .rounded)).foregroundStyle(Theme.chalk)
                }
                .listRowBackground(Theme.card)
            }
            .onMove { settings.players.move(fromOffsets: $0, toOffset: $1) }
            .onDelete { settings.players.remove(atOffsets: $0) }
        }
        // The last error ("Up to 8 players", a duplicate name) no longer applies once the list changes.
        .onChange(of: settings.players) { problem = nil }
        .environment(\.editMode, .constant(.active))
        .scrollContentBackground(.hidden)
        .listStyle(.insetGrouped)
        .overlay {
            if settings.players.isEmpty {
                Text("Add everyone who's playing.\nThey take turns in this order.")
                    .multilineTextAlignment(.center)
                    .font(.system(size: 16, weight: .medium, design: .rounded))
                    .foregroundStyle(Theme.muted)
            }
        }
    }

    private var startButton: some View {
        Button(action: start) {
            HStack {
                if starting { ProgressView().tint(Theme.fairwayBottom) }
                Text("Start Game · \(settings.holes) holes").font(.system(size: 20, weight: .heavy, design: .rounded))
            }
            .foregroundStyle(Theme.fairwayBottom)
            .frame(maxWidth: .infinity, minHeight: 60)
            .background(Theme.flag, in: .rect(cornerRadius: 18))
        }
        .buttonStyle(PressDimStyle())
        .disabled(settings.players.isEmpty || starting)
        .opacity(settings.players.isEmpty ? 0.5 : 1)
    }

    private func add() {
        switch Roster.validate(draft, joining: settings.players) {
        case .success(let name):
            settings.players.append(name)
            draft = ""
            problem = nil
            Haptics.tick()
        case .failure(let reason):
            problem = reason.message
            Haptics.problem()
        }
        typing = true
    }

    private func start() {
        call {
            _ = try await $0.startGame(players: settings.players, holes: settings.holes)
            Haptics.addressSet()
            if !session.game.simConnected { problem = "Game started. \(GameLink.noSimHint)" }
        }
    }

    /// Runs a server call, showing progress and any error.
    private func call(_ work: @escaping (GameAPI) async throws -> Void) {
        guard let api = GameAPI.forSettings(settings) else {
            problem = GameAPI.APIError.noServer.errorDescription
            return
        }
        starting = true
        problem = nil
        Task {
            defer { starting = false }
            do { try await work(api) } catch {
                problem = error.localizedDescription
                Haptics.problem()
            }
        }
    }
}
