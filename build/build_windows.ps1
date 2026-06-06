$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $PSScriptRoot
$Dist = Join-Path $PSScriptRoot "dist"

if (Test-Path $Dist) {
    Remove-Item $Dist -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $Dist | Out-Null

$Exe = Join-Path $Dist "TaskOverlay.exe"

Write-Host "Building TaskOverlay..."
$env:GOOS = "windows"
$env:GOARCH = "amd64"

go build -trimpath -ldflags="-H windowsgui -s -w" -o $Exe "$Root\cmd\taskoverlay"

$Readme = Join-Path $Dist "README.txt"
$Changelog = Join-Path $Dist "CHANGELOG.txt"

@"
TaskOverlay portable

Run TaskOverlay.exe.

State:
%APPDATA%\TaskOverlay\state.json

Logs:
%APPDATA%\TaskOverlay\logs
"@ | Set-Content -Path $Readme -Encoding UTF8

Get-Content (Join-Path $Root "CHANGELOG.md") | Set-Content -Path $Changelog -Encoding UTF8

$Zip = Join-Path $Dist "TaskOverlay_portable.zip"
Compress-Archive -Path $Exe, $Readme, $Changelog -DestinationPath $Zip -Force

Write-Host "Done:"
Write-Host $Exe
Write-Host $Zip
