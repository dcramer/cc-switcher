import Foundation

/// Works out which profile a claude:// link belongs to, so it can go straight there instead of
/// asking. Returns nil when it can't tell; the caller then asks.
enum LinkOwner {
    /// Code and Cowork session ids, as they appear in links and in Claude's log.
    private static let sessionPattern = try! NSRegularExpression(
        pattern: #"local_[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}"#)

    /// How long after starting an MCP sign-in its callback is still matched to it.
    static let signInWindow: TimeInterval = 15 * 60

    static func owner(of url: URL, among profiles: [Profile]) -> Profile? {
        if isMcpSignIn(url) {
            // The callback only says which sign-in it answers (its state), not which Claude
            // started it. Claude logs the session that started each MCP sign-in, though, and a
            // session lives in exactly one profile's data folder.
            guard let session = lastMcpSignInSession() else { return nil }
            return profile(owning: session, among: profiles)
        }
        // Links that open a session (from notifications, Inbox and so on) name it.
        for session in sessionIds(in: url.absoluteString) {
            if let profile = profile(owning: session, among: profiles) { return profile }
        }
        return nil
    }

    static func isMcpSignIn(_ url: URL) -> Bool {
        url.path.hasPrefix("/mcp-auth-callback/")
    }

    static func sessionIds(in text: String) -> [String] {
        let range = NSRange(text.startIndex..., in: text)
        return sessionPattern.matches(in: text, range: range).compactMap {
            Range($0.range, in: text).map { String(text[$0]) }
        }
    }

    // MARK: Session folders

    /// Claude's data folder for a profile: its own, or Claude's default for the default profile.
    static func dataDir(of profile: Profile) -> URL {
        profile.isDefault
            ? FileManager.default.homeDirectoryForCurrentUser
                .appendingPathComponent("Library/Application Support/Claude", isDirectory: true)
            : profile.dataDir
    }

    static func profile(owning session: String, among profiles: [Profile]) -> Profile? {
        profiles.first { owns($0, session) }
    }

    /// Code sessions are stored as claude-code-sessions/<account>/<org>/<id>.json and Cowork
    /// ones as local-agent-mode-sessions/<account>/<org>/<id>.json.
    private static func owns(_ profile: Profile, _ session: String) -> Bool {
        let fm = FileManager.default
        let root = dataDir(of: profile)
        for kind in ["claude-code-sessions", "local-agent-mode-sessions"] {
            let base = root.appendingPathComponent(kind, isDirectory: true)
            for account in (try? fm.contentsOfDirectory(atPath: base.path)) ?? [] {
                let accountDir = base.appendingPathComponent(account, isDirectory: true)
                for org in (try? fm.contentsOfDirectory(atPath: accountDir.path)) ?? [] {
                    let file = accountDir.appendingPathComponent(org).appendingPathComponent(session + ".json")
                    if fm.fileExists(atPath: file.path) { return true }
                }
            }
        }
        return false
    }

    // MARK: Claude's log

    /// Every profile's Claude writes to the same log (it doesn't follow --user-data-dir).
    static var logFile: URL {
        FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent("Library/Logs/Claude/main.log")
    }

    /// Lines Claude logs when a Code or Cowork session starts an MCP sign-in.
    private static let signInStart = try! NSRegularExpression(pattern:
        #"^(\d{4}-\d\d-\d\d \d\d:\d\d:\d\d) .*(?:LocalSessions\.mcpAuthenticate(?:ConfigServer)?|LocalSessions\.mcpHostSignInStart|LocalAgentModeSessions\.mcpAuthenticate): sessionId="?(local_[0-9a-f-]{36})"#,
        options: [.anchorsMatchLines])

    /// The session behind the most recent MCP sign-in, if one started recently.
    static func lastMcpSignInSession(now: Date = Date()) -> String? {
        guard let text = tail(of: logFile, bytes: 1 << 20) else { return nil }
        let range = NSRange(text.startIndex..., in: text)
        guard let match = signInStart.matches(in: text, range: range).last,
              let stamp = Range(match.range(at: 1), in: text),
              let session = Range(match.range(at: 2), in: text) else { return nil }

        let format = DateFormatter()
        format.locale = Locale(identifier: "en_US_POSIX")
        format.dateFormat = "yyyy-MM-dd HH:mm:ss"
        guard let started = format.date(from: String(text[stamp])),
              now.timeIntervalSince(started) < signInWindow else { return nil }
        return String(text[session])
    }

    private static func tail(of file: URL, bytes: Int) -> String? {
        guard let handle = try? FileHandle(forReadingFrom: file) else { return nil }
        defer { try? handle.close() }
        guard let size = try? handle.seekToEnd() else { return nil }
        try? handle.seek(toOffset: size > UInt64(bytes) ? size - UInt64(bytes) : 0)
        guard let data = try? handle.readToEnd() else { return nil }
        return String(decoding: data, as: UTF8.self)
    }
}
