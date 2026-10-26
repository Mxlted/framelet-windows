[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [switch]$DesktopCapture,
    [switch]$LiveRecording,
    [switch]$FfmpegIntegration
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
Push-Location $projectRoot
try {
    $projects = @('Framelet.Core.Tests', 'Framelet.Editor.Tests', 'Framelet.Capture.Tests', 'Framelet.Recording.Tests', 'Framelet.Session.Tests')
    foreach ($name in $projects) {
        $project = "tests/$name/$name.csproj"
        Write-Host "Running $name"
        dotnet restore $project --locked-mode -p:Platform=x64
        if ($LASTEXITCODE -ne 0) { throw "$name restore failed." }
        $runArguments = @('run', '--project', $project, '--no-restore', '-c', $Configuration, '-p:Platform=x64')
        if ($name -eq 'Framelet.Capture.Tests' -and $DesktopCapture) { $runArguments += @('--', '--desktop') }
        if ($name -eq 'Framelet.Core.Tests' -and $FfmpegIntegration) { $runArguments += @('--', '--ffmpeg-integration') }
        if ($name -eq 'Framelet.Recording.Tests' -and $LiveRecording) {
            $recordingOutput = Join-Path $projectRoot ('artifacts/recording-check/' + [guid]::NewGuid().ToString('N'))
            New-Item -ItemType Directory -Path $recordingOutput -Force | Out-Null
            $runArguments += @('--', '--live', '--output', $recordingOutput)
        }
        & dotnet @runArguments
        if ($LASTEXITCODE -ne 0) { throw "$name failed with exit code $LASTEXITCODE." }
    }
}
finally { Pop-Location }
