$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "scripts\Common.ps1")

$builtApps = @(
    (Join-Path $PSScriptRoot "dist\FloatMate\FloatMate.exe"),
    (Join-Path $PSScriptRoot "dist\FloatMate-framework-dependent\FloatMate.exe")
)
foreach ($app in $builtApps) {
    if (Test-Path -LiteralPath $app) {
        Start-Process -FilePath $app
        exit 0
    }
}

$projectPath = Join-Path $PSScriptRoot "FloatMate\FloatMate.csproj"
$dotnet = Get-FloatMateDotNet -RepositoryRoot $PSScriptRoot
if (-not $dotnet) {
    throw ".NET 8 SDK was not found. Run install.cmd first."
}

& $dotnet run --project $projectPath
