import Foundation

/// Same file and format as the Windows version: ~/.cc-switcher/settings.json.
struct Profile: Codable, Identifiable, Equatable {
    /// Stable; names the profile's folder, so it doesn't change on rename.
    var id: String
    var name: String
    var color: String?
    /// True for the one profile that is Claude's own default data folder, i.e. the existing sign-in.
    var usesDefaultData: Bool?
    /// Give Claude Code sessions started from this profile their own CLAUDE_CONFIG_DIR.
    var separateCodeConfig: Bool?

    var isDefault: Bool { usesDefaultData ?? false }
    var separate: Bool { separateCodeConfig ?? false }
    var colorHex: String { color ?? Palette.colors[0].hex }

    var folder: URL { Settings.profilesRoot.appendingPathComponent(id, isDirectory: true) }
    var dataDir: URL { folder.appendingPathComponent("data", isDirectory: true) }
    var codeConfigDir: URL { folder.appendingPathComponent("claude-code", isDirectory: true) }

    /// Never signed in: its data folder hasn't got a cookie jar yet.
    var isNew: Bool {
        !isDefault && !FileManager.default.fileExists(atPath: dataDir.appendingPathComponent("Cookies").path)
    }
}

enum Palette {
    static let colors: [(name: String, hex: String)] = [
        ("Clay", "#D97757"), ("Blue", "#6A9BCC"), ("Green", "#7A9B63"), ("Purple", "#9A84D0"),
        ("Amber", "#D9A441"), ("Rose", "#D4698C"), ("Teal", "#4FA8A0"), ("Slate", "#7D8794"),
    ]
}

struct Settings: Codable {
    /// Overrides auto-detection of Claude Desktop.
    var claudePath: String?
    var closeAfterLaunch: Bool?
    var lastProfileId: String?
    /// macOS: send claude:// links through CC Switcher so they reach the right profile.
    var routeLinks: Bool?
    var profiles: [Profile]

    static var homeDir: URL {
        if let home = ProcessInfo.processInfo.environment["CC_SWITCHER_HOME"], !home.isEmpty {
            return URL(fileURLWithPath: home, isDirectory: true)
        }
        return FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent(".cc-switcher", isDirectory: true)
    }

    static var filePath: URL { homeDir.appendingPathComponent("settings.json") }
    static var profilesRoot: URL { homeDir.appendingPathComponent("profiles", isDirectory: true) }

    static func initial() -> Settings {
        Settings(profiles: [
            Profile(id: "personal", name: "Personal", color: "#D97757", usesDefaultData: true, separateCodeConfig: false),
            Profile(id: "work", name: "Work", color: "#6A9BCC", usesDefaultData: false, separateCodeConfig: false),
        ])
    }

    static func load() throws -> Settings {
        guard FileManager.default.fileExists(atPath: filePath.path) else {
            let settings = initial()
            try settings.save()
            return settings
        }
        do {
            return try JSONDecoder().decode(Settings.self, from: Data(contentsOf: filePath))
        } catch {
            throw NSError(domain: "CCSwitcher", code: 1, userInfo: [
                NSLocalizedDescriptionKey: "Couldn't read \(filePath.path): \(error.localizedDescription)\n\nFix or delete the file and try again.",
            ])
        }
    }

    func save() throws {
        try FileManager.default.createDirectory(at: Settings.homeDir, withIntermediateDirectories: true)
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
        try encoder.encode(self).write(to: Settings.filePath, options: .atomic)
    }

    func find(_ idOrName: String) -> Profile? {
        profiles.first { $0.id.caseInsensitiveCompare(idOrName) == .orderedSame }
            ?? profiles.first { $0.name.caseInsensitiveCompare(idOrName) == .orderedSame }
    }

    /// Also avoids folders left on disk, so a new profile never inherits an old one's sign-in.
    func uniqueId(for name: String) -> String {
        var slug = ""
        for c in name.lowercased() {
            if c.isASCII && (c.isLetter || c.isNumber) {
                slug.append(c)
            } else if !slug.isEmpty && slug.last != "-" {
                slug.append("-")
            }
        }
        while slug.hasSuffix("-") { slug.removeLast() }
        if slug.isEmpty { slug = "profile" }

        var id = slug
        var n = 2
        while profiles.contains(where: { $0.id == id })
            || FileManager.default.fileExists(atPath: Settings.profilesRoot.appendingPathComponent(id).path) {
            id = "\(slug)-\(n)"
            n += 1
        }
        return id
    }
}
