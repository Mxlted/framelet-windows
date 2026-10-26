[CmdletBinding()]
param([Parameter(Mandatory)][string]$Installer)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$installerPath = (Resolve-Path -LiteralPath $Installer).Path
$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{570AF1C8-B103-47C3-9604-7CBA6EBF31B4}_is1'
if (Test-Path -LiteralPath $uninstallKey) { throw 'Framelet is already installed for this user. Installer validation refuses to replace its registration.' }
$checkRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot ('artifacts/installer-check/' + [guid]::NewGuid().ToString('N'))))
$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot 'artifacts'))
if (!$checkRoot.StartsWith($artifactsRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Installer check path must stay under workspace artifacts.' }
New-Item -ItemType Directory -Path $checkRoot | Out-Null
$installPath = Join-Path $checkRoot 'app'
$settingsPath = Join-Path $env:LOCALAPPDATA 'Framelet/settings.json'
$beforeSettings = if (Test-Path -LiteralPath $settingsPath) { (Get-FileHash -LiteralPath $settingsPath).Hash } else { $null }
$arguments = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', '/NOICONS', '/TASKS=""', ('/DIR="' + $installPath + '"'), ('/LOG="' + (Join-Path $checkRoot 'install.log') + '"'))
$install = Start-Process -FilePath $installerPath -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
if ($install.ExitCode -ne 0) { throw "Installer exited $($install.ExitCode). Close Framelet, including its tray window, and inspect $checkRoot/install.log." }
foreach ($name in @('Framelet.exe', 'Framelet.NativeHost.exe', 'ScreenRecorderLib.dll', 'msvcp140.dll', 'vcruntime140.dll', 'unins000.exe')) {
    if (!(Test-Path -LiteralPath (Join-Path $installPath $name))) { throw "Installer omitted $name." }
}
if (!(Test-Path -LiteralPath $uninstallKey)) { throw 'Per-user uninstall registration was not created.' }
$registeredPath = (Get-ItemProperty -LiteralPath $uninstallKey).InstallLocation.TrimEnd('\')
if (![IO.Path]::GetFullPath($registeredPath).Equals($installPath, [StringComparison]::OrdinalIgnoreCase)) { throw 'Installer registered the wrong installation directory.' }
$userFile = Join-Path $installPath 'user-capture-sentinel.txt'
'User-created capture placeholder; the uninstaller must preserve this file.' | Set-Content -LiteralPath $userFile
$uninstallArgs = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', ('/LOG="' + (Join-Path $checkRoot 'uninstall.log') + '"'))
$uninstall = Start-Process -FilePath (Join-Path $installPath 'unins000.exe') -ArgumentList $uninstallArgs -WindowStyle Hidden -Wait -PassThru
if ($uninstall.ExitCode -ne 0) { throw "Uninstaller exited $($uninstall.ExitCode). See $checkRoot/uninstall.log." }
if (Test-Path -LiteralPath (Join-Path $installPath 'Framelet.exe')) { throw 'Uninstaller left the application executable behind.' }
if (Test-Path -LiteralPath $uninstallKey) { throw 'Uninstaller left its per-user registration behind.' }
if (!(Test-Path -LiteralPath $userFile)) { throw 'Uninstaller removed a user-created file.' }
$afterSettings = if (Test-Path -LiteralPath $settingsPath) { (Get-FileHash -LiteralPath $settingsPath).Hash } else { $null }
if ($beforeSettings -ne $afterSettings) { throw 'Installer lifecycle changed existing application settings.' }
Write-Host 'Installer and uninstaller passed: per-user registration, required files, preserved user files, unchanged settings.'
Write-Host "Validation logs: $checkRoot"
