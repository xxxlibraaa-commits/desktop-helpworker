$projectPath = Join-Path $PSScriptRoot "FloatMate\FloatMate.csproj"
$localSdk = Join-Path $env:LOCALAPPDATA "FloatMateDev\dotnet\dotnet.exe"
$dotnet = if (Test-Path $localSdk) { $localSdk } else { "dotnet" }
& $dotnet run --project $projectPath
