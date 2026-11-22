import AppKit

let args = Array(CommandLine.arguments.dropFirst())

func fail(_ message: String) -> Never {
    FileHandle.standardError.write(Data("cc-switcher: \(message)\n".utf8))
    exit(1)
}

func loadSettings() -> Settings {
    do { return try Settings.load() } catch { fail(error.localizedDescription) }
}

// Command-line use, as with the Windows version: `cc-switcher --profile work`.
switch args.first {
case "--claim-links-after":
    // Helper: wait for a just-launched Claude to claim claude:// links, then take them back.
    sleep(args.count > 1 ? UInt32(args[1]) ?? 15 : 15)
    if loadSettings().routeLinks == true { Claude.setLinkHandler(Store.ownBundleId) }
    exit(0)

case "--profile", "launch":
    guard args.count > 1 else { fail("usage: cc-switcher --profile <name>") }
    var settings = loadSettings()
    guard let profile = settings.find(args[1]) else { fail("no profile named '\(args[1])'") }
    let wasRunning = Claude.runningApp(for: profile) != nil
    settings.lastProfileId = profile.id
    try? settings.save()
    Claude.launch(profile, settings: settings) { _, error in
        if let error { fail(error.localizedDescription) }
        if !wasRunning && settings.routeLinks == true { Claude.claimLinksLater() }
        exit(0)
    }
    dispatchMain()

case "owner":
    // Which profile a claude:// link would go to: `cc-switcher owner <link>`.
    guard args.count > 1, let url = URL(string: args[1]) else { fail("usage: cc-switcher owner <claude://…>") }
    print(LinkOwner.owner(of: url, among: loadSettings().profiles)?.id ?? "unknown (CC Switcher would ask)")
    exit(0)

case "list":
    for profile in loadSettings().profiles {
        let state = Claude.runningApp(for: profile) != nil ? "running" : profile.isNew ? "new" : ""
        print(profile.id.padding(toLength: 14, withPad: " ", startingAt: 0)
              + profile.name.padding(toLength: 18, withPad: " ", startingAt: 0) + state)
    }
    exit(0)

case "--help", "-h", "help":
    print("""
    cc-switcher                     Open the profile list
    cc-switcher --profile <name>    Open Claude with that profile (or bring it forward)
    cc-switcher list                Show profiles and which are running
    cc-switcher owner <claude://…>  Show which profile a link would open in
    """)
    exit(0)

default:
    break
}

let app = NSApplication.shared
let delegate = AppDelegate()
app.delegate = delegate
app.setActivationPolicy(.regular)
app.run()
