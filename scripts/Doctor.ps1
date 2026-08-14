$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot "Common.ps1")

$dotnet = Get-FloatMateDotNet -RepositoryRoot $repositoryRoot
$project = Join-Path $repositoryRoot "FloatMate\FloatMate.csproj"
$selfContainedApp = Join-Path $repositoryRoot "dist\FloatMate\FloatMate.exe"
$frameworkApp = Join-Path $repositoryRoot "dist\FloatMate-framework-dependent\FloatMate.exe"
$hasBuiltApp = (Test-Path -LiteralPath $selfContainedApp) -or (Test-Path -LiteralPath $frameworkApp)
$data = Join-Path $env:LOCALAPPDATA "FloatMate\data.json"

Write-Host "FloatMate environment check"
Write-Host "------------------"
Write-Host "Windows: $($env:OS -eq 'Windows_NT')"
Write-Host "Project file: $(Test-Path -LiteralPath $project)"
Write-Host ".NET: $(if ($dotnet) { $dotnet } else { 'not installed' })"
Write-Host ".NET 8 SDK: $(if ($dotnet) { Test-FloatMateDotNet8Sdk -DotNetPath $dotnet } else { $false })"
Write-Host "Built app: $hasBuiltApp"
Write-Host "Local data: $(Test-Path -LiteralPath $data)"

if (-not (Test-Path -LiteralPath $project)) { exit 1 }
if (-not $dotnet -or -not (Test-FloatMateDotNet8Sdk -DotNetPath $dotnet)) { exit 2 }
if (-not $hasBuiltApp) { exit 3 }
exit 0
