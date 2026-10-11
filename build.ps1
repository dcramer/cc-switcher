# Builds CC Switcher into dist\ the way release builds are made: self-contained, with the .NET
# runtime alongside it. With -Install, also copies it to %LOCALAPPDATA%\Programs\cc-switcher and
# adds a Start menu shortcut. That is the folder the release installer uses, so the two must lay
# out the same files: a build that expects the machine's .NET, dropped among a release's bundled
# runtime, confuses .NET's launcher into asking for a .NET download.
#
# From WSL:  powershell.exe -ExecutionPolicy Bypass -File "$(wslpath -w build.ps1)" -Install
param(
    [switch]$Install,
    # Internal: skip the build and only install what's already in dist\.
    [switch]$InstallOnly
)

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'src\CcSwitcher\CcSwitcher.csproj'
$dist = Join-Path $PSScriptRoot 'dist'
$target = Join-Path $env:LOCALAPPDATA 'Programs\cc-switcher'
$exe = Join-Path $target 'cc-switcher.exe'
# The app's own code; the rest of the folder is the runtime, which rarely changes.
$built = Join-Path $dist 'cc-switcher.dll'
$installed = Join-Path $target 'cc-switcher.dll'

function Test-InsidePackage {
    Add-Type -Namespace CcSwitcherBuild -Name AppModel -MemberDefinition @'
[DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
public static extern int GetCurrentPackageFullName(ref int length, System.Text.StringBuilder name);
'@
    $length = 0
    # 15700 = APPMODEL_ERROR_NO_PACKAGE
    [CcSwitcherBuild.AppModel]::GetCurrentPackageFullName([ref]$length, $null) -ne 15700
}

if (-not $InstallOnly) {
    if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
    dotnet publish $project -c Release -r win-x64 --self-contained true -o $dist --nologo
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

if (-not ($Install -or $InstallOnly)) { return }

# A shell started from inside Claude Desktop (its Claude Code sessions included, even through
# WSL) inherits Claude's MSIX sandbox, where AppData writes are silently redirected into the
# package's private cache. Installing from there would put the app where nothing can find it,
# so hand the install to a process started by WMI, which runs outside the sandbox.
if (Test-InsidePackage) {
    $command = "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`" -InstallOnly"
    $created = Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{ CommandLine = $command }
    if ($created.ReturnValue -ne 0) { throw "Couldn't start the installer outside Claude's sandbox (WMI error $($created.ReturnValue))." }
    Wait-Process -Id $created.ProcessId -Timeout 120 -ErrorAction SilentlyContinue

    # That process's output isn't visible here, so check its work instead.
    if (-not (Test-Path $installed) -or (Get-FileHash $installed).Hash -ne (Get-FileHash $built).Hash) {
        throw "The install didn't complete. If CC Switcher is open, close it and try again."
    }
    "Installed to $target (Start menu: CC Switcher)"
    return
}

New-Item -ItemType Directory -Force -Path $target | Out-Null
Copy-Item (Join-Path $dist '*') $target -Recurse -Force

$shortcut = (New-Object -ComObject WScript.Shell).CreateShortcut(
    (Join-Path ([Environment]::GetFolderPath('Programs')) 'CC Switcher.lnk'))
$shortcut.TargetPath = $exe
$shortcut.WorkingDirectory = $target
$shortcut.Description = 'Open Claude Desktop with a chosen profile'
$shortcut.Save()

"Installed to $target (Start menu: CC Switcher)"
