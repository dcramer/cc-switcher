import SwiftUI

struct MainView: View {
    @ObservedObject var store: Store
    var onLaunched: () -> Void

    @State private var editing: EditTarget?

    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            List(selection: $store.selection) {
                ForEach(store.profiles) { profile in
                    ProfileRow(profile: profile, icon: Icons.profileIcon(profile.colorHex, claude: store.claudeURL),
                               running: store.isRunning(profile), isNew: store.newIds.contains(profile.id))
                        .tag(profile.id)
                }
            }
            .contextMenu(forSelectionType: String.self) { ids in
                if let profile = ids.first.flatMap(profile(withId:)) {
                    Button("Launch") { launch(profile) }
                    Button("Edit…") { editing = .edit(profile) }
                    Divider()
                    Button("Remove…") { remove(profile) }.disabled(profile.isDefault)
                }
            } primaryAction: { ids in
                if let profile = ids.first.flatMap(profile(withId:)) { launch(profile) }
            }
            .listStyle(.bordered(alternatesRowBackgrounds: false))
            .frame(minHeight: 220)

            HStack {
                Button("Launch") { store.selected.map(launch) }
                    .keyboardShortcut(.defaultAction)
                    .disabled(store.selected == nil)
                Button("Add…") { editing = .add }
                Button("Edit…") { store.selected.map { editing = .edit($0) } }
                    .disabled(store.selected == nil)
                Button("Remove") { store.selected.map(remove) }
                    .disabled(store.selected?.isDefault ?? true)
                Spacer()
            }

            HStack {
                Text(store.routeLinks
                     ? "Sign-in links are routed to the profile you choose."
                     : "Sign-in links go to whichever Claude macOS picks.")
                    .foregroundStyle(.secondary)
                Spacer()
                Button(store.routeLinks ? "Turn off" : "Turn on") { store.setRouteLinks(!store.routeLinks) }
            }

            Toggle("Close this window after launching", isOn: Binding(
                get: { store.closeAfterLaunch }, set: { store.setCloseAfterLaunch($0) }))
        }
        .padding(18)
        .frame(minWidth: 520, minHeight: 400)
        .sheet(item: $editing) { target in
            ProfileEditor(store: store, target: target) { editing = nil }
        }
    }

    private func profile(withId id: String) -> Profile? { store.profiles.first { $0.id == id } }

    private func launch(_ profile: Profile) {
        store.launch(profile)
        if store.closeAfterLaunch { onLaunched() }
    }

    private func remove(_ profile: Profile) {
        guard !profile.isDefault else { return }
        if store.isRunning(profile) {
            Store.showError("Quit \(profile.name)'s Claude first", "Its data can't be moved while Claude is using it.")
            return
        }
        let alert = NSAlert()
        alert.messageText = "Remove the \"\(profile.name)\" profile?"
        alert.informativeText = "Its sign-in and local data will be moved to the Trash. Your Claude account itself isn't affected."
        alert.addButton(withTitle: "Remove")
        alert.addButton(withTitle: "Cancel")
        alert.buttons[0].hasDestructiveAction = true
        guard alert.runModal() == .alertFirstButtonReturn else { return }
        do {
            try store.remove(profile)
        } catch {
            Store.showError("Couldn't remove \(profile.name)", error.localizedDescription)
        }
    }
}

struct ProfileRow: View {
    let profile: Profile
    let icon: NSImage
    let running: Bool
    let isNew: Bool

    var body: some View {
        HStack(spacing: 14) {
            Image(nsImage: icon)
                .resizable()
                .frame(width: 40, height: 40)
            Text(profile.name)
                .font(.system(size: 14))
            Spacer()
            if running {
                Text("Running").foregroundStyle(.green)
            } else if isNew {
                Text("New").foregroundStyle(.secondary)
            }
        }
        .padding(.vertical, 4)
        .contentShape(Rectangle())
    }
}

enum EditTarget: Identifiable {
    case add
    case edit(Profile)

    var id: String {
        switch self {
        case .add: return "+new"
        case .edit(let p): return p.id
        }
    }
}

