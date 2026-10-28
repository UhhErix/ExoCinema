# After Exo Cinema closes, its WebView2 processes (msedgewebview2.exe with --user-data-dir=<its profile>) let go of the
# profile a moment later. The install engine waits for them here, up to -Seconds, before it swaps or removes anything.
# It never stops one, and it doesn't wait for those of an Exo Cinema still running from somewhere else (a developer
# build shares the profile): only for those whose app has exited or is the one in -AppDirectory.
# Exit 0 once none is left, 1 if some still run.
param(
    [Parameter(Mandatory)][string]$ProfileDirectory,
    [Parameter(Mandatory)][string]$AppDirectory,
    [int]$Seconds = 5
)
$ErrorActionPreference = 'Stop'
$target = [IO.Path]::GetFullPath($ProfileDirectory).TrimEnd('\')
$app = Join-Path ([IO.Path]::GetFullPath($AppDirectory).TrimEnd('\')) 'ExoCinema.exe'

function Test-Profile([string]$CommandLine) {
    if ([string]::IsNullOrWhiteSpace($CommandLine)) { return $false }
    $match = [regex]::Match($CommandLine, '(?:^|\s)--user-data-dir=(?:"([^"\r\n]+)"|([^\s"\r\n]+))', 'IgnoreCase')
    if (-not $match.Success) { return $false }
    $value = if ($match.Groups[1].Success) { $match.Groups[1].Value } else { $match.Groups[2].Value }
    try { return [StringComparer]::OrdinalIgnoreCase.Equals([IO.Path]::GetFullPath($value).TrimEnd('\'), $target) } catch { return $false }
}

$deadline = [DateTime]::UtcNow.AddSeconds($Seconds)
do {
    $all = @{}
    foreach ($p in Get-CimInstance Win32_Process -Property ProcessId, ParentProcessId, Name, ExecutablePath, CommandLine) { $all[[int]$p.ProcessId] = $p }
    $left = @($all.Values | Where-Object { $_.Name -eq 'msedgewebview2.exe' -and (Test-Profile $_.CommandLine) } | Where-Object {
            # Up through WebView2's own processes to the app that started them.
            $owner = $_
            for ($i = 0; $i -lt 8 -and $owner -and $owner.Name -eq 'msedgewebview2.exe'; $i++) { $owner = $all[[int]$owner.ParentProcessId] }
            -not $owner -or ($owner.ExecutablePath -and [StringComparer]::OrdinalIgnoreCase.Equals($owner.ExecutablePath, $app))
        })
    if ($left.Count -eq 0) { exit 0 }
    Start-Sleep -Milliseconds 200
} while ([DateTime]::UtcNow -lt $deadline)
Write-Output "$($left.Count) WebView2 process(es) still hold the profile."
exit 1
