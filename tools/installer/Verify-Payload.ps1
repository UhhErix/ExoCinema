# Checks an app folder against the payload-manifest.json in it (tools\installer\New-PayloadManifest.ps1 writes it at
# build time), as Exo Launcher's Verify-Payload.ps1 does: every file listed, at its size and SHA-256, nothing else, no
# links, and Exo Cinema's own files present. The install engine runs it on its staged copy before anything installed
# is touched.
#   powershell -File Verify-Payload.ps1 -PayloadRoot <folder>        exit 0 when the folder is exactly the payload
param([string]$PayloadRoot, [switch]$DefinitionsOnly)
$ErrorActionPreference = 'Stop'

# What Exo Cinema can't start without: the .NET host and app, its page, and mpv.
$ExoCinemaRequired = @('ExoCinema.exe', 'ExoCinema.dll', 'ExoCinema.deps.json', 'ExoCinema.runtimeconfig.json', 'wwwroot\index.html', 'libmpv-2.dll')

function Get-ExoPayloadHash([string]$Path) {
    $algorithm = [Security.Cryptography.SHA256]::Create()
    $stream = $null
    try {
        $stream = [IO.File]::OpenRead($Path)
        return [BitConverter]::ToString($algorithm.ComputeHash($stream)).Replace('-', '').ToLowerInvariant()
    } finally {
        if ($null -ne $stream) { $stream.Dispose() }
        $algorithm.Dispose()
    }
}

function Get-ExoPayloadFiles([string]$Root) {
    $rootPath = [IO.Path]::GetFullPath($Root).TrimEnd('\')
    $pending = [Collections.Generic.Stack[string]]::new()
    $pending.Push($rootPath)
    while ($pending.Count -gt 0) {
        $directory = $pending.Pop()
        if (([IO.File]::GetAttributes($directory) -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Payload links are not allowed.' }
        foreach ($entry in [IO.Directory]::EnumerateFileSystemEntries($directory)) {
            $attributes = [IO.File]::GetAttributes($entry)
            if (($attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Payload links are not allowed.' }
            if (($attributes -band [IO.FileAttributes]::Directory) -ne 0) { $pending.Push($entry) }
            elseif (-not [StringComparer]::OrdinalIgnoreCase.Equals($entry, (Join-Path $rootPath 'payload-manifest.json'))) { $entry }
        }
    }
}

function Test-ExoRequired([string]$Root) {
    $rootPath = [IO.Path]::GetFullPath($Root).TrimEnd('\')
    foreach ($relative in $ExoCinemaRequired) {
        if (-not [IO.File]::Exists((Join-Path $rootPath $relative))) { throw "Required payload file is missing: $relative" }
    }
}

function Test-ExoPayload([string]$Root) {
    $rootPath = [IO.Path]::GetFullPath($Root).TrimEnd('\')
    $manifestPath = Join-Path $rootPath 'payload-manifest.json'
    if (-not [IO.File]::Exists($manifestPath) -or ([IO.FileInfo]$manifestPath).Length -gt 8MB) { throw 'Payload manifest missing or too large.' }
    if (([IO.File]::GetAttributes($manifestPath) -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Payload manifest cannot be a link.' }
    $manifest = [IO.File]::ReadAllText($manifestPath) | ConvertFrom-Json
    if ($manifest.schema -ne 1 -or @($manifest.files).Count -eq 0) { throw 'Invalid payload manifest.' }
    Test-ExoRequired $rootPath
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($entry in $manifest.files) {
        if ([string]::IsNullOrWhiteSpace($entry.path) -or [IO.Path]::IsPathRooted($entry.path) -or $entry.path -match '(^|[\\/])\.\.([\\/]|$)' -or $entry.sha256 -notmatch '^[a-fA-F0-9]{64}$' -or $entry.size -lt 0) { throw 'Invalid payload manifest entry.' }
        $path = [IO.Path]::GetFullPath((Join-Path $rootPath $entry.path))
        if (-not $path.StartsWith($rootPath + '\', [StringComparison]::OrdinalIgnoreCase) -or -not $seen.Add($path)) { throw 'Duplicate or escaped payload path.' }
        if (-not [IO.File]::Exists($path) -or ([IO.FileInfo]$path).Length -ne $entry.size) { throw "Payload size mismatch: $($entry.path)" }
    }
    $files = @(Get-ExoPayloadFiles $rootPath)
    if ($files.Count -ne $seen.Count) { throw 'Payload contains an unexpected or missing file.' }
    foreach ($entry in $manifest.files) {
        $path = [IO.Path]::GetFullPath((Join-Path $rootPath $entry.path))
        if (-not [StringComparer]::OrdinalIgnoreCase.Equals((Get-ExoPayloadHash $path), $entry.sha256)) { throw "Payload checksum mismatch: $($entry.path)" }
    }
    return $manifest
}

if ($DefinitionsOnly) { return }
try { $manifest = Test-ExoPayload $PayloadRoot; Write-Output "Verified $(@($manifest.files).Count) payload files."; exit 0 }
catch { Write-Error $_ -ErrorAction Continue; exit 1 }