struct ProfileEditor: View {
    @ObservedObject var store: Store
    let target: EditTarget
    var dismiss: () -> Void

    @State private var name = ""
    @State private var color = Palette.colors[1].hex
    @State private var separate = false
    @State private var problem: String?

    var body: some View {
        VStack(alignment: .leading, spacing: 16) {
            Text(isNewProfile ? "New profile" : "Edit profile").font(.headline)

            Grid(alignment: .leading, horizontalSpacing: 12, verticalSpacing: 12) {
                GridRow {
                    Text("Name")
                    TextField("", text: $name).frame(width: 260)
                }
                GridRow {
                    Text("Color")
                    HStack(spacing: 8) {
                        ForEach(Palette.colors, id: \.hex) { swatch in
                            Circle()
                                .fill(Color(nsColor: Icons.color(swatch.hex)))
                                .frame(width: 22, height: 22)
                                .overlay(Circle().stroke(Color.primary, lineWidth: color == swatch.hex ? 2 : 0).padding(-3))
                                .help(swatch.name)
                                .onTapGesture { color = swatch.hex }
                        }
                    }
                }
            }

            VStack(alignment: .leading, spacing: 4) {
                Toggle("Also separate Claude Code settings and history", isOn: $separate)
                Text("Each profile always has its own account. This also gives it its own Claude Code config folder instead of sharing ~/.claude.")
                    .font(.caption)
                    .foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
            }

            if let problem {
                Text(problem).foregroundStyle(.red)
            }

            HStack {
                Spacer()
                Button("Cancel", action: dismiss).keyboardShortcut(.cancelAction)
                Button("OK", action: save).keyboardShortcut(.defaultAction)
            }
        }
        .padding(20)
        .frame(width: 420)
        .onAppear(perform: load)
    }

    private var isNewProfile: Bool {
        if case .add = target { return true }
        return false
    }

    private func load() {
        switch target {
        case .add:
            color = Palette.colors[store.profiles.count % Palette.colors.count].hex
        case .edit(let p):
            name = p.name
            color = p.colorHex
            separate = p.separate
        }
    }

    private func save() {
        let trimmed = name.trimmingCharacters(in: .whitespacesAndNewlines)
        let existingId: String? = { if case .edit(let p) = target { return p.id }; return nil }()
        if trimmed.isEmpty {
            problem = "Give the profile a name."
            return
        }
        if store.profiles.contains(where: { $0.name.caseInsensitiveCompare(trimmed) == .orderedSame && $0.id != existingId }) {
            problem = "There's already a profile named \"\(trimmed)\"."
            return
        }
        do {
            switch target {
            case .add:
                try store.add(name: trimmed, color: color, separate: separate)
            case .edit(var p):
                p.name = trimmed
                p.color = color
                p.separateCodeConfig = separate
                try store.update(p)
            }
            dismiss()
        } catch {
            problem = error.localizedDescription
        }
    }
}

/// Asks which profile a claude:// link is for when more than one could have asked for it.
struct LinkPicker: View {
    @ObservedObject var store: Store
    let kind: String
    var choose: (Profile?) -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text("Open this \(kind) in which profile?").font(.headline)
            Text("If you're signing in, pick the profile whose window you started from.")
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
            VStack(spacing: 6) {
                ForEach(ordered) { profile in
                    Button { choose(profile) } label: {
                        HStack(spacing: 10) {
                            Image(nsImage: Icons.profileIcon(profile.colorHex, claude: store.claudeURL))
                                .resizable().frame(width: 24, height: 24)
                            Text(profile.name)
                            Spacer()
                            if store.isRunning(profile) { Text("running").foregroundStyle(.secondary) }
                        }
                        .frame(maxWidth: .infinity)
                        .padding(.vertical, 2)
                    }
                }
            }
            HStack {
                Spacer()
                Button("Cancel") { choose(nil) }.keyboardShortcut(.cancelAction)
            }
        }
        .padding(18)
        .frame(width: 360)
    }

    /// Running profiles first: the link is almost always for one of them.
    private var ordered: [Profile] {
        store.profiles.filter { store.isRunning($0) } + store.profiles.filter { !store.isRunning($0) }
    }
}
