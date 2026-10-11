# Builds cc-switcher.exe into dist\. With -Install, also copies it to
# %LOCALAPPDATA%\Programs\cc-switcher and adds a Start menu shortcut.
#
# From WSL:  powershell.exe -ExecutionPolicy Bypass -File "$(wslpath -w build.ps1)" -Install
param(
    [switch]$Install,
    # Internal: skip the build and only install what's already in dist\.
    [switch]$InstallOnly
)

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'src\CcSwitcher\CcSwitcher.csproj'
$built = Join-Path $PSScriptRoot 'dist\cc-switcher.exe'
$target = Join-Path $env:LOCALAPPDATA 'Programs\cc-switcher'
$installed = Join-Path $target 'cc-switcher.exe'

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
    dotnet publish $project -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o (Split-Path $built) --nologo
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
Copy-Item $built $installed -Force

$shortcut = (New-Object -ComObject WScript.Shell).CreateShortcut(
    (Join-Path ([Environment]::GetFolderPath('Programs')) 'CC Switcher.lnk'))
$shortcut.TargetPath = $installed
$shortcut.WorkingDirectory = $target
$shortcut.Description = 'Open Claude Desktop with a chosen profile'
$shortcut.Save()

"Installed to $target (Start menu: CC Switcher)"
