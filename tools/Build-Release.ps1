# Builds Exo Cinema's release installer, as Exo Browser's and Exo Launcher's tools\Build-Release.ps1 do:
# artifacts\ExoCinema-Setup.exe, its .sha256 and artifacts\whats-new.json (upload all three to the release; the in-app
# updater reads them by these names, AppUpdater.cs).
#   pwsh -File tools\Build-Release.ps1 [-PayloadDir <folder>] [-TestRoot <folder>]
#   -PayloadDir  package an app folder that already exists (an earlier publish, or an installed copy) instead of
#                publishing src; files only an install adds (Uninstall.exe, ExoSetup.exe, the old manifest) are left out.
#   -TestRoot    a walk-through build: installs only under that folder, as "Exo Cinema (test)" with its own registry
#                key (ExoCinemaTest), never over the real Exo Cinema. Written as ExoCinema-Setup-test.exe.
# Steps: What's New and libmpv checked, a clean publish, the payload staged and checked, Setup's window
# (setup\build.ps1), the NSIS install engine (tools\ExoCinema.nsi, which records the payload's SHA-256s), then the
# window and engine joined, and the SHA-256 file.
[CmdletBinding()]
param(
    [string]$PayloadDir,
    [string]$TestRoot,
    [string]$MakeNsis = 'C:\Program Files (x86)\NSIS\makensis.exe'
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$version = (Get-Content (Join-Path $repo 'VERSION') -Raw).Trim()
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw "VERSION should be X.Y.Z, not '$version'." }

# This release's What's New, uploaded beside Setup as whats-new.json: the version before it shows these items while it
# offers the update.
$whatsNewFile = Join-Path $repo 'src\ExoCinema\wwwroot\whats-new.json'
$whatsNew = @([IO.File]::ReadAllText($whatsNewFile) | ConvertFrom-Json) | Where-Object version -EQ $version | Select-Object -First 1
if (-not $whatsNew) { throw "src\ExoCinema\wwwroot\whats-new.json has no entry for $version." }
if (-not (Test-Path $MakeNsis)) { throw "NSIS not found at $MakeNsis." }

$release = Join-Path $repo 'out\release'
$payload = Join-Path $release 'payload'
if (Test-Path $release) { Remove-Item -Recurse -Force $release }
New-Item -ItemType Directory -Force -Path $payload | Out-Null

if ($PayloadDir) {
    $source = (Resolve-Path -LiteralPath $PayloadDir).Path
    Write-Output "Packaging $source"
    robocopy $source $payload /MIR /XF Uninstall.exe ExoSetup.exe payload-manifest.json /XJ /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Copying the payload failed ($LASTEXITCODE)." }
} else {
    # mpv draws every picture; the project only picks it up when lib\mpv has it (lib\mpv\SOURCE.txt says which build).
    if (-not (Test-Path (Join-Path $repo 'lib\mpv\libmpv-2.dll'))) { throw 'lib\mpv\libmpv-2.dll is missing (lib\mpv\SOURCE.txt says which build to fetch).' }
    $project = Join-Path $repo 'src\ExoCinema\ExoCinema.csproj'
    $publish = Join-Path $repo 'out\publish'
    if (Test-Path $publish) { Remove-Item -Recurse -Force $publish }
    dotnet clean $project -c Release -r win-x64 -p:Platform=x64 --nologo -v q
    if ($LASTEXITCODE) { throw "dotnet clean failed ($LASTEXITCODE)." }
    dotnet publish $project -c Release -r win-x64 -p:Platform=x64 -o $publish --nologo
    if ($LASTEXITCODE) { throw "dotnet publish failed ($LASTEXITCODE)." }
    robocopy $publish $payload /MIR /XJ /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Copying the payload failed ($LASTEXITCODE)." }
}
# What Exo Cinema can't start without, before anything is built around it (the engine checks them again, with every hash).
. (Join-Path $PSScriptRoot 'installer\Verify-Payload.ps1') -DefinitionsOnly
Test-ExoRequired $payload
$null = @(Get-ExoPayloadFiles $payload) # no links
$appBytes = (Get-ChildItem -Recurse -File $payload | Measure-Object Length -Sum).Sum
Write-Output "Payload: $([math]::Round($appBytes / 1MB, 1)) MB"

$artifacts = Join-Path $repo 'artifacts'
New-Item -ItemType Directory -Force -Path $artifacts | Out-Null
$name = if ($TestRoot) { 'ExoCinema-Setup-test.exe' } else { 'ExoCinema-Setup.exe' }
$setup = Join-Path $artifacts $name

# Setup's window, once: the engine carries a copy of it (the uninstaller in app\), and the finished Setup starts with it.
$windowArgs = @('-NoProfile', '-File', (Join-Path $repo 'setup\build.ps1'))
if ($TestRoot) { $windowArgs += @('-TestRoot', $TestRoot) }
pwsh @windowArgs
if ($LASTEXITCODE) { throw "Setup window build failed ($LASTEXITCODE)." }
$shell = Join-Path $repo 'setup\out\ExoSetup.exe'

# The install engine: the payload (its manifest recorded as it builds), the staged swap, shortcuts and the uninstaller.
$engine = Join-Path $release 'ExoCinema-Engine.exe'
$defines = @("/DPAYLOAD_DIR=$payload", "/DOUTFILE=$engine", "/DPRODUCT_VERSION=$version", "/DSETUP_SHELL=$shell")
if ($TestRoot) { $defines += "/DEXO_TEST_ROOT=$([IO.Path]::GetFullPath($TestRoot).TrimEnd('\'))" }
& $MakeNsis /V2 @defines (Join-Path $PSScriptRoot 'ExoCinema.nsi')
if ($LASTEXITCODE) { throw "makensis failed ($LASTEXITCODE)." }

# The Setup people download: the window with the engine inside it (and the app's size, for real progress).
$appBytes = (Get-ChildItem -Recurse -File $payload | Measure-Object Length -Sum).Sum
pwsh -NoProfile -File (Join-Path $repo 'setup\build.ps1') -Join -Engine $engine -AppBytes $appBytes -Out $setup
if ($LASTEXITCODE) { throw "Joining Setup failed ($LASTEXITCODE)." }
Remove-Item $engine -Force

$hash = (Get-FileHash $setup -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText("$setup.sha256", "$hash  $name", [Text.Encoding]::ASCII)
[IO.File]::WriteAllText((Join-Path $artifacts 'whats-new.json'), ($whatsNew | ConvertTo-Json -Depth 6), [Text.UTF8Encoding]::new($false))
Write-Output "Exo Cinema ${version}: $setup ($([math]::Round((Get-Item $setup).Length / 1MB, 1)) MB), SHA-256 $hash"
