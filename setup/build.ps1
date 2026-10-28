# Builds Exo Cinema Setup's window (setup\out\ExoSetup.exe), as Exo Browser's and Exo Launcher's setup\build.ps1 do:
# its page (setup\page, plain HTML, CSS and JS, with Exo Cinema's glass.css, logo and Inter from the app's wwwroot)
# staged in setup\build\ui and packed into the exe, then the C++ window with the Visual Studio C++ tools (vcvars64, rc,
# cl) and WebView2's static loader from the NuGet package the app uses.
#   pwsh -File setup\build.ps1 [-TestRoot <folder>]     the window (-TestRoot: a walk-through build's, see Build-Release)
#   pwsh -File setup\build.ps1 -PageOnly                only the page, in setup\build\ui (open index.html there to preview)
#   pwsh -File setup\build.ps1 -Join -Engine <engine.exe> -AppBytes <n> -Out <ExoCinema-Setup.exe>
#                                                       the finished installer from the window already built
[CmdletBinding()]
param(
    [string]$TestRoot,
    [switch]$PageOnly,
    [switch]$Join,
    [string]$Engine,
    [long]$AppBytes,
    [string]$Out
)
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$repo = Split-Path -Parent $here
$build = Join-Path $here 'build'
$outDir = Join-Path $here 'out'
$shell = Join-Path $outDir 'ExoSetup.exe'
New-Item -ItemType Directory -Force -Path $build, $outDir | Out-Null

# The finished installer: the window, then the engine, its size, the app's size (for progress) and the mark.
if ($Join) {
    if (-not $Engine -or -not $Out) { throw '-Join needs -Engine and -Out.' }
    if (-not (Test-Path $shell)) { throw "Build the window first ($shell is missing)." }
    $target = [IO.File]::Open($Out, [IO.FileMode]::Create)
    try {
        foreach ($part in $shell, $Engine) { $source = [IO.File]::OpenRead($part); try { $source.CopyTo($target) } finally { $source.Dispose() } }
        $tail = [IO.BinaryWriter]::new($target)
        $tail.Write([uint64](Get-Item $Engine).Length)
        $tail.Write([uint64]$AppBytes)
        $tail.Write([Text.Encoding]::ASCII.GetBytes('EXOSETUP'))
        $tail.Flush()
    } finally { $target.Dispose() }
    Write-Output "Exo Cinema Setup: $Out"
    return
}

$version = (Get-Content (Join-Path $repo 'VERSION') -Raw).Trim()
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw "VERSION should be X.Y.Z, not '$version'." }

# The page: Setup's own files, and from the app what it shares with Exo Cinema (its glass, its logo, Inter and its licence).
$ui = Join-Path $build 'ui'
if (Test-Path $ui) { Remove-Item -Recurse -Force $ui }
New-Item -ItemType Directory -Force -Path (Join-Path $ui 'fonts') | Out-Null
$www = Join-Path $repo 'src\ExoCinema\wwwroot'
Copy-Item (Join-Path $here 'page\*') $ui -Recurse -Force
Copy-Item (Join-Path $www 'glass.css'), (Join-Path $www 'logo.svg') $ui -Force
Copy-Item (Join-Path $www 'fonts\InterVariable.woff2'), (Join-Path $www 'fonts\LICENSE-Inter.txt') (Join-Path $ui 'fonts') -Force
if ($PageOnly) { Write-Output "Exo Cinema Setup page: $(Join-Path $ui 'index.html')"; return }

# One pack the exe serves from memory: [u32 count] then [u16 name][name][u32 size][bytes] per file.
$files = @(Get-ChildItem -Recurse -File $ui | Sort-Object FullName)
$pack = [IO.MemoryStream]::new()
$writer = [IO.BinaryWriter]::new($pack)
$writer.Write([uint32]$files.Count)
foreach ($file in $files) {
    $name = [Text.Encoding]::UTF8.GetBytes($file.FullName.Substring($ui.Length + 1).Replace('\', '/'))
    $bytes = [IO.File]::ReadAllBytes($file.FullName)
    $writer.Write([uint16]$name.Length); $writer.Write($name)
    $writer.Write([uint32]$bytes.Length); $writer.Write($bytes)
}
$writer.Flush()
[IO.File]::WriteAllBytes((Join-Path $build 'ui.pack'), $pack.ToArray())

# The version (and a walk-through build's folder) for the window and its version details, as one header.
$config = @(
    '#pragma once',
    "#define VERSION_COMMA $($version -replace '\.', ','),0",
    "#define VERSION_TEXT `"$version`"",
    "#define PRODUCT_VERSION L`"$version`""
)
if ($TestRoot) {
    $full = [IO.Path]::GetFullPath($TestRoot).TrimEnd('\')
    $config += "#define TEST_ROOT L`"$($full.Replace('\', '\\').Replace('"', ''))`""
}
[IO.File]::WriteAllLines((Join-Path $build 'setup-config.h'), [string[]]$config, [Text.UTF8Encoding]::new($false))

$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$vs = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $vs) { throw 'Visual Studio C++ tools (x64) are not installed.' }
$vcvars = Join-Path $vs 'VC\Auxiliary\Build\vcvars64.bat'
# WebView2's headers and static loader: the package (and version) Exo Cinema itself uses; a restore of the app fetches it.
$wv2 = Join-Path $env:USERPROFILE '.nuget\packages\microsoft.web.webview2\1.0.4314.35\build\native'
if (-not (Test-Path (Join-Path $wv2 'x64\WebView2LoaderStatic.lib'))) { throw "WebView2 SDK not found in $wv2 (dotnet restore src\ExoCinema fetches it)." }
$obj = Join-Path $build 'obj'
New-Item -ItemType Directory -Force $obj | Out-Null
$commands = @(
    "`"$vcvars`" >nul 2>&1",
    "cd /d `"$here\src`"",
    "rc /nologo /I `"$build`" /fo `"$obj\setup.res`" setup.rc",
    ("cl /nologo /std:c++20 /EHsc /O2 /MT /W3 /utf-8 /permissive- /DUNICODE /D_UNICODE /DNOMINMAX /DWIN32_LEAN_AND_MEAN /D_WIN32_WINNT=0x0A00 " +
     "/I`"$build`" /I`"$wv2\include`" main.cpp `"$obj\setup.res`" /Fo`"$obj\\`" /Fe`"$shell`" " +
     "/link /SUBSYSTEM:WINDOWS /MANIFEST:NO `"$wv2\x64\WebView2LoaderStatic.lib`" ole32.lib oleaut32.lib shell32.lib shlwapi.lib user32.lib gdi32.lib dwmapi.lib version.lib advapi32.lib")
) -join ' && '
cmd /c $commands
if ($LASTEXITCODE) { throw "Exo Cinema Setup build failed ($LASTEXITCODE)." }
Write-Output "Exo Cinema Setup window: $shell"
