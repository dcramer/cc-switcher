import AppKit
import Combine

/// The app's state: settings plus which profiles are running, kept current as Claude starts and quits.
final class Store: ObservableObject {
    @Published private(set) var settings: Settings
    @Published private(set) var runningIds: Set<String> = []
    @Published private(set) var newIds: Set<String> = []
    @Published var selection: String?

    static var ownBundleId: String { Bundle.main.bundleIdentifier ?? "com.cc-switcher.app" }

    private var observers: [NSObjectProtocol] = []
    private var timer: Timer?

    init(settings: Settings) {
        self.settings = settings
        selection = settings.lastProfileId.flatMap { id in settings.profiles.first { $0.id == id }?.id }
            ?? settings.profiles.first?.id
        refresh()

        let center = NSWorkspace.shared.notificationCenter
        for name in [NSWorkspace.didLaunchApplicationNotification, NSWorkspace.didTerminateApplicationNotification] {
            observers.append(center.addObserver(forName: name, object: nil, queue: .main) { [weak self] _ in
                // Arguments can lag the launch notification slightly.
                self?.refresh()
                DispatchQueue.main.asyncAfter(deadline: .now() + 1) { self?.refresh() }
            })
        }
        // Picks up a profile going from New to signed in.
        timer = Timer.scheduledTimer(withTimeInterval: 3, repeats: true) { [weak self] _ in self?.refresh() }
    }

    var profiles: [Profile] { settings.profiles }
    var claudeURL: URL? { Claude.appURL(settings) }
    var selected: Profile? { profiles.first { $0.id == selection } }
    var routeLinks: Bool { settings.routeLinks ?? false }
    var closeAfterLaunch: Bool { settings.closeAfterLaunch ?? false }

    func refresh() {
        let running = Set(profiles.filter { Claude.runningApp(for: $0) != nil }.map(\.id))
        if running != runningIds { runningIds = running }
        let fresh = Set(profiles.filter(\.isNew).map(\.id))
        if fresh != newIds { newIds = fresh }
    }

    func isRunning(_ profile: Profile) -> Bool { runningIds.contains(profile.id) }

    private func mutate(_ change: (inout Settings) -> Void) throws {
        var copy = settings
        change(&copy)
        try copy.save()
        settings = copy
        refresh()
    }

    // MARK: Actions

    func launch(_ profile: Profile, completion: ((NSRunningApplication?) -> Void)? = nil) {
        try? mutate { $0.lastProfileId = profile.id }
        let wasRunning = isRunning(profile)
        Claude.launch(profile, settings: settings) { [weak self] app, error in
            if let error {
                Store.showError("Couldn't open \(profile.name)", error.localizedDescription)
            } else if !wasRunning, self?.routeLinks == true {
                Claude.claimLinksLater()
            }
            self?.refresh()
            completion?(app)
        }
    }

    func add(name: String, color: String, separate: Bool) throws {
        let id = settings.uniqueId(for: name)
        try mutate { $0.profiles.append(Profile(id: id, name: name, color: color, usesDefaultData: false, separateCodeConfig: separate)) }
        selection = id
    }

    func update(_ profile: Profile) throws {
        try mutate { settings in
            if let i = settings.profiles.firstIndex(where: { $0.id == profile.id }) { settings.profiles[i] = profile }
        }
    }

    func remove(_ profile: Profile) throws {
        if FileManager.default.fileExists(atPath: profile.folder.path) {
            try FileManager.default.trashItem(at: profile.folder, resultingItemURL: nil)
        }
        try mutate { $0.profiles.removeAll { $0.id == profile.id } }
        selection = profiles.first?.id
    }

    func setRouteLinks(_ on: Bool) {
        try? mutate { $0.routeLinks = on }
        Claude.setLinkHandler(on ? Store.ownBundleId : Claude.bundleId)
    }

    func setCloseAfterLaunch(_ on: Bool) {
        try? mutate { $0.closeAfterLaunch = on }
    }

    static func showError(_ title: String, _ detail: String) {
        let alert = NSAlert()
        alert.alertStyle = .warning
        alert.messageText = title
        alert.informativeText = detail
        alert.runModal()
    }
}
