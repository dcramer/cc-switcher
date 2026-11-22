import AppKit
import CoreServices

/// Finds, launches and talks to Claude Desktop, one process per profile.
enum Claude {
    static let bundleId = "com.anthropic.claudefordesktop"
    static let linkScheme = "claude"
    static let dataDirSwitch = "--user-data-dir="

    static func appURL(_ settings: Settings) -> URL? {
        let fm = FileManager.default
        if let custom = settings.claudePath, fm.fileExists(atPath: custom) {
            return URL(fileURLWithPath: custom)
        }
        if let found = NSWorkspace.shared.urlForApplication(withBundleIdentifier: bundleId) {
            return found
        }
        for path in ["/Applications/Claude.app", NSHomeDirectory() + "/Applications/Claude.app"] where fm.fileExists(atPath: path) {
            return URL(fileURLWithPath: path)
        }
        return nil
    }

    /// Running Claude Desktop main processes and the data folder each uses (nil for Claude's default).
    static func running() -> [(app: NSRunningApplication, dataDir: String?)] {
        NSWorkspace.shared.runningApplications
            .filter { $0.bundleIdentifier == bundleId && !$0.isTerminated }
            .map { app in
                let dir = arguments(of: app.processIdentifier)?
                    .first { $0.hasPrefix(dataDirSwitch) }
                    .map { String($0.dropFirst(dataDirSwitch.count)) }
                return (app, dir)
            }
    }

    static func runningApp(for profile: Profile) -> NSRunningApplication? {
        let want: String? = profile.isDefault ? nil : profile.dataDir.path
        return running().first { normalize($0.dataDir) == normalize(want) }?.app
    }

    private static func normalize(_ path: String?) -> String? {
        guard let path else { return nil }
        return URL(fileURLWithPath: path).standardizedFileURL.path
    }

    /// Opens Claude with a profile, or brings it forward if it's already open. On macOS Claude
    /// doesn't stop a second copy from opening the same data folder, which would damage it.
    static func launch(_ profile: Profile, settings: Settings, completion: @escaping (NSRunningApplication?, Error?) -> Void) {
        if let app = runningApp(for: profile) {
            bringToFront(app)
            completion(app, nil)
            return
        }
        guard let claude = appURL(settings) else {
            completion(nil, NSError(domain: "CCSwitcher", code: 2, userInfo: [
                NSLocalizedDescriptionKey: "Couldn't find Claude Desktop. Is it installed?",
            ]))
            return
        }
        let url = tintedCopy(of: claude, for: profile) ?? claude

        let config = NSWorkspace.OpenConfiguration()
        // A new process even though another profile's Claude is already running.
        config.createsNewApplicationInstance = true
        config.activates = true
        if !profile.isDefault {
            try? FileManager.default.createDirectory(at: profile.dataDir, withIntermediateDirectories: true)
            config.arguments = [dataDirSwitch + profile.dataDir.path]
        }
        if profile.separate {
            try? FileManager.default.createDirectory(at: profile.codeConfigDir, withIntermediateDirectories: true)
            config.environment = ["CLAUDE_CONFIG_DIR": profile.codeConfigDir.path]
        }
        NSWorkspace.shared.openApplication(at: url, configuration: config) { app, error in
            DispatchQueue.main.async { completion(app, error) }
        }
    }

    // MARK: Colored Dock icons

    /// Claude's own color: a profile in it just runs Claude as installed.
    static let claudeColor = "#D97757"

    /// A profile in another color runs from its own copy of Claude.app, made with clonefile so it
    /// takes no extra disk space, with a Finder custom icon in the profile's color. That icon is
    /// what the Dock and the app switcher show. Claude's signed files aren't touched: the custom
    /// icon lives beside them, so the copy's signature still verifies. Returns nil to use Claude as is.
    static func tintedCopy(of claude: URL, for profile: Profile) -> URL? {
        guard !profile.isDefault, profile.colorHex.caseInsensitiveCompare(claudeColor) != .orderedSame else { return nil }
        let fm = FileManager.default
        let copy = profile.folder.appendingPathComponent("Claude.app", isDirectory: true)
        let stamp = profile.folder.appendingPathComponent("icon-color")

        // Refresh the copy whenever the installed Claude is newer (it updated), but never
        // replace a copy that updated itself past it, which would downgrade the profile.
        let installed = version(of: claude)
        let current = fm.fileExists(atPath: copy.path) ? version(of: copy) : nil
        var fresh = false
        if current == nil || installed.compare(current!, options: .numeric) == .orderedDescending {
            try? fm.createDirectory(at: profile.folder, withIntermediateDirectories: true)
            try? fm.removeItem(at: copy)
            if clonefile(claude.path, copy.path, 0) != 0 {
                do { try fm.copyItem(at: claude, to: copy) } catch { return nil }
            }
            fresh = true
        }

        let applied = try? String(contentsOf: stamp, encoding: .utf8)
        if fresh || applied != profile.colorHex {
            let icon = Icons.profileIcon(profile.colorHex, claude: claude, pixels: 1024)
            if NSWorkspace.shared.setIcon(icon, forFile: copy.path, options: []) {
                try? profile.colorHex.write(to: stamp, atomically: true, encoding: .utf8)
            }
        }
        return copy
    }

