param(
    [string]$BackupFile
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$migrationDirectory = Join-Path $repositoryRoot "migration-data"

if (Get-Process FloatMate -ErrorAction SilentlyContinue) {
    throw "Exit FloatMate from the system tray before restoring data."
}

if (-not $BackupFile) {
    $latest = Get-ChildItem -LiteralPath $migrationDirectory -Filter "FloatMate-data-*.json" -File -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
    if ($latest) { $BackupFile = $latest.FullName }
}

if (-not $BackupFile -or -not (Test-Path -LiteralPath $BackupFile)) {
    throw "No backup was found. Put FloatMate-data-*.json in the migration-data folder."
}

$raw = Get-Content -LiteralPath $BackupFile -Raw -Encoding UTF8
$null = $raw | ConvertFrom-Json

$dataDirectory = Join-Path $env:LOCALAPPDATA "FloatMate"
$destination = Join-Path $dataDirectory "data.json"
New-Item -ItemType Directory -Path $dataDirectory -Force | Out-Null

if (Test-Path -LiteralPath $destination) {
    New-Item -ItemType Directory -Path $migrationDirectory -Force | Out-Null
    $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
    Copy-Item -LiteralPath $destination -Destination (Join-Path $migrationDirectory "pre-restore-$stamp.json")
}

Copy-Item -LiteralPath $BackupFile -Destination $destination -Force
Write-Host "Data restored: $destination" -ForegroundColor Green
