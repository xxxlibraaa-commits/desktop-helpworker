param(
    [switch]$FrameworkDependent
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "scripts\Common.ps1")

$projectPath = Join-Path $PSScriptRoot "FloatMate\FloatMate.csproj"
$dotnet = Get-FloatMateDotNet -RepositoryRoot $PSScriptRoot

if (-not $dotnet) {
    throw ".NET 8 SDK was not found. Run install.cmd first."
}

$selfContained = if ($FrameworkDependent) { "false" } else { "true" }
$outputName = if ($FrameworkDependent) { "FloatMate-framework-dependent" } else { "FloatMate" }
$outputDirectory = Join-Path $PSScriptRoot "dist\$outputName"
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null

Write-Host "Restoring dependencies..."
& $dotnet restore $projectPath -r win-x64
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "Publishing the Windows x64 single-file app..."
& $dotnet publish $projectPath `
    -c Release `
    -r win-x64 `
    --self-contained $selfContained `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $outputDirectory `
    --no-restore
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$output = Join-Path $outputDirectory "FloatMate.exe"
Write-Host ""
Write-Host "Build complete: $output" -ForegroundColor Green
