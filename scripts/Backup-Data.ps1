param(
    [string]$DestinationDirectory
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$source = Join-Path $env:LOCALAPPDATA "FloatMate\data.json"

if (-not $DestinationDirectory) {
    $DestinationDirectory = Join-Path $repositoryRoot "migration-data"
}

if (-not (Test-Path -LiteralPath $source)) {
    throw "FloatMate local data was not found: $source"
}

New-Item -ItemType Directory -Path $DestinationDirectory -Force | Out-Null
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$destination = Join-Path $DestinationDirectory "FloatMate-data-$stamp.json"
Copy-Item -LiteralPath $source -Destination $destination

Write-Host "Data backup created: $destination" -ForegroundColor Green
Write-Host "This folder is ignored by Git. Transfer the file separately using trusted private storage."
