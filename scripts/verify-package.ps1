[CmdletBinding()]
param([Parameter(Mandatory)][string]$Archive)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$archivePath = (Resolve-Path -LiteralPath $Archive).Path
$verificationDirectory = Join-Path $projectRoot ('artifacts/package-verification/' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $verificationDirectory -Force | Out-Null
Expand-Archive -LiteralPath $archivePath -DestinationPath $verificationDirectory
$manifestPath = Join-Path $verificationDirectory 'SHA256SUMS.txt'
if (!(Test-Path -LiteralPath $manifestPath)) { throw 'Package has no file hash manifest.' }
$count = 0
$seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($line in Get-Content -LiteralPath $manifestPath) {
    if ($line -notmatch '^([a-f0-9]{64})  (.+)$') { throw "Invalid manifest entry: $line" }
    $expected = $Matches[1]
    $relative = $Matches[2]
    if (!$seen.Add($relative)) { throw "Duplicate manifest entry: $relative" }
    $path = [IO.Path]::GetFullPath((Join-Path $verificationDirectory $relative))
    if (!$path.StartsWith($verificationDirectory + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Manifest path escapes package directory.' }
    if (!(Test-Path -LiteralPath $path) -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected) { throw "Hash mismatch: $relative" }
    $count++
}
$actualCount = @(Get-ChildItem -LiteralPath $verificationDirectory -Recurse -File).Count - 1
if ($actualCount -ne $count) { throw 'Package contains files not accounted for in its hash manifest.' }
if (Get-ChildItem -LiteralPath $verificationDirectory -Filter 'ffmpeg*.exe' -Recurse) { throw 'FFmpeg must remain an explicitly configured optional dependency.' }
foreach ($name in @('Framelet.exe', 'Framelet.NativeHost.exe', 'ScreenRecorderLib.dll', 'msvcp140.dll', 'vcruntime140.dll', 'Framelet.runtimeconfig.json', 'licenses/ScreenRecorderLib-LICENSE.txt')) {
    if (!(Test-Path -LiteralPath (Join-Path $verificationDirectory $name))) { throw "Required package file is missing: $name" }
}
Write-Host "Verified $count package file hashes. No FFmpeg executable is bundled."
