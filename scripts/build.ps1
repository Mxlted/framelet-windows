[CmdletBinding()]
param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Release')

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
Push-Location $projectRoot
try {
    dotnet restore src/Framelet.App/Framelet.App.csproj --locked-mode -p:Platform=x64
    if ($LASTEXITCODE -ne 0) { throw 'Dependency restore failed.' }
    dotnet build src/Framelet.App/Framelet.App.csproj --no-restore -c $Configuration -p:Platform=x64
    if ($LASTEXITCODE -ne 0) { throw 'Application build failed.' }
}
finally { Pop-Location }
