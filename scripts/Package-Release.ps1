param(
    [string]$Version = "0.4.1"
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$app = Join-Path $repositoryRoot "dist\FloatMate\FloatMate.exe"
$readme = Join-Path $repositoryRoot "release-assets\README.txt"
$releaseNotes = Join-Path $repositoryRoot "release-notes\v$Version.md"
$releaseDirectory = Join-Path $repositoryRoot "artifacts\releases\v$Version"
$packageName = "FloatMate-v$Version-win-x64"
$stagingDirectory = Join-Path $releaseDirectory $packageName
$zipPath = Join-Path $releaseDirectory "$packageName.zip"
$checksumPath = Join-Path $releaseDirectory "SHA256SUMS.txt"

foreach ($requiredFile in @($app, $readme, $releaseNotes)) {
    if (-not (Test-Path -LiteralPath $requiredFile)) {
        throw "Required release file was not found: $requiredFile"
    }
}

New-Item -ItemType Directory -Path $stagingDirectory -Force | Out-Null
Copy-Item -LiteralPath $app -Destination (Join-Path $stagingDirectory "FloatMate.exe") -Force
Copy-Item -LiteralPath $readme -Destination (Join-Path $stagingDirectory "README.txt") -Force
Copy-Item -LiteralPath $releaseNotes -Destination (Join-Path $stagingDirectory "RELEASE_NOTES.md") -Force

Compress-Archive -Path (Join-Path $stagingDirectory "*") -DestinationPath $zipPath -CompressionLevel Optimal -Force
$hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath $checksumPath -Value "$hash  $packageName.zip" -Encoding ASCII

Write-Host "Release package: $zipPath" -ForegroundColor Green
Write-Host "Checksum file: $checksumPath"
Write-Host "Release notes: $releaseNotes"