    private static func version(of app: URL) -> String {
        // Read the file each time: Bundle caches Info.plist, which would hide an update.
        let plist = NSDictionary(contentsOf: app.appendingPathComponent("Contents/Info.plist"))
        return plist?["CFBundleVersion"] as? String ?? "0"
    }

    /// Like clicking its Dock icon: reopens a window if they were all closed, and activates it.
    static func bringToFront(_ app: NSRunningApplication) {
        let reopen = NSAppleEventDescriptor(
            eventClass: AEEventClass(kCoreEventClass), eventID: AEEventID(kAEReopenApplication),
            targetDescriptor: NSAppleEventDescriptor(processIdentifier: app.processIdentifier),
            returnID: AEReturnID(kAutoGenerateReturnID), transactionID: AETransactionID(kAnyTransactionID))
        _ = try? reopen.sendEvent(options: [.noReply], timeout: 5)
        app.activate(options: [.activateAllWindows])
    }

    /// Hands a claude:// link to one specific Claude process; macOS would otherwise pick one.
    static func open(_ link: URL, in app: NSRunningApplication) throws {
        let event = NSAppleEventDescriptor(
            eventClass: AEEventClass(kInternetEventClass), eventID: AEEventID(kAEGetURL),
            targetDescriptor: NSAppleEventDescriptor(processIdentifier: app.processIdentifier),
            returnID: AEReturnID(kAutoGenerateReturnID), transactionID: AETransactionID(kAnyTransactionID))
        event.setParam(NSAppleEventDescriptor(string: link.absoluteString), forKeyword: keyDirectObject)
        _ = try event.sendEvent(options: [.noReply], timeout: 10)
        app.activate(options: [.activateAllWindows])
    }

    // MARK: claude:// handler

    @discardableResult
    static func setLinkHandler(_ bundleId: String) -> Bool {
        LSSetDefaultHandlerForURLScheme(linkScheme as CFString, bundleId as CFString) == noErr
    }

    /// Claude makes itself the claude:// handler every time it starts, so links are taken back
    /// once it has settled. A detached copy of this executable does it, so it still happens if
    /// CC Switcher quits first.
    static func claimLinksLater(seconds: Int = 15) {
        guard let exe = Bundle.main.executableURL else { return }
        let process = Process()
        process.executableURL = exe
        process.arguments = ["--claim-links-after", String(seconds)]
        process.standardOutput = FileHandle.nullDevice
        process.standardError = FileHandle.nullDevice
        try? process.run()
    }

    // MARK: Process arguments

    /// argv of another process, via sysctl(KERN_PROCARGS2).
    static func arguments(of pid: pid_t) -> [String]? {
        var mib: [Int32] = [CTL_KERN, KERN_PROCARGS2, pid]
        var size = 0
        guard sysctl(&mib, 3, nil, &size, nil, 0) == 0, size > 4 else { return nil }
        var buffer = [UInt8](repeating: 0, count: size)
        guard sysctl(&mib, 3, &buffer, &size, nil, 0) == 0 else { return nil }

        let argc = buffer.withUnsafeBytes { $0.load(as: Int32.self) }
        var i = 4
        while i < size && buffer[i] != 0 { i += 1 } // executable path
        while i < size && buffer[i] == 0 { i += 1 } // padding
        var args: [String] = []
        while args.count < argc && i < size {
            let start = i
            while i < size && buffer[i] != 0 { i += 1 }
            args.append(String(decoding: buffer[start..<i], as: UTF8.self))
            i += 1
        }
        return args
    }
}
