[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+(?:-[A-Za-z0-9.-]+)?$')][string]$Version = '0.1.0-preview.1',
    [string]$VCRuntimeDirectory,
    [string]$InnoCompiler,
    [switch]$SkipTests,
    [switch]$PortableOnly,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$releaseDirectory = Join-Path $projectRoot 'artifacts/releases'
$packageName = "Framelet-$Version-win-x64"
$portablePath = Join-Path $releaseDirectory "$packageName-portable.zip"
$installerPath = Join-Path $releaseDirectory "$packageName-setup.exe"
$hashPath = Join-Path $releaseDirectory "$packageName-SHA256SUMS.txt"
$numericVersion = $Version.Split('-')[0]

foreach ($output in @($portablePath, $installerPath, $hashPath)) {
    if ((Test-Path -LiteralPath $output) -and !$Force) { throw "Output already exists: $output. Use -Force to replace this build's artifacts." }
}

if (!$VCRuntimeDirectory) {
    # Only the documented, redistributable release CRT folder is eligible. Never copy System32 or debug DLLs.
    $visualStudioRoot = Join-Path ${env:ProgramFiles} 'Microsoft Visual Studio/2022'
    $candidates = @()
    if (Test-Path -LiteralPath $visualStudioRoot) {
        foreach ($edition in Get-ChildItem -LiteralPath $visualStudioRoot -Directory) {
            $redistRoot = Join-Path $edition.FullName 'VC/Redist/MSVC'
            if (!(Test-Path -LiteralPath $redistRoot)) { continue }
            foreach ($versionDirectory in Get-ChildItem -LiteralPath $redistRoot -Directory) {
                if ($versionDirectory.Name -notmatch '^\d+\.\d+\.\d+$') { continue }
                $crt = Join-Path $versionDirectory.FullName 'x64/Microsoft.VC143.CRT'
                if (Test-Path -LiteralPath (Join-Path $crt 'msvcp140.dll')) {
                    $candidates += [pscustomobject]@{ Version = [version]$versionDirectory.Name; Path = $crt }
                }
            }
        }
    }
    $VCRuntimeDirectory = ($candidates | Sort-Object Version -Descending | Select-Object -First 1).Path
}
if (!$VCRuntimeDirectory -or !(Test-Path -LiteralPath (Join-Path $VCRuntimeDirectory 'vcruntime140.dll'))) {
    throw 'Install the Visual Studio 2022 C++ redistributable build components, or supply -VCRuntimeDirectory pointing to the licensed x64 Microsoft.VC143.CRT release folder. Nothing is downloaded automatically.'
}
$VCRuntimeDirectory = (Resolve-Path -LiteralPath $VCRuntimeDirectory).Path
if ($VCRuntimeDirectory -match 'debug_nonredist|onecore' -or $VCRuntimeDirectory -notmatch '[\\/]x64[\\/]Microsoft\.VC143\.CRT$') {
    throw 'VCRuntimeDirectory must be the standard x64 Microsoft.VC143.CRT release redistributable directory.'
}

if (!$PortableOnly -and !$InnoCompiler) {
    $locations = @((Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6/ISCC.exe'), (Join-Path $env:LOCALAPPDATA 'Programs/Inno Setup 6/ISCC.exe'))
    $InnoCompiler = $locations | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if (!$PortableOnly -and (!$InnoCompiler -or !(Test-Path -LiteralPath $InnoCompiler))) {
    throw 'Inno Setup 6 is required for the installer. Install it explicitly from https://jrsoftware.org/isdl.php, pass -InnoCompiler, or use -PortableOnly.'
}

Push-Location $projectRoot
try {
    if (!$SkipTests) { & "$PSScriptRoot/test.ps1" }
    New-Item -ItemType Directory -Path $releaseDirectory -Force | Out-Null
    $staging = Join-Path $projectRoot ("artifacts/package-staging/" + $packageName + '-' + [guid]::NewGuid().ToString('N'))
    $buildDirectory = Join-Path $projectRoot ('artifacts/package-build/' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $staging | Out-Null
    foreach ($project in @('Framelet.App', 'Framelet.NativeHost')) {
        # RID-specific publish restore must not rewrite the normal development lockfiles.
        dotnet publish "src/$project/$project.csproj" -c Release -r win-x64 --self-contained true -p:Platform=x64 "-p:Version=$Version" -p:PublishSingleFile=false -p:PublishTrimmed=false -p:RestorePackagesWithLockFile=false "-p:NuGetLockFilePath=$buildDirectory/publish-only.lock.json" --artifacts-path $buildDirectory -o $staging
        if ($LASTEXITCODE -ne 0) { throw "$project publish failed." }
    }
    foreach ($name in @('Framelet.exe', 'Framelet.NativeHost.exe', 'Framelet.NativeHost.runtimeconfig.json', 'ScreenRecorderLib.dll')) {
        if (!(Test-Path -LiteralPath (Join-Path $staging $name))) { throw "Published package is missing $name." }
    }
    Get-ChildItem -LiteralPath $staging -Filter '*.pdb' -File | Remove-Item
    foreach ($dll in Get-ChildItem -LiteralPath $VCRuntimeDirectory -Filter '*.dll' -File) { Copy-Item -LiteralPath $dll.FullName -Destination $staging }
    foreach ($required in @('msvcp140.dll', 'vcruntime140.dll', 'vcruntime140_1.dll', 'concrt140.dll')) {
        if (!(Test-Path -LiteralPath (Join-Path $staging $required))) { throw "Required native runtime file is missing: $required" }
    }
    foreach ($notice in @('LICENSE', 'README.md', 'THIRD_PARTY_NOTICES.md')) {
        $source = Join-Path $projectRoot $notice
        if (!(Test-Path -LiteralPath $source)) { throw "Release documentation is missing: $notice" }
        Copy-Item -LiteralPath $source -Destination $staging
    }
    Copy-Item -LiteralPath (Join-Path $projectRoot 'docs') -Destination (Join-Path $staging 'docs') -Recurse
    $licenses = New-Item -ItemType Directory -Path (Join-Path $staging 'licenses') -Force
    Copy-Item -LiteralPath (Join-Path $projectRoot 'installer/notices/MICROSOFT-VC-RUNTIME.txt') -Destination $licenses.FullName
    $nugetRoot = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget/packages' }
    Copy-Item -LiteralPath (Join-Path $nugetRoot 'screenrecorderlib/7.0.1/LICENSE') -Destination (Join-Path $licenses.FullName 'ScreenRecorderLib-LICENSE.txt')
    $runtimeConfig = Get-Content -LiteralPath (Join-Path $staging 'Framelet.runtimeconfig.json') -Raw | ConvertFrom-Json
    foreach ($framework in $runtimeConfig.runtimeOptions.includedFrameworks) {
        $packageId = ($framework.name + '.runtime.win-x64').ToLowerInvariant()
        $packagePath = Join-Path $nugetRoot "$packageId/$($framework.version)"
        $license = @('LICENSE.TXT', 'LICENSE') | ForEach-Object { Join-Path $packagePath $_ } | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
        if (!$license) { throw "Runtime license is missing from $packagePath" }
        Copy-Item -LiteralPath $license -Destination (Join-Path $licenses.FullName "$($framework.name)-LICENSE.txt")
        $thirdParty = Join-Path $packagePath 'THIRD-PARTY-NOTICES.TXT'
        if (Test-Path -LiteralPath $thirdParty) { Copy-Item -LiteralPath $thirdParty -Destination (Join-Path $licenses.FullName "$($framework.name)-THIRD-PARTY-NOTICES.txt") }
    }
    $metadata = [ordered]@{ version = $Version; architecture = 'win-x64'; nativeRuntimeVersion = (Get-Item -LiteralPath (Join-Path $VCRuntimeDirectory 'vcruntime140.dll')).VersionInfo.FileVersion; ffmpegBundled = $false; selfContained = $true }
    $metadata | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $staging 'package-info.json') -Encoding utf8
    $manifest = Get-ChildItem -LiteralPath $staging -Recurse -File | Sort-Object FullName | ForEach-Object {
        $relative = [IO.Path]::GetRelativePath($staging, $_.FullName).Replace('\', '/')
        "$((Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant())  $relative"
    }
    $manifest | Set-Content -LiteralPath (Join-Path $staging 'SHA256SUMS.txt') -Encoding utf8
    Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $portablePath -CompressionLevel Optimal -Force
    if (!$PortableOnly) {
        & $InnoCompiler '/Q' "/DAppVersion=$Version" "/DFileVersion=$numericVersion" "/DSourcePath=$staging" "/DOutputPath=$releaseDirectory" (Join-Path $projectRoot 'installer/Framelet.iss')
        if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
    }
    $outputs = @($portablePath)
    if (!$PortableOnly) { $outputs += $installerPath }
    $outputs | ForEach-Object { "$((Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant())  $([IO.Path]::GetFileName($_))" } | Set-Content -LiteralPath $hashPath -Encoding utf8
    & "$PSScriptRoot/verify-package.ps1" -Archive $portablePath
    Write-Host "Release files: $releaseDirectory"
    Write-Host "Unpacked validation build: $staging"
}
finally { Pop-Location }
