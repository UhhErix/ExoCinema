# Records every file of the payload (size and SHA-256) in payload-manifest.json at its root, as Exo Launcher's does.
# tools\ExoCinema.nsi runs it as it builds, so the manifest always describes exactly what the engine carries; the engine
# checks its staged copy against it (Verify-Payload.ps1) before it installs.
param([Parameter(Mandatory)][string]$PayloadRoot, [Parameter(Mandatory)][string]$Version)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Verify-Payload.ps1') -DefinitionsOnly -PayloadRoot $PayloadRoot
$rootPath = (Resolve-Path -LiteralPath $PayloadRoot).Path.TrimEnd('\')
Test-ExoRequired $rootPath
$files = @(Get-ExoPayloadFiles $rootPath | Sort-Object | ForEach-Object {
        @{ path = $_.Substring($rootPath.Length + 1).Replace('\', '/'); size = ([IO.FileInfo]$_).Length; sha256 = (Get-ExoPayloadHash $_) }
    })
$json = @{ schema = 1; version = $Version; files = $files } | ConvertTo-Json -Depth 5 -Compress
[IO.File]::WriteAllText((Join-Path $rootPath 'payload-manifest.json'), $json, [Text.UTF8Encoding]::new($false))
$null = Test-ExoPayload $rootPath
Write-Output "Recorded $($files.Count) payload hashes."
