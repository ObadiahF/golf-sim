import SwiftUI

/// Settings > Course physics: live tuning of how the ball bounces and rolls on each surface, saved to the game
/// server, which pushes it to the sim; it applies from the next shot. Change, Save, hit a shot.
struct CoursePhysicsView: View {
    let api: GameAPI?
    let simConnected: Bool

    private enum Status: Equatable {
        case loading
        case ready
        case saving
        case saved(String)
        case failed(String)
    }

    @State private var values: [CoursePhysics.Group.ID: CoursePhysics.Values] = [:]
    /// What the server has, to tell unsaved changes apart.
    @State private var saved: [CoursePhysics.Group.ID: CoursePhysics.Values] = [:]
    @State private var status = Status.loading
    @State private var confirmReset = false

    private var hasChanges: Bool { values != saved }
    private var busy: Bool { status == .loading || status == .saving }

    var body: some View {
        Form {
            if !values.isEmpty {
                ForEach(CoursePhysics.groups) { group in
                    Section(group.title) {
                        rollRow(group)
                        sliderRow("Bounce", group: group, field: .restitution, range: CoursePhysics.bounceRange)
                        sliderRow("Grip", group: group, field: .friction, range: CoursePhysics.gripRange)
                    }
                }
            }

            Section {
                statusLabel
                Button("Save", systemImage: "checkmark.circle", action: save)
                    .disabled(busy || values.isEmpty || !hasChanges)
                Button("Reset to defaults", systemImage: "arrow.counterclockwise", role: .destructive) { confirmReset = true }
                    .disabled(busy || values.isEmpty)
            } footer: {
                Text("Roll: how far the ball runs (the green as its Stimp; elsewhere the rolling resistance and how far a ball rolling at 4 m/s goes). Bounce: energy kept when it lands. Grip: how much the turf grabs it on a bounce, which kills spin and speed. Grey values are the game's built-in ones. Saved values apply from the sim's next shot.")
            }
        }
        .navigationTitle("Course physics")
        .navigationBarTitleDisplayMode(.inline)
        .toolbar {
            ToolbarItem(placement: .confirmationAction) {
                Button("Save", action: save).disabled(busy || values.isEmpty || !hasChanges)
            }
        }
        .confirmationDialog("Reset every surface to the game's built-in values?", isPresented: $confirmReset, titleVisibility: .visible) {
            Button("Reset to defaults", role: .destructive, action: reset)
        }
        .refreshable { await load() }
        .task { await load() }
    }

    // MARK: Rows

    @ViewBuilder
    private func rollRow(_ group: CoursePhysics.Group) -> some View {
        let rolling = binding(group, .rolling)
        if group.isGreen {
            let stimp = Binding(get: { CoursePhysics.stimp(rolling: rolling.wrappedValue) },
                                set: { rolling.wrappedValue = CoursePhysics.rolling(stimp: $0) })
            row("Roll", value: String(format: "Stimp %.1f ft", stimp.wrappedValue), group: group, field: .rolling) {
                Slider(value: stimp, in: CoursePhysics.stimpRange, step: 0.1)
            }
        } else {
            let runs = CoursePhysics.rollDistance(speed: CoursePhysics.rollCaptionSpeed, rolling: rolling.wrappedValue)
            row("Roll", value: String(format: "%.2f · runs %.1f m", rolling.wrappedValue, runs), group: group, field: .rolling) {
                Slider(value: rolling, in: group.rollRange, step: 0.01)
            }
        }
    }

    private func sliderRow(_ title: String, group: CoursePhysics.Group, field: CoursePhysics.Field, range: ClosedRange<Double>) -> some View {
        let value = binding(group, field)
        return row(title, value: String(format: "%.2f", value.wrappedValue), group: group, field: field) {
            Slider(value: value, in: range, step: 0.01)
        }
    }

    private func row(_ title: String, value: String, group: CoursePhysics.Group, field: CoursePhysics.Field,
                     @ViewBuilder slider: () -> some View) -> some View {
        let isDefault = abs((values[group.id]?[field] ?? 0) - group.defaults[field]) < 1e-6
        return VStack(alignment: .leading) {
            HStack {
                Text(title)
                Spacer()
                Text(value).monospacedDigit().foregroundStyle(isDefault ? .secondary : .primary)
            }
            slider()
        }
    }

    private func binding(_ group: CoursePhysics.Group, _ field: CoursePhysics.Field) -> Binding<Double> {
        Binding(get: { values[group.id]?[field] ?? group.defaults[field] },
                set: { values[group.id, default: group.defaults][field] = $0 })
    }

    @ViewBuilder
    private var statusLabel: some View {
        switch status {
        case .loading: Label("Loading…", systemImage: "hourglass").foregroundStyle(.secondary)
        case .saving: Label("Saving…", systemImage: "hourglass").foregroundStyle(.secondary)
        case .ready: Label(hasChanges ? "Unsaved changes" : "Up to date", systemImage: hasChanges ? "pencil.circle" : "checkmark.circle")
                .foregroundStyle(hasChanges ? Theme.warn : .secondary)
        case .saved(let message): Label(hasChanges ? "Unsaved changes" : message, systemImage: hasChanges ? "pencil.circle" : "checkmark.circle.fill")
                .foregroundStyle(hasChanges ? Theme.warn : Theme.good)
        case .failed(let message): Label(message, systemImage: "exclamationmark.triangle.fill").foregroundStyle(Theme.warn)
        }
    }

    // MARK: Server

    private func load() async {
        guard let api else { status = .failed(GameAPI.APIError.noServer.localizedDescription); return }
        status = .loading
        do {
            show(try await api.physics(), as: .ready)
        } catch {
            status = .failed("Couldn't load: \(error.localizedDescription)")
        }
    }

    private func save() {
        run { try await $0.savePhysics(CoursePhysics.update(from: values)) }
    }

    private func reset() {
        run { try await $0.resetPhysics() }
    }

    /// Sends a change and shows what the server now has.
    private func run(_ call: @escaping (GameAPI) async throws -> CoursePhysics.Profile) {
        guard let api else { return }
        status = .saving
        Task {
            do {
                let profile = try await call(api)
                show(profile, as: .saved(simConnected ? "Saved: applies from the next shot" : "Saved: the sim picks it up when it connects"))
                Haptics.addressSet()
            } catch {
                status = .failed("Not saved: \(error.localizedDescription)")
                Haptics.problem()
            }
        }
    }

    private func show(_ profile: CoursePhysics.Profile, as newStatus: Status) {
        saved = CoursePhysics.values(from: profile)
        values = saved
        status = newStatus
    }
}
