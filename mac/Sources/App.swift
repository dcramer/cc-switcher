import AppKit
import SwiftUI
import os

let linkLog = Logger(subsystem: "com.cc-switcher.app", category: "links")

/// A link with its query left out: OAuth callbacks carry codes.
func redacted(_ url: URL) -> String { "\(url.scheme ?? "")://\(url.host ?? "")\(url.path)" }

final class AppDelegate: NSObject, NSApplicationDelegate, NSWindowDelegate {
    private var store: Store!
    private var mainWindow: NSWindow?
    private var pickerWindows: [NSWindow] = []
    /// Started by macOS just to deliver a link: quit once it's delivered instead of showing the list.
    private var startedForLink = false
    private var finishedLaunching = false

    func applicationDidFinishLaunching(_ notification: Notification) {
        do {
            store = Store(settings: try Settings.load())
        } catch {
            Store.showError("CC Switcher can't start", error.localizedDescription)
            NSApp.terminate(nil)
            return
        }
        buildMenu()
        if store.routeLinks { Claude.setLinkHandler(Store.ownBundleId) }

        // A link that launched us arrives just after this; give it a moment before showing the list.
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.4) { [self] in
            finishedLaunching = true
            if !startedForLink { showMainWindow() }
        }
    }

    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool {
        showMainWindow()
        return true
    }

    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { true }

    func applicationDidBecomeActive(_ notification: Notification) {
        store?.refresh()
    }

    // MARK: Main window

    func showMainWindow() {
        startedForLink = false
        if mainWindow == nil {
            let window = NSWindow(
                contentRect: NSRect(x: 0, y: 0, width: 560, height: 440),
                styleMask: [.titled, .closable, .miniaturizable, .resizable],
                backing: .buffered, defer: false)
            window.title = "CC Switcher"
            window.isReleasedWhenClosed = false
            window.contentView = NSHostingView(rootView: MainView(store: store) { [weak self] in
                self?.mainWindow?.close()
            })
            window.setFrameAutosaveName("CCSwitcherMain")
            if !window.setFrameUsingName("CCSwitcherMain") { window.center() }
            window.delegate = self
            mainWindow = window
        }
        store.refresh()
        mainWindow?.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true)
    }

    // MARK: Links

    func application(_ application: NSApplication, open urls: [URL]) {
        linkLog.info("open urls: \(urls.map(redacted), privacy: .public) finishedLaunching=\(self.finishedLaunching, privacy: .public)")
        if !finishedLaunching && mainWindow == nil { startedForLink = true }
        for url in urls where url.scheme?.lowercased() == Claude.linkScheme {
            route(url)
        }
    }

    private func route(_ url: URL) {
        store.refresh()
        let running = store.profiles.filter { store.isRunning($0) }
        let owner = LinkOwner.owner(of: url, among: store.profiles)
        linkLog.info("route \(redacted(url), privacy: .public): running=\(running.map(\.id), privacy: .public) owner=\(owner?.id ?? "unknown", privacy: .public)")
        if let owner, store.isRunning(owner) || !LinkOwner.isMcpSignIn(url) {
            // A sign-in only completes in the Claude that started it, so never start one for it.
            deliver(url, to: owner)
        } else if running.count == 1 {
            deliver(url, to: running[0])
        } else {
            pick(for: url)
        }
    }

    private func pick(for url: URL) {
        let kind = url.host == "login" || url.path.contains("auth") || url.path.contains("login") ? "sign-in link" : "Claude link"
        var window: NSWindow!
        let picker = LinkPicker(store: store, kind: kind) { [weak self] profile in
            window.close()
            self?.pickerWindows.removeAll { $0 === window }
            linkLog.info("picker chose \(profile?.id ?? "cancel", privacy: .public)")
            if let profile { self?.deliver(url, to: profile) } else { self?.quitIfOnlyForLink() }
        }
        window = NSPanel(contentRect: .zero, styleMask: [.titled, .closable], backing: .buffered, defer: false)
        window.title = "CC Switcher"
        window.isReleasedWhenClosed = false
        window.level = .floating
        window.contentView = NSHostingView(rootView: picker)
        window.center()
        pickerWindows.append(window)
        window.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true)
    }

    private func deliver(_ url: URL, to profile: Profile) {
        let wasRunning = store.isRunning(profile)
        linkLog.info("deliver \(redacted(url), privacy: .public) to \(profile.id, privacy: .public) wasRunning=\(wasRunning, privacy: .public)")
        store.launch(profile) { [weak self] app in
            guard let app else { linkLog.error("deliver: no Claude process for \(profile.id, privacy: .public)"); self?.quitIfOnlyForLink(); return }
            // A Claude that has just started needs a moment before it handles links.
            DispatchQueue.main.asyncAfter(deadline: .now() + (wasRunning ? 0 : 4)) {
                do {
                    try Claude.open(url, in: app)
                    linkLog.info("sent link to pid \(app.processIdentifier, privacy: .public)")
                } catch {
                    linkLog.error("sending link to pid \(app.processIdentifier, privacy: .public) failed: \(error.localizedDescription, privacy: .public)")
                    Store.showError(
                        "Couldn't send the link to \(profile.name)",
                        "\(error.localizedDescription)\n\nIf macOS asked whether CC Switcher may control Claude, allow it in System Settings → Privacy & Security → Automation.")
                }
                self?.quitIfOnlyForLink()
            }
        }
    }

    private func quitIfOnlyForLink() {
        guard startedForLink, mainWindow?.isVisible != true, pickerWindows.isEmpty else { return }
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.5) { NSApp.terminate(nil) }
    }

    // MARK: Menu

    private func buildMenu() {
        let main = NSMenu()

        let appItem = NSMenuItem()
        let appMenu = NSMenu()
        appMenu.addItem(withTitle: "About CC Switcher", action: #selector(NSApplication.orderFrontStandardAboutPanel(_:)), keyEquivalent: "")
        appMenu.addItem(.separator())
        appMenu.addItem(withTitle: "Hide CC Switcher", action: #selector(NSApplication.hide(_:)), keyEquivalent: "h")
        appMenu.addItem(withTitle: "Quit CC Switcher", action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q")
        appItem.submenu = appMenu
        main.addItem(appItem)

        let editItem = NSMenuItem()
        let editMenu = NSMenu(title: "Edit")
        editMenu.addItem(withTitle: "Cut", action: #selector(NSText.cut(_:)), keyEquivalent: "x")
        editMenu.addItem(withTitle: "Copy", action: #selector(NSText.copy(_:)), keyEquivalent: "c")
        editMenu.addItem(withTitle: "Paste", action: #selector(NSText.paste(_:)), keyEquivalent: "v")
        editMenu.addItem(withTitle: "Select All", action: #selector(NSText.selectAll(_:)), keyEquivalent: "a")
        editItem.submenu = editMenu
        main.addItem(editItem)

        let windowItem = NSMenuItem()
        let windowMenu = NSMenu(title: "Window")
        windowMenu.addItem(withTitle: "Close", action: #selector(NSWindow.performClose(_:)), keyEquivalent: "w")
        windowMenu.addItem(withTitle: "Minimize", action: #selector(NSWindow.performMiniaturize(_:)), keyEquivalent: "m")
        windowItem.submenu = windowMenu
        main.addItem(windowItem)
        NSApp.windowsMenu = windowMenu

        NSApp.mainMenu = main
    }
}
