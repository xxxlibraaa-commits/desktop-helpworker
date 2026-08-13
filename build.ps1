$ErrorActionPreference = "Stop"
$projectPath = Join-Path $PSScriptRoot "FloatMate\FloatMate.csproj"
$localSdk = Join-Path $env:LOCALAPPDATA "FloatMateDev\dotnet\dotnet.exe"
$dotnet = if (Test-Path $localSdk) { $localSdk } else { "dotnet" }

& $dotnet publish $projectPath -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true

$output = Join-Path $PSScriptRoot "FloatMate\bin\Release\net8.0-windows\win-x64\publish\FloatMate.exe"
Write-Host "Build complete: $output"
