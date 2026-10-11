# CC Switcher

A small Windows app for running Claude Desktop under several accounts at once, for example
work and personal. Each profile is a name; pick one and Claude opens signed in as that account,
alongside any other profiles that are already open.

Unofficial, and not affiliated with Anthropic.

## Installing

Download `cc-switcher-setup.exe` from the
[latest release](https://github.com/dcramer/cc-switcher/releases/latest) and run it. It installs
for your user only (no admin prompt), needs nothing else installed, and adds **CC Switcher** to
the Start menu. The installer isn't code-signed, so Windows SmartScreen will ask you to confirm
with **More info → Run anyway**.

## Using it

Open **CC Switcher** from the Start menu. It starts with two profiles:

- **Personal** is Claude exactly as it is today: your existing sign-in and data.
- **Work** is new. Launch it and sign in with the other account.

**Add…** creates another profile from just a name and a color. **Edit…** renames one.
**Remove** deletes a profile and moves its sign-in data to the Recycle Bin (the profile holding
your original sign-in can be renamed but not removed). **Shortcut** puts a "Claude (Work)" icon
on the desktop or Start menu that opens that profile directly.

### Telling profiles apart

A profile's color is how you recognize its Claude windows. Each one gets Claude's icon tinted to
that color, its own button on the taskbar instead of a place in Claude's stack, and a thin
border in the same color (Windows 11). A profile left in the first color, Claude's own clay,
looks exactly like Claude always has.

To keep this up, CC Switcher stays running in the notification area after you close its window.
Right-click its icon there to launch a profile, or click it to bring the window back. **Exit**
in that menu closes it for real and returns any open Claude windows to their normal look.

### Signing in to a second profile

Google, SSO and emailed-link sign-in all finish by having your browser open a `claude://` link,
and Windows hands that link to Claude's default profile no matter which window asked. To sign in
to any other profile, click **Set up…** at the bottom of the window once. CC Switcher registers
itself as a handler for `claude://` links and opens Windows Settings, where you set the
**CLAUDE** link type to CC Switcher. From then on it passes each link to the right profile,
asking which one when more than one is open. **Turn off** undoes it.

## How it works

Claude Desktop keeps one account per data folder and allows one running instance per data
folder. CC Switcher starts Claude with Chromium's `--user-data-dir` switch pointed at a folder
it manages for the profile. (Claude's signed builds ignore the `CLAUDE_USER_DATA_DIR`
environment variable, so the switch is the only way in.)

Everything lives in `%USERPROFILE%\.cc-switcher`:

| Path | Contents |
| --- | --- |
| `settings.json` | The profile list |
| `profiles\<id>\data` | A profile's Claude sign-in and local data |
| `profiles\<id>\claude-code` | Its Claude Code config, if you asked for a separate one |
| `icons\` | Each profile's tinted icon, for its shortcuts and windows |

This is deliberately not under `AppData`. Claude Desktop is an MSIX package, and Windows
redirects a packaged app's `AppData` writes into the package's private cache: a profile stored
there would be invisible to CC Switcher and erased whenever Claude is reinstalled. Set
`CC_SWITCHER_HOME` to use a different folder.

Claude has no option for a window's icon or color, so CC Switcher sets them on Claude's windows
from outside once they open: a taskbar ID of their own, a window icon and a border color. The
icon is made from the one inside your installed Claude, so it follows Claude's artwork. Windows
drops a window icon when the program that set it exits, which is why CC Switcher stays running.

Profiles always have separate accounts, but Claude Code sessions started from any of them share
the `.claude` folder in your user folder (settings, history, memory). Tick **Also separate
Claude Code settings and history** on a profile to give it its own via `CLAUDE_CONFIG_DIR`.

## Command line

```
cc-switcher                     Open the profile list
cc-switcher --profile <name>    Open Claude with that profile
cc-switcher --url <claude://…>  Open a Claude link in a profile
cc-switcher --tray              Start in the notification area
```

## Building

Requires the .NET 9 SDK on Windows. A local build is small and uses the installed .NET Desktop
Runtime (9 or later); release builds bundle the runtime instead.

```
powershell -ExecutionPolicy Bypass -File build.ps1 -Install
```

This publishes `dist\cc-switcher.exe`, copies it to `%LOCALAPPDATA%\Programs\cc-switcher` and
adds the Start menu entry. Without `-Install` it only builds. `tools\make-icon.ps1` regenerates
the app icon.

### Releases

`.github/workflows/build.yml` builds every push and pull request and attaches the installer to
the workflow run. Every push to `main` is also published as a GitHub release, versioned
`0.1.<run number>`. The installer bundles the .NET runtime and is built from
`installer\cc-switcher.iss` with [Inno Setup](https://jrsoftware.org/isinfo.php).

## Limitations

- `--user-data-dir` is a Chromium switch that Claude doesn't document. A future Claude version
  could stop honoring it.
- Claude draws its own title bar, which can't be recolored from outside; the border is the only
  color on the window itself.
- A profile's windows only look different while CC Switcher is running. One opened while it
  isn't looks like plain Claude until CC Switcher next starts.
- Claude re-registers itself for `claude://` links each time it starts, which is why link
  routing has to be chosen in Windows Settings rather than set silently.

## License

MIT. See [LICENSE](LICENSE).
